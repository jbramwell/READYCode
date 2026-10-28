// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Windows;
using ReadyCode.Assembler;
using ReadyCode.C64U;
using ReadyCode.Diff;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using ReadyCode.ViewModels;
using ReadyCode.Vice;

namespace ReadyCode.Views;

// Loading and running programs on a C64 Ultimate or in VICE, from the editor and from the explorers' context menus.
public partial class MainWindow
{
    #region Load / Run

    private enum TransferTarget { Vice, C64U }

    // "Load"/"Run" on C64U/VICE for a local top-level .prg/.ml/.asm file.
    private async void FileContextLoadC64U_Click(object sender, RoutedEventArgs e) => await FileContextLoadOrRunAsync(sender, TransferTarget.C64U, run: false);

    private async void FileContextLoadVice_Click(object sender, RoutedEventArgs e) => await FileContextLoadOrRunAsync(sender, TransferTarget.Vice, run: false);

    private async void FileContextRunC64U_Click(object sender, RoutedEventArgs e) => await FileContextLoadOrRunAsync(sender, TransferTarget.C64U, run: true);

    private async void FileContextRunVice_Click(object sender, RoutedEventArgs e) => await FileContextLoadOrRunAsync(sender, TransferTarget.Vice, run: true);

    private async Task FileContextLoadOrRunAsync(object sender, TransferTarget target, bool run)
    {
        var item = GetContextItem(sender);
        if (item == null) return;

        try
        {
            byte[] bytes = File.ReadAllBytes(item.FullPath);
            await LoadOrRunFileAsync(bytes, item.Kind, item.Name, target, run);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error reading file: {ex.Message}", "Load/Run File", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // "Load"/"Run" on C64U/VICE for a .prg/.ml/.asm entry inside a mounted .d64/.d81 in the
    // Explorer tree; its content is already in memory from when the disk image was expanded.
    private async void LocalDiskEntryContextLoadC64U_Click(object sender, RoutedEventArgs e) => await LocalDiskEntryLoadOrRunAsync(sender, TransferTarget.C64U, run: false);

    private async void LocalDiskEntryContextLoadVice_Click(object sender, RoutedEventArgs e) => await LocalDiskEntryLoadOrRunAsync(sender, TransferTarget.Vice, run: false);

    private async void LocalDiskEntryContextRunC64U_Click(object sender, RoutedEventArgs e) => await LocalDiskEntryLoadOrRunAsync(sender, TransferTarget.C64U, run: true);

    private async void LocalDiskEntryContextRunVice_Click(object sender, RoutedEventArgs e) => await LocalDiskEntryLoadOrRunAsync(sender, TransferTarget.Vice, run: true);

    private async Task LocalDiskEntryLoadOrRunAsync(object sender, TransferTarget target, bool run)
    {
        var item = GetContextItem(sender);
        if (item?.Content == null) return;

        await LoadOrRunFileAsync(item.Content, item.Kind, item.Name, target, run);
    }

    // "Load"/"Run" on C64U/VICE for a .prg/.ml/.asm file in the C64U tree, whether a real remote
    // file or a virtual entry inside a mounted disk image (item.Content already holds the
    // latter's bytes) - the same unification OpenC64UFileInEditorAsync already relies on.
    private async void C64UFileContextLoadC64U_Click(object sender, RoutedEventArgs e) => await C64UFileLoadOrRunAsync(sender, TransferTarget.C64U, run: false);

    private async void C64UFileContextLoadVice_Click(object sender, RoutedEventArgs e) => await C64UFileLoadOrRunAsync(sender, TransferTarget.Vice, run: false);

    private async void C64UFileContextRunC64U_Click(object sender, RoutedEventArgs e) => await C64UFileLoadOrRunAsync(sender, TransferTarget.C64U, run: true);

    private async void C64UFileContextRunVice_Click(object sender, RoutedEventArgs e) => await C64UFileLoadOrRunAsync(sender, TransferTarget.Vice, run: true);

    private async Task C64UFileLoadOrRunAsync(object sender, TransferTarget target, bool run)
    {
        var item = GetC64UContextItem(sender);
        if (item == null) return;
        if (item.Content == null && ViewModel.C64UFtp == null) return;

        using var _ = BeginBusyCursor();
        try
        {
            byte[] bytes = item.Content ?? await ViewModel.C64UFtp!.DownloadBytesAsync(item.FullPath);
            await LoadOrRunFileAsync(bytes, item.Kind, item.Name, target, run);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error downloading file: {ex.Message}", "Load/Run File", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Shared with CompareFileResolver (File Compare's own bytes-to-text resolution), which needs
    // the exact same BOM-stripping logic but must stay free of any dependency on this WPF-hosting
    // window - CompareFileResolver.DecodeSourceText is the one place that logic actually lives.
    private static string DecodeSourceText(byte[] bytes) => CompareFileResolver.DecodeSourceText(bytes);

    // Sends a file's bytes to VICE or the C64 Ultimate, without needing it open in a tab first.
    // An .asm file is assembled fresh from its source text (mirroring MainViewModel's own
    // TryBuildPrgData); a .prg/.ml file's bytes are already the final ready-to-transfer payload.
    // Shows an "Assembly Errors" dialog and stops, same as the active-tab Run/Load commands, if
    // assembling fails.
    private async Task LoadOrRunFileAsync(byte[] rawBytes, C64UFileKind kind, string displayName, TransferTarget target, bool run)
    {
        byte[] prgBytes;
        if (kind == C64UFileKind.Asm)
        {
            var result = new Asm6502Assembler().Assemble(
                DecodeSourceText(rawBytes), ViewModel.Settings.AsmOutputMode == "Standalone",
                (ushort)ViewModel.Settings.AsmDefaultOriginAddress);
            if (!result.Success)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.Errors.Select(err => $"Line {err.LineNumber}: {err.Message}")),
                    "Assembly Errors", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            prgBytes = result.PrgBytes!;
        }
        else
        {
            prgBytes = rawBytes;
        }

        if (target == TransferTarget.Vice)
        {
            if (run) await RunFileOnViceAsync(prgBytes, displayName);
            else await LoadFileOnViceAsync(prgBytes, displayName);
        }
        else
        {
            if (run) await RunFileOnC64UAsync(prgBytes, displayName);
            else await LoadFileOnC64UAsync(prgBytes, displayName);
        }
    }

    private async Task LoadFileOnViceAsync(byte[] prgBytes, string displayName)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.Settings.ViceEmulatorPath))
        {
            ViewModel.SetStatus("Please set the VICE emulator path in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            ViewModel.SetStatus($"Transferring '{displayName}' to VICE…");
            var client = new ViceClient(ViewModel.Settings.ViceMonitorHost, ViewModel.Settings.ViceMonitorPort);
            await client.TransferAsync(ViewModel.Settings.ViceEmulatorPath, prgBytes, displayName, ViewModel.Settings.ViceBringToForeground);
            ViewModel.SetStatus($"'{displayName}' transferred to VICE. Type RUN in the emulator to start it.");
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    // Runs a file's bytes on VICE: a native autostart RUN if the bytes have a runnable BASIC
    // entry point of their own, otherwise the same load-then-type-SYS trick RunOnViceAsync uses
    // for a standalone-origin active tab (see PrgConverter.NeedsSysToRun).
    private async Task RunFileOnViceAsync(byte[] prgBytes, string displayName)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.Settings.ViceEmulatorPath))
        {
            ViewModel.SetStatus("Please set the VICE emulator path in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            var client = new ViceClient(ViewModel.Settings.ViceMonitorHost, ViewModel.Settings.ViceMonitorPort);

            if (new PrgConverter().NeedsSysToRun(prgBytes, out ushort origin))
            {
                ViewModel.SetStatus($"Transferring '{displayName}' to VICE…");
                await client.TransferAsync(ViewModel.Settings.ViceEmulatorPath, prgBytes, displayName, ViewModel.Settings.ViceBringToForeground);

                await Task.Delay(MainViewModel.SysCommandDelay);
                await client.TypeAsync($"SYS{origin}\r");

                ViewModel.SetStatus($"'{displayName}' transferred and running on VICE (SYS{origin}).");
            }
            else
            {
                ViewModel.SetStatus($"Transferring '{displayName}' to VICE…");
                await client.RunAsync(ViewModel.Settings.ViceEmulatorPath, prgBytes, displayName, ViewModel.Settings.ViceBringToForeground);
                ViewModel.SetStatus($"'{displayName}' transferred and running on VICE.");
            }
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Transfer/program execution failed: {ex.Message}", StatusType.Error);
        }
    }

    private async Task LoadFileOnC64UAsync(byte[] prgBytes, string displayName)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.Settings.C64UUrl))
        {
            ViewModel.SetStatus("Please set the Commodore 64 Ultimate URL in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            ViewModel.SetStatus($"Transferring '{displayName}' to C64 Ultimate…");
            var client = new C64UltimateClient();
            await client.LoadPrgAsync(ViewModel.Settings.C64UUrl, prgBytes);
            ViewModel.SetStatus($"'{displayName}' transferred to C64 Ultimate.");
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    // Runs a file's bytes on the C64 Ultimate - see RunFileOnViceAsync for the same reasoning,
    // mirroring RunCurrentProgramAsync's own load_prg-then-typed-SYS trick.
    private async Task RunFileOnC64UAsync(byte[] prgBytes, string displayName)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.Settings.C64UUrl))
        {
            ViewModel.SetStatus("Please set the Commodore 64 Ultimate URL in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            var client = new C64UltimateClient();

            if (new PrgConverter().NeedsSysToRun(prgBytes, out ushort origin))
            {
                ViewModel.SetStatus($"Transferring '{displayName}' to C64 Ultimate…");
                await client.LoadPrgAsync(ViewModel.Settings.C64UUrl, prgBytes);

                await Task.Delay(MainViewModel.SysCommandDelay);
                await client.TypeAsync(ViewModel.Settings.C64UUrl, $"SYS{origin}\r");

                ViewModel.SetStatus($"'{displayName}' transferred and running on the C64 Ultimate (SYS{origin}).");
            }
            else
            {
                ViewModel.SetStatus($"Transferring '{displayName}' to C64 Ultimate…");
                await client.RunPrgAsync(ViewModel.Settings.C64UUrl, prgBytes);
                ViewModel.SetStatus($"'{displayName}' transferred and running on the C64 Ultimate.");
            }
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Transfer/program execution failed: {ex.Message}", StatusType.Error);
        }
    }

    private void EditorContextTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.C64UTransferCommand.CanExecute(null))
            ViewModel.C64UTransferCommand.Execute(null);
    }

    private void EditorContextRun_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.C64URunCommand.CanExecute(null))
            ViewModel.C64URunCommand.Execute(null);
    }

    private void EditorContextViceTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ViceTransferCommand.CanExecute(null))
            ViewModel.ViceTransferCommand.Execute(null);
    }

    private void EditorContextViceRun_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ViceRunCommand.CanExecute(null))
            ViewModel.ViceRunCommand.Execute(null);
    }

    #endregion
}
