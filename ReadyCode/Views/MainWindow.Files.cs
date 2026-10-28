// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ReadyCode.C64U;
using ReadyCode.IO;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

// File menu commands - New, Open, Save, Save As, Export/Import, recent files - and dropping files from Windows onto the window.
public partial class MainWindow
{
    #region New / Open / Save

    // A new BASIC tab defaults to the tokenized .prg format - the same target
    // SaveTabWithDialog/FileSaveAs_Click already default to (DefaultExt = ".prg") - so it
    // renders with the C64 font/PETSCII glyph substitution from the start rather than only
    // after the user actually saves it. File > New > BASIC File (.bas) opts into the
    // untokenized plain-text format instead, for a user who specifically wants that.
    private void FileNew_Click(object sender, RoutedEventArgs e) =>
        CreateNewTab(EditorLanguage.Basic, C64UFileKind.Prg, "Untitled.prg");

    private void FileNewBas_Click(object sender, RoutedEventArgs e) =>
        CreateNewTab(EditorLanguage.Basic, C64UFileKind.Bas, "Untitled.bas");

    private void FileNewAsm_Click(object sender, RoutedEventArgs e) =>
        CreateNewTab(EditorLanguage.Asm, C64UFileKind.Asm, "Untitled.asm");

    private void CreateNewTab(EditorLanguage language, C64UFileKind kind, string displayName)
    {
        var tab = new EditorTab { Language = language, Kind = kind, DisplayName = displayName };

        ViewModel.OpenTabs.Add(tab);
        ActivateTab(tab);

        // ActivateTab already focuses the editor, but invoking via the File menu lets the
        // menu's own focus-restore logic run afterward and steal it back - defer one more
        // focus call so the editor keeps focus regardless of invocation path (menu or Ctrl+N).
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => Editor.Focus());
    }

    private void FileOpen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Commodore 64 Programs (*.prg)|*.prg|BASIC Source (*.bas)|*.bas|6502 Assembly (*.asm;*.s)|*.asm;*.s|All Files (*.*)|*.*",
            Title = "Open File"
        };
        if (dialog.ShowDialog() == true)
            OpenFileByPath(dialog.FileName);
    }

    // Shows the Windows "busy" cursor for the scope of the returned IDisposable - used around
    // file-open operations that can take a second or two (large disk reads, PRG detokenization,
    // FTP downloads) so there's visual feedback while the UI is otherwise unresponsive. Reused by
    // every "open a file into a tab" entry point rather than each duplicating the
    // Mouse.OverrideCursor set/reset pair itself.
    private static IDisposable BeginBusyCursor()
    {
        Mouse.OverrideCursor = Cursors.Wait;
        return new BusyCursorScope();
    }

    private sealed class BusyCursorScope : IDisposable
    {
        public void Dispose()
        {
            // Deferred rather than reset immediately - the caller's synchronous work finishing
            // doesn't mean the UI has visually caught up yet. Assigning a virtualized
            // ItemsControl's ItemsSource (the hex grid) or a new AvalonEdit Document (the text
            // editor) only QUEUES that content's layout/render pass; it runs later on the
            // dispatcher, at Render priority. Resetting the cursor synchronously here would hide
            // it well before the actual (occasionally 1-2 second, for a large hex grid) work is
            // done. ApplicationIdle is lower priority than Render, Loaded, Background, and Input,
            // so this only runs once all of that pending work has actually been processed.
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => Mouse.OverrideCursor = null);
        }
    }

    // forceHex: when true, always opens the file's raw bytes as a hex tab regardless of its kind
    // - even a disk image or a confirmed-BASIC .prg, letting the hex editor be used on any file
    // type READYCode supports, not just the ones it auto-detects as needing it (a
    // machine-language .prg).
    private void OpenFileByPath(string path, bool forceHex = false)
    {
        using var _ = BeginBusyCursor();

        // A disk image isn't text - it's ~175KB-800KB of binary bytes. Reading it as UTF-8 and
        // running it through AvalonEdit's colorizers/diagnostics would be pathologically slow
        // (looks like a hang) rather than throw, since decoding arbitrary binary as text usually
        // "succeeds" with garbage. Expand it in the Explorer tree to see its contents instead -
        // unless the caller explicitly wants the raw bytes via forceHex.
        if (!forceHex && FileClassifier.Classify(path, isFolder: false).IsDiskImageKind())
        {
            ViewModel.SetStatus("Disk images can't be opened as text - expand it in the Explorer tree to see the programs inside.", StatusType.Warning);
            return;
        }

        byte[]? prgData = null;
        C64UFileKind kind;
        if (path.EndsWith(".prg", StringComparison.OrdinalIgnoreCase))
        {
            prgData = File.ReadAllBytes(path);
            kind = FileClassifier.Classify(path, isFolder: false, () => prgData);
        }
        else
        {
            kind = FileClassifier.Classify(path, isFolder: false);
        }
        bool wantsHexMode = forceHex || kind == C64UFileKind.Ml;

        // If already open in the requested mode, just activate that tab instead of opening a
        // duplicate. If it's open in the OTHER mode (e.g. previously opened via "Open as Hex",
        // now double-clicked for its normal view, or vice versa), reload that same tab's content
        // into the newly requested mode instead of silently doing nothing.
        var existing = ViewModel.OpenTabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (existing.IsHexMode == wantsHexMode)
            {
                ActivateTab(existing);
                return;
            }

            if (existing.IsModified)
            {
                ViewModel.SetStatus($"{Path.GetFileName(path)} has unsaved changes - save or close it before reopening in a different view.", StatusType.Warning);
                ActivateTab(existing);
                return;
            }
        }

        try
        {
            var tab = existing ?? new EditorTab
            {
                FilePath = path,
                Language = LanguageClassifier.Classify(path),
            };
            tab.Kind = kind;

            if (wantsHexMode)
            {
                tab.RawBytes = prgData ?? File.ReadAllBytes(path);
                tab.Document.Text = string.Empty;
            }
            else if (prgData != null)
            {
                tab.RawBytes = null;
                tab.Document.Text = PadLineNumbers(new PrgConverter().ConvertFromPrg(prgData));
            }
            else
            {
                tab.RawBytes = null;
                tab.Document.Text = File.ReadAllText(path, Encoding.UTF8);
            }

            if (existing == null)
                ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            tab.IsModified = false; // reset any spurious change event from document setup
            TrackRecentFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error opening file: {ex.Message}",
                "Open File Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Opens a "virtual" file found inside a mounted .d64 image in the Folder Explorer tree -
    // its content is already in memory, so no disk read is needed beyond what already happened
    // when the disk image itself was expanded. forceHex: see OpenFileByPath.
    private Task OpenLocalVirtualFileInEditor(FileTreeItem item, bool forceHex = false)
    {
        if (item.Content == null) return Task.CompletedTask;

        using var _ = BeginBusyCursor();

        bool wantsHexMode = forceHex || item.Kind == C64UFileKind.Ml;

        // Virtual entries have no real FilePath to dedupe on, so use the disk image's own path
        // plus the entry's name as a stable identity instead - re-activate an already-open tab
        // rather than opening a duplicate. If it's open in the OTHER mode, reload it into the
        // newly requested mode instead of silently doing nothing - see OpenFileByPath.
        string sourceId = $"{item.SourcePath}!{item.Name}";
        var existingTab = ViewModel.OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
        if (existingTab != null)
        {
            if (existingTab.IsHexMode == wantsHexMode)
            {
                ActivateTab(existingTab);
                return Task.CompletedTask;
            }

            if (existingTab.IsModified)
            {
                ViewModel.SetStatus($"{item.Name} has unsaved changes - save or close it before reopening in a different view.", StatusType.Warning);
                ActivateTab(existingTab);
                return Task.CompletedTask;
            }
        }

        try
        {
            var tab = existingTab ?? new EditorTab { DisplayName = item.Name, VirtualSourceId = sourceId };
            tab.Kind = item.Kind;
            tab.Language = item.Kind == C64UFileKind.Asm ? EditorLanguage.Asm : EditorLanguage.Basic;

            if (wantsHexMode)
            {
                tab.RawBytes = item.Content;
                tab.Document.Text = string.Empty;
            }
            else
            {
                tab.RawBytes = null;
                tab.Document.Text = item.Kind == C64UFileKind.Prg
                    ? PadLineNumbers(new PrgConverter().ConvertFromPrg(item.Content))
                    : DecodeSourceText(item.Content);
            }

            if (existingTab == null)
                ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            tab.IsModified = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening file: {ex.Message}", "Open File Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private void FileSave_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab == null) return;
        if (ViewModel.ActiveTab.VirtualSourceId != null)
        {
            _ = SaveVirtualTabAsync(ViewModel.ActiveTab);
            return;
        }
        if (string.IsNullOrEmpty(ViewModel.ActiveTab.FilePath))
        {
            FileSaveAs_Click(sender, e);
            return;
        }
        SaveFile(ViewModel.ActiveTab.FilePath);
    }

    // Writes a virtual tab's (a program opened from inside a mounted .d64/.d81) edits back into
    // its source disk image, re-tokenizing BASIC or writing Asm source as plain text - mirrors
    // SaveFile's language check. Routes to a local file write or an FTP download/modify/re-upload
    // round trip depending on whether SourcePath is a local file or a C64 Ultimate remote path.
    private async Task SaveVirtualTabAsync(EditorTab tab)
    {
        var parts = tab.VirtualSourceId!.Split('!', 2);
        if (parts.Length != 2) return;
        string sourcePath = parts[0];
        string entryName = parts[1];

        byte[] newContent = tab.IsHexMode
            ? tab.RawBytes!
            : tab.Language == EditorLanguage.Asm
                ? Encoding.UTF8.GetBytes(tab.Document.Text)
                : new PrgConverter().ConvertToPrg(tab.Document.Text);

        if (File.Exists(sourcePath))
        {
            try
            {
                byte[] diskBytes = File.ReadAllBytes(sourcePath);
                var kind = FileClassifier.Classify(sourcePath, isFolder: false);
                byte[] updated = DiskImage.ForKind(kind).ReplaceEntry(diskBytes, entryName, newContent);
                AtomicFile.WriteAllBytes(sourcePath, updated);

                tab.IsModified = false;
                ViewModel.SetStatus("File saved.");
                RefreshDiskImageNode(sourcePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving to disk image: {ex.Message}", "Save File Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        if (ViewModel.C64UFtp == null)
        {
            MessageBox.Show(
                "Not connected to the C64 Ultimate, so this disk image can't be saved to. Connect in the C64U Explorer panel, then try again.",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            byte[] diskBytes = await ViewModel.C64UFtp.DownloadBytesAsync(sourcePath);
            var kind = FileClassifier.Classify(sourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).ReplaceEntry(diskBytes, entryName, newContent);
            await ViewModel.C64UFtp.UploadBytesAsync(sourcePath, updated);

            tab.IsModified = false;
            ViewModel.SetStatus("File saved.");
            await RefreshC64UNode(sourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving to disk image: {ex.Message}", "Save File Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FileSaveAs_Click(object sender, RoutedEventArgs e)
    {
        var (filter, defaultExt) = GetSaveFilterFor(ViewModel.ActiveTab);
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = "Save File As",
            DefaultExt = defaultExt,
            AddExtension = true
        };
        if (!string.IsNullOrEmpty(ViewModel.CurrentFilePath))
            dialog.FileName = Path.GetFileNameWithoutExtension(ViewModel.CurrentFilePath);

        if (dialog.ShowDialog() == true)
        {
            ViewModel.CurrentFilePath = dialog.FileName;
            if (ViewModel.ActiveTab != null)
            {
                ViewModel.ActiveTab.Language = LanguageClassifier.Classify(ViewModel.CurrentFilePath!);
                ViewModel.ActiveTab.Kind = FileClassifier.Classify(ViewModel.CurrentFilePath!, isFolder: false);
                ApplyLineTransformersForLanguage(ViewModel.ActiveTab.Language);

                // Saving a disassembly listing turns it into an ordinary editable .asm file from
                // now on - re-disassembling in place isn't offered (see EditorTab.IsDisassemblyMode).
                if (ViewModel.ActiveTab.IsDisassemblyMode)
                {
                    ViewModel.ActiveTab.IsDisassemblyMode = false;
                    ViewModel.ActiveTab.DisassemblyLineAddresses = null;
                    DisassemblyToolbar.Visibility = Visibility.Collapsed;
                    Editor.IsReadOnly = false;
                    _asmLineNumberMargin.LineAddresses = null;
                    _asmLineNumberMargin.InvalidateMeasure();
                    _asmLineNumberMargin.InvalidateVisual();
                }
            }
            SaveFile(ViewModel.CurrentFilePath!);
            RefreshExplorerForSavedFile(ViewModel.CurrentFilePath!);
        }
    }

    // Writes the active tab's content to filePath - plain text for assembly source or .bas
    // BASIC listings, or BASIC-tokenized PRG bytes otherwise (see
    // PrgConverter.ShouldTokenizeOnSave). Must check this rather than always tokenizing, or
    // saving a .asm/.bas file would silently overwrite it with binary PRG data.
    private void SaveFile(string filePath)
    {
        try
        {
            if (ViewModel.ActiveTab?.IsHexMode == true)
            {
                byte[] rawBytes = ViewModel.ActiveTab.RawBytes!;
                AtomicFile.WriteAllBytes(filePath, rawBytes);
                ViewModel.IsModified = false;
                TrackRecentFile(filePath);
                ViewModel.SetStatus($"File saved: {rawBytes.Length:N0} bytes.");
            }
            else if (ViewModel.ActiveTab != null && !PrgConverter.ShouldTokenizeOnSave(ViewModel.ActiveTab.Language, filePath))
            {
                AtomicFile.WriteAllText(filePath, Editor.Text, Encoding.UTF8);
                ViewModel.IsModified = false;
                TrackRecentFile(filePath);
                ViewModel.SetStatus("File saved.");
                ActivateErrorsPanelIfDiagnostics(ViewModel.ActiveTab!);
            }
            else
            {
                var converter = new PrgConverter();
                var prgData = converter.ConvertToPrg(Editor.Text);
                AtomicFile.WriteAllBytes(filePath, prgData);
                ViewModel.IsModified = false;
                TrackRecentFile(filePath);
                ViewModel.SetStatus($"File saved: {prgData.Length:N0} tokenized bytes.");
                ActivateErrorsPanelIfDiagnostics(ViewModel.ActiveTab!);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error saving file: {ex.Message}",
                "Save File Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // After a Save/Save As writes a file to a brand-new path, refresh just the affected
    // explorer folder (or root) so it appears without collapsing other expanded folders,
    // and select/focus it since it's the active tab.
    private void RefreshExplorerForSavedFile(string filePath)
    {
        string folder = ViewModel.Project.RootPath;
        if (string.IsNullOrEmpty(folder)) return;

        string? dir = Path.GetDirectoryName(filePath);
        if (dir == null) return;

        string normalizedFolder = folder.TrimEnd(Path.DirectorySeparatorChar);
        bool isFolderItself = string.Equals(dir, normalizedFolder, StringComparison.OrdinalIgnoreCase);
        bool isNestedInFolder = dir.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!isFolderItself && !isNestedInFolder) return;

        RefreshAfterCreate(dir, filePath);
    }

    // Picks a Save/Save As dialog's file-type filter and default extension for a tab, keyed off
    // its Kind rather than just Language - a Basic-language tab can be either the tokenized .prg
    // format or the untokenized .bas format (see EditorTab.Kind), and the dialog should default
    // to whichever one the tab already is instead of always assuming .prg.
    private static (string Filter, string DefaultExt) GetSaveFilterFor(EditorTab? tab)
    {
        if (tab?.Language == EditorLanguage.Asm)
            return ("6502 Assembly (*.asm;*.s)|*.asm;*.s|All Files (*.*)|*.*", ".asm");

        if (tab?.Kind == C64UFileKind.Bas)
            return ("BASIC Source (*.bas)|*.bas|Commodore 64 Programs (*.prg)|*.prg|All Files (*.*)|*.*", ".bas");

        return ("Commodore 64 Programs (*.prg)|*.prg|BASIC Source (*.bas)|*.bas|All Files (*.*)|*.*", ".prg");
    }

    // Saves a tab (possibly not the active one); prompts for path if untitled.
    // Returns false if the user cancels.
    private bool SaveTabWithDialog(EditorTab tab)
    {
        if (string.IsNullOrEmpty(tab.FilePath))
        {
            var (filter, defaultExt) = GetSaveFilterFor(tab);
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                Title = "Save File",
                DefaultExt = defaultExt,
                AddExtension = true
            };
            if (dialog.ShowDialog() != true) return false;
            tab.FilePath = dialog.FileName;
            tab.Language = LanguageClassifier.Classify(tab.FilePath);
            tab.Kind = FileClassifier.Classify(tab.FilePath, isFolder: false);
            if (ReferenceEquals(tab, ViewModel.ActiveTab))
                ApplyLineTransformersForLanguage(tab.Language);

            // Saving a disassembly listing turns it into an ordinary editable .asm file from now
            // on. Only touch the shared toolbar/Editor UI if this tab actually is the one
            // currently showing - SaveTabWithDialog can also save a tab that isn't active.
            if (tab.IsDisassemblyMode)
            {
                tab.IsDisassemblyMode = false;
                tab.DisassemblyLineAddresses = null;
                if (ReferenceEquals(tab, ViewModel.ActiveTab))
                {
                    DisassemblyToolbar.Visibility = Visibility.Collapsed;
                    Editor.IsReadOnly = false;
                    _asmLineNumberMargin.LineAddresses = null;
                    _asmLineNumberMargin.InvalidateMeasure();
                    _asmLineNumberMargin.InvalidateVisual();
                }
            }
        }
        try
        {
            if (!PrgConverter.ShouldTokenizeOnSave(tab.Language, tab.FilePath))
            {
                AtomicFile.WriteAllText(tab.FilePath, tab.Document.Text, Encoding.UTF8);
                tab.IsModified = false;
                TrackRecentFile(tab.FilePath);
                RefreshExplorerForSavedFile(tab.FilePath);
                ViewModel.SetStatus("File saved.");
                ActivateErrorsPanelIfDiagnostics(tab);
            }
            else
            {
                var converter = new PrgConverter();
                var prgData = converter.ConvertToPrg(tab.Document.Text);
                AtomicFile.WriteAllBytes(tab.FilePath, prgData);
                tab.IsModified = false;
                TrackRecentFile(tab.FilePath);
                RefreshExplorerForSavedFile(tab.FilePath);
                ViewModel.SetStatus($"File saved: {prgData.Length:N0} tokenized bytes.");
                ActivateErrorsPanelIfDiagnostics(tab);
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving file: {ex.Message}", "Save File Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void FileExport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Editor.Text))
        {
            MessageBox.Show("There is no code to export.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Export as Text File",
            DefaultExt = ".txt",
            AddExtension = true
        };
        if (!string.IsNullOrEmpty(ViewModel.CurrentFilePath))
            dialog.FileName = Path.GetFileNameWithoutExtension(ViewModel.CurrentFilePath);

        if (dialog.ShowDialog() == true)
        {
            try { AtomicFile.WriteAllText(dialog.FileName, Editor.Text, Encoding.UTF8); }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting file: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FileImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Import Text File"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var tab = new EditorTab();
                tab.Document.Text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                ViewModel.OpenTabs.Add(tab);
                ActivateTab(tab);
                tab.IsModified = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing file: {ex.Message}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FileExit_Click(object sender, RoutedEventArgs e) => Close();

    private void TrackRecentFile(string path)
    {
        ViewModel.Settings.AddRecentFile(path);
        ViewModel.Settings.Save();
        RefreshRecentFiles();
    }

    private void RefreshRecentFiles()
    {
        RecentFilesMenuItem.Items.Clear();
        var files = ViewModel.Settings.RecentFiles;
        if (files.Count == 0)
        {
            RecentFilesMenuItem.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "(none)",
                IsEnabled = false
            });
            return;
        }
        foreach (string path in files)
        {
            string capturedPath = path;
            var item = new System.Windows.Controls.MenuItem
            {
                Header = Path.GetFileName(path),
                ToolTip = path
            };
            item.Click += (_, _) => OpenFileByPath(capturedPath);
            RecentFilesMenuItem.Items.Add(item);
        }
    }

    #endregion

    #region Drag and Drop from Windows

    // Dropping files from Explorer is only allowed when every dropped file is a .prg - the
    // drop is rejected as a whole (no partial-open) if any other file type is included.
    // Dropping from Windows Explorer while hovering a folder or disk-image row in either tree
    // copies/uploads/embeds into it (any file type - see Window_PreviewDrop); hovering anything
    // else falls back to the original behavior of opening every dropped file as a new tab, only
    // when all of them are .prg (mixed selections are rejected wholesale, no partial-open).
    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        e.Handled = true;
        if (FindOsDropTarget(e) != null)
        {
            e.Effects = DragDropEffects.Copy;
            return;
        }

        ClearOsDropHighlight();
        e.Effects = IsAllPrgFileDrop(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        e.Handled = true;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        object? target = FindOsDropTarget(e);
        ClearOsDropHighlight();

        switch (target)
        {
            case FileTreeItem localFolder when localFolder.IsFolder:
                CopyFilesIntoLocalFolder(paths, localFolder);
                return;
            case FileTreeItem localDiskImage when localDiskImage.Kind.IsDiskImageKind():
                foreach (string path in paths)
                {
                    if (RejectFolderForDiskImage(path)) continue;
                    AddFileToLocalDiskImage(path, localDiskImage);
                }
                return;
            case C64UFileItem c64uFolder when c64uFolder.IsFolder:
                await UploadFilesToC64UFolderAsync(paths, c64uFolder.FullPath);
                return;
            case C64UFileItem c64uDiskImage when c64uDiskImage.Kind.IsDiskImageKind():
                foreach (string path in paths)
                {
                    if (RejectFolderForDiskImage(path)) continue;
                    await AddFileToC64UDiskImageAsync(path, c64uDiskImage);
                }
                return;
        }

        if (!IsAllPrgFileDrop(e, out _)) return;
        foreach (string path in paths)
            OpenFileByPath(path);
    }

    private static bool RejectFolderForDiskImage(string path)
    {
        if (!Directory.Exists(path)) return false;
        MessageBox.Show($"\"{Path.GetFileName(path)}\" is a folder and can't be added to a disk image.",
            "Add File Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        return true;
    }

    private static bool IsAllPrgFileDrop(DragEventArgs e, out string[] paths)
    {
        paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        return paths.Length > 0 && paths.All(p => p.EndsWith(".prg", StringComparison.OrdinalIgnoreCase));
    }

    // Copies each dropped file/folder into targetFolder, matching PasteToFolder's copy-mode
    // collision handling (skip with a warning rather than overwrite).
    private void CopyFilesIntoLocalFolder(string[] sourcePaths, FileTreeItem targetFolder)
    {
        bool anyCopied = false;
        foreach (string sourcePath in sourcePaths)
        {
            string itemName    = Path.GetFileName(sourcePath);
            string destination = Path.Combine(targetFolder.FullPath, itemName);
            bool isFolder       = Directory.Exists(sourcePath);
            if ((isFolder && Directory.Exists(destination)) || (!isFolder && File.Exists(destination)))
            {
                MessageBox.Show($"A {(isFolder ? "folder" : "file")} named \"{itemName}\" already exists in \"{targetFolder.Name}\".",
                    "Copy Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }
            try
            {
                if (isFolder) CopyDirectoryRecursive(sourcePath, destination);
                else          File.Copy(sourcePath, destination);
                anyCopied = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy \"{itemName}\":\n{ex.Message}", "Copy Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (!anyCopied) return;
        targetFolder.RefreshChildren();
        targetFolder.IsExpanded = true;
    }

    // Uploads each dropped file as-is (no assemble/tokenize - a C64U folder can hold source
    // files directly, unlike a disk image) into targetDir, mirroring C64UUploadToolbar_Click's
    // single-file flow. Folders aren't supported (no recursive-FTP-upload capability exists yet).
    private async Task UploadFilesToC64UFolderAsync(string[] sourcePaths, string targetDir)
    {
        if (ViewModel.C64UFtp == null) return;

        bool anyUploaded = false;
        foreach (string sourcePath in sourcePaths)
        {
            string itemName = Path.GetFileName(sourcePath);
            if (Directory.Exists(sourcePath))
            {
                MessageBox.Show($"\"{itemName}\" is a folder - uploading folders to the C64 Ultimate isn't supported yet.",
                    "Upload Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }
            try
            {
                var bytes = File.ReadAllBytes(sourcePath);
                string remotePath = CombineC64UPath(targetDir, itemName);
                await ViewModel.C64UFtp.UploadBytesAsync(remotePath, bytes);
                anyUploaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not upload \"{itemName}\":\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (!anyUploaded) return;
        await RefreshC64UNode(targetDir);
        ViewModel.SetStatus($"Uploaded {sourcePaths.Length:N0} file(s) to the C64 Ultimate.");
    }

    // Finds the folder or disk-image row (in either tree) currently under an OS file drag, if
    // any, and highlights it the same way an internal tree drag-drop does. Window-level
    // PreviewDragOver/Drop claim every OS file drop (DataFormats.FileDrop) before the
    // per-TreeViewItem DragOver/Drop handlers ever see it, so this reimplements the same
    // hit-testing/highlight bookkeeping here instead of relying on those.
    private object? FindOsDropTarget(DragEventArgs e)
    {
        object? dataContext = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext;

        if (dataContext is FileTreeItem localItem && (localItem.IsFolder || localItem.Kind.IsDiskImageKind()))
        {
            SetOsDropHighlight(localItem);
            return localItem;
        }
        if (dataContext is C64UFileItem c64uItem && !c64uItem.IsVirtual && (c64uItem.IsFolder || c64uItem.Kind.IsDiskImageKind()))
        {
            SetOsDropHighlight(c64uItem);
            return c64uItem;
        }

        ClearOsDropHighlight();
        return null;
    }

    private void SetOsDropHighlight(FileTreeItem target)
    {
        if (_c64uCurrentDropTarget != null) { _c64uCurrentDropTarget.IsDropTarget = false; _c64uCurrentDropTarget = null; }
        if (ReferenceEquals(_currentDropTarget, target)) return;
        if (_currentDropTarget != null) _currentDropTarget.IsDropTarget = false;
        _currentDropTarget = target;
        target.IsDropTarget = true;
    }

    private void SetOsDropHighlight(C64UFileItem target)
    {
        if (_currentDropTarget != null) { _currentDropTarget.IsDropTarget = false; _currentDropTarget = null; }
        if (ReferenceEquals(_c64uCurrentDropTarget, target)) return;
        if (_c64uCurrentDropTarget != null) _c64uCurrentDropTarget.IsDropTarget = false;
        _c64uCurrentDropTarget = target;
        target.IsDropTarget = true;
    }

    private void ClearOsDropHighlight()
    {
        if (_currentDropTarget != null) { _currentDropTarget.IsDropTarget = false; _currentDropTarget = null; }
        if (_c64uCurrentDropTarget != null) { _c64uCurrentDropTarget.IsDropTarget = false; _c64uCurrentDropTarget = null; }
    }

    #endregion
}
