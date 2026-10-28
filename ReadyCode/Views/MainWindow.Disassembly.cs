// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Windows;
using System.Windows.Threading;
using ReadyCode.Assembler;
using ReadyCode.C64U;
using ReadyCode.Models;
using ReadyCode.ViewModels;
using ReadyCode.Vice;

namespace ReadyCode.Views;

// Disassembling machine-language programs - from a C64 Ultimate, VICE, or a file - into a disassembly tab.
public partial class MainWindow
{
    #region Disassembly

    private void C64UDisassemble_Click(object sender, RoutedEventArgs e) => OpenDisassemblyTab(DisassemblySource.C64U);

    private void ViceDisassemble_Click(object sender, RoutedEventArgs e) => OpenDisassemblyTab(DisassemblySource.Vice);

    // Opens a new read-only disassembly tab with the address-range toolbar visible, ready for
    // the user to fill in Start/End and click Disassemble. The tab has no FilePath until Saved
    // As - see FileSaveAs_Click/SaveTabWithDialog for where IsDisassemblyMode gets cleared once
    // that happens, turning it into an ordinary editable .asm tab.
    private void OpenDisassemblyTab(DisassemblySource source)
    {
        var tab = new EditorTab
        {
            Language = EditorLanguage.Asm,
            Kind = C64UFileKind.Asm,
            DisplayName = source == DisassemblySource.Vice ? "Disassembly (VICE).asm" : "Disassembly (C64U).asm",
            IsDisassemblyMode = true,
            DisassemblySource = source,
        };
        tab.Document.Text = "; Enter a Start and End address above, then click Disassemble.";

        ViewModel.OpenTabs.Add(tab);
        ActivateTab(tab);

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => DisassemblyToolbar.FocusStartAddress());
    }

    // Reads the requested memory range from the tab's DisassemblySource (the C64 Ultimate or a
    // running VICE instance) and disassembles it into the active tab's document. Captures the
    // tab up front (rather than reusing Editor.Document after the await) so a slow request that
    // completes after the user switches tabs still lands in the right place instead of
    // overwriting whatever tab is active by then.
    private async void DisassemblyToolbar_DisassembleRequested(object? sender, EventArgs e)
    {
        var tab = ViewModel.ActiveTab;
        if (tab == null || !tab.IsDisassemblyMode) return;

        if (!DisassemblyToolbar.TryGetAddressRange(out ushort start, out ushort end, out string? rangeError))
        {
            ViewModel.SetStatus(rangeError!, StatusType.Error);
            return;
        }

        bool useVice = tab.DisassemblySource == DisassemblySource.Vice;
        if (useVice && string.IsNullOrWhiteSpace(ViewModel.Settings.ViceEmulatorPath))
        {
            ViewModel.SetStatus("Please set the VICE emulator path in Settings - Preferences first.", StatusType.Error);
            return;
        }
        if (!useVice && string.IsNullOrWhiteSpace(ViewModel.Settings.C64UUrl))
        {
            ViewModel.SetStatus("C64U URL not set. Go to Preferences > Settings to configure it.", StatusType.Error);
            return;
        }

        int length = end - start + 1;

        try
        {
            using var _ = BeginBusyCursor();
            ViewModel.SetStatus(useVice
                ? $"Reading {length:N0} bytes from VICE…"
                : $"Reading {length:N0} bytes from the C64 Ultimate…");

            byte[] bytes = useVice
                ? await new ViceClient(ViewModel.Settings.ViceMonitorHost, ViewModel.Settings.ViceMonitorPort).ReadMemoryAsync(start, length)
                : await new C64UltimateClient().ReadMemoryAsync(ViewModel.Settings.C64UUrl, start, length);

            var result = new Asm6502Disassembler().Disassemble(
                bytes, start, ViewModel.Settings.AsmMnemonicIndentColumn, ViewModel.Settings.AsmCommentAlignColumn);
            tab.Document.Text = result.Source;
            tab.DisassemblyLineAddresses = result.LineAddresses;
            tab.IsModified = false;

            // Only touch the shared gutter margin if this tab is still the one showing - a slow
            // request that completes after the user switched tabs shouldn't repaint whatever
            // tab is active by then.
            if (ReferenceEquals(tab, ViewModel.ActiveTab))
            {
                _asmLineNumberMargin.LineAddresses = result.LineAddresses;
                _asmLineNumberMargin.InvalidateMeasure();
                _asmLineNumberMargin.InvalidateVisual();
            }

            ViewModel.SetStatus($"Disassembled {length:N0} bytes from ${start:X4}-${end:X4}.");
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Disassembly failed: {ex.Message}", StatusType.Error);
        }
    }

    // "Disassemble file" - local top-level .ml file in the Explorer tree.
    private void FileContextDisassemble_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;

        try
        {
            DisassembleFileBytes(File.ReadAllBytes(item.FullPath), item.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error disassembling file: {ex.Message}", "Disassemble File",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // "Disassemble file" - virtual .ml entry inside a mounted .d64/.d81 in the Explorer tree;
    // its content is already in memory from when the disk image was expanded.
    private void LocalDiskEntryContextDisassemble_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item?.Content == null) return;

        DisassembleFileBytes(item.Content, item.Name);
    }

    // "Disassemble file" - a .ml file in the C64U tree, whether a real remote file or a virtual
    // entry inside a mounted disk image (item.Content already holds the latter's bytes).
    private async void C64UFileContextDisassemble_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item == null) return;
        if (item.Content == null && ViewModel.C64UFtp == null) return;

        using var _ = BeginBusyCursor();
        try
        {
            byte[] bytes = item.Content ?? await ViewModel.C64UFtp!.DownloadBytesAsync(item.FullPath);
            DisassembleFileBytes(bytes, item.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error disassembling file: {ex.Message}", "Disassemble File",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Decodes a standalone .prg's 2-byte little-endian load-address header, disassembles the
    // remaining bytes as machine code, and opens the result as a new, ordinary editable .asm tab -
    // unlike the live-memory "Disassemble at..." flow (see OpenDisassemblyTab), there's no address
    // range to pick since the whole file is already in hand, so no read-only toolbar is needed.
    private void DisassembleFileBytes(byte[] prgBytes, string displayName)
    {
        if (prgBytes.Length < 2)
        {
            MessageBox.Show($"'{displayName}' is too small to disassemble.", "Disassemble File",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = PrgFileDisassembler.Disassemble(
            prgBytes, ViewModel.Settings.AsmMnemonicIndentColumn, ViewModel.Settings.AsmCommentAlignColumn);

        var tab = new EditorTab
        {
            Language = EditorLanguage.Asm,
            Kind = C64UFileKind.Asm,
            DisplayName = $"{Path.GetFileNameWithoutExtension(displayName)} (Disassembled).asm",
        };

        // The gutter shows the real memory address of each disassembled line, same as the
        // live-memory "Disassemble at..." flow - shifted past the stub comment's own lines, which
        // have no address of their own, when one was prepended above.
        if (result.StubCommentLines != null)
        {
            tab.Document.Text = string.Join(Environment.NewLine, result.StubCommentLines) + Environment.NewLine + result.Source;
            tab.DisassemblyLineAddresses = result.LineAddresses.ToDictionary(
                kvp => kvp.Key + result.StubCommentLines.Count, kvp => kvp.Value);
        }
        else
        {
            tab.Document.Text = result.Source;
            tab.DisassemblyLineAddresses = result.LineAddresses;
        }

        ViewModel.OpenTabs.Add(tab);
        ActivateTab(tab);
    }

    #endregion
}
