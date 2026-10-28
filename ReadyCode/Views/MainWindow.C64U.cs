// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
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

// The C64 Ultimate FTP Explorer panel: browsing, context menus, disk images, drag and drop, and inline rename / create.
public partial class MainWindow
{
    #region C64U FTP Explorer

    private async void C64UConnect_Click(object sender, RoutedEventArgs e) => await ViewModel.ConnectToC64UAsync();

    private void C64USettingsHeader_Click(object sender, RoutedEventArgs e) => OpenSettingsDialog("c64u");

    private async void C64URefreshToolbar_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshC64UFolderAsync();

    private async void C64UNewFolderToolbar_Click(object sender, RoutedEventArgs e)
    {
        var selected = C64UFileTree.SelectedItem as C64UFileItem;
        C64UFileItem? parentFolder = selected == null ? null
            : selected.IsFolder ? selected
            : FindC64UParentFolder(selected);
        await CreateC64UNewFolderInlineAsync(parentFolder);
    }

    private async void C64UUploadToolbar_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.C64UFtp == null) return;

        var selected = C64UFileTree.SelectedItem as C64UFileItem;
        string targetDir = selected == null ? "/"
            : selected.IsFolder ? selected.FullPath
            : GetC64UParentPath(selected.FullPath);

        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog() != true) return;

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            string remotePath = CombineC64UPath(targetDir, Path.GetFileName(dialog.FileName));
            await ViewModel.C64UFtp.UploadBytesAsync(remotePath, bytes);
            await RefreshC64UNode(targetDir);
            ViewModel.SetStatus($"Uploaded \"{Path.GetFileName(dialog.FileName)}\" to the C64 Ultimate.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not upload file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void C64UFileTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as C64UFileItem;
        if (item == null || item.IsFolder || !item.IsOpenable) return;
        _ = OpenC64UFileInEditorAsync(item);
        e.Handled = true;
    }

    private void C64UFileTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (C64UFileTree.SelectedItem is not C64UFileItem item || item.IsRenaming) return;

        if (e.Key == Key.F2)
        {
            BeginC64UInlineRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _ = DeleteC64UItemAsync(item);
            e.Handled = true;
        }
    }

    // ── Folder context menu ───────────────────────────────────────────────────

    private async void C64UFolderContextNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await CreateC64UNewFolderInlineAsync(item);
    }

    private async void C64UFolderContextNewD64_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await CreateC64UNewDiskImageInlineAsync(item, C64UFileKind.D64);
    }

    private async void C64UFolderContextNewD81_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await CreateC64UNewDiskImageInlineAsync(item, C64UFileKind.D81);
    }

    private async void C64UFolderContextRefresh_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await item.RefreshChildrenAsync();
    }

    // ── File context menu ─────────────────────────────────────────────────────

    private async void C64UFileContextOpen_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await OpenC64UFileInEditorAsync(item);
    }

    // "Open as Hex" - see FileContextOpenAsHex_Click. OpenC64UFileInEditorAsync already handles
    // both a real remote file (including a disk image itself) and a virtual entry inside one
    // uniformly, so this single handler covers every C64U context menu that offers it.
    private async void C64UFileContextOpenAsHex_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await OpenC64UFileInEditorAsync(item, forceHex: true);
    }

    private async void C64UFileContextMountA_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await ViewModel.MountC64UDriveAsync("a", item.FullPath);
    }

    private async void C64UFileContextMountB_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await ViewModel.MountC64UDriveAsync("b", item.FullPath);
    }

    private async void C64UEjectDriveA_Click(object sender, RoutedEventArgs e) => await ViewModel.EjectC64UDriveAsync("a");

    private async void C64UEjectDriveB_Click(object sender, RoutedEventArgs e) => await ViewModel.EjectC64UDriveAsync("b");

    private async void C64UFileContextDownload_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item == null || ViewModel.C64UFtp == null) return;

        var dialog = new SaveFileDialog { FileName = item.Name };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var bytes = await ViewModel.C64UFtp.DownloadBytesAsync(item.FullPath);
            AtomicFile.WriteAllBytes(dialog.FileName, bytes);
            ViewModel.SetStatus($"Downloaded \"{item.Name}\".");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not download file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // "Add File..." on a .d64/.d81 node itself - lets the user pick a local .prg (embedded as-is,
    // since a .prg file's bytes already are the on-disk PRG format) or .bas (tokenized first via
    // the same converter SaveFile uses) to add into the image mounted over FTP.
    private async void C64UDiskImageContextAddFile_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item == null || ViewModel.C64UFtp == null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Commodore 64 Programs (*.prg)|*.prg|BASIC Source (*.bas)|*.bas|6502 Assembly (*.asm;*.s)|*.asm;*.s|All Files (*.*)|*.*",
            Title = "Add File to Disk Image"
        };
        if (dialog.ShowDialog() != true) return;

        await AddFileToC64UDiskImageAsync(dialog.FileName, item);
    }

    // Builds prgData from a local file path and adds it to a C64U disk image mounted over FTP -
    // the shared core behind both "Add File to Disk Image" and dragging a local file onto a C64U
    // disk image row.
    private async Task AddFileToC64UDiskImageAsync(string sourcePath, C64UFileItem diskImageItem)
    {
        if (ViewModel.C64UFtp == null || !TryBuildDiskEntryPrgData(sourcePath, out byte[]? prgData)) return;

        string entryName = Path.GetFileNameWithoutExtension(sourcePath);
        await AddEntryToC64UDiskImageAsync(prgData!, entryName, diskImageItem);
    }

    // Adds already-built prgData to a C64U disk image mounted over FTP - the shared core behind
    // AddFileToC64UDiskImageAsync (source is a local file path) and dragging an already-uploaded
    // C64U file onto a disk image row (source is bytes already in hand/downloaded).
    private async Task AddEntryToC64UDiskImageAsync(byte[] prgData, string entryName, C64UFileItem diskImageItem)
    {
        if (ViewModel.C64UFtp == null) return;

        try
        {
            var entryKind = FileClassifier.Classify(entryName + ".prg", isFolder: false, () => prgData);

            byte[] diskBytes = await ViewModel.C64UFtp.DownloadBytesAsync(diskImageItem.FullPath);
            var diskKind = FileClassifier.Classify(diskImageItem.FullPath, isFolder: false);
            byte[] updated = DiskImage.ForKind(diskKind).AddEntry(diskBytes, entryName, entryKind, prgData);
            await ViewModel.C64UFtp.UploadBytesAsync(diskImageItem.FullPath, updated);

            await RefreshC64UNode(diskImageItem.FullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not add file to disk image:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Reuses the same inline-rename UI as real files/folders (BeginC64UInlineRename/
    // CommitC64URename) - routes through CommitC64UVirtualEntryRenameAsync since a virtual entry
    // has no real FullPath to FTP-rename.
    private void C64UDiskEntryContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) BeginC64UInlineRename(item);
    }

    private async void C64UDiskEntryContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item?.SourcePath == null || ViewModel.C64UFtp == null) return;

        if (MessageBox.Show($"Permanently delete \"{item.Name}\" from this disk image?",
                "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            byte[] diskBytes = await ViewModel.C64UFtp.DownloadBytesAsync(item.SourcePath);
            var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).DeleteEntry(diskBytes, item.Name);
            await ViewModel.C64UFtp.UploadBytesAsync(item.SourcePath, updated);

            string sourceId = $"{item.SourcePath}!{item.Name}";
            var openTab = ViewModel.OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
            if (openTab != null)
            {
                openTab.IsModified = false; // its source entry is gone; no need to save
                CloseTab(openTab);
            }

            await RefreshC64UNode(item.SourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete \"{item.Name}\":\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Shared folder/file context menu ───────────────────────────────────────

    private void C64UContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) BeginC64UInlineRename(item);
    }

    private async void C64UContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await DeleteC64UItemAsync(item);
    }

    // ── Shared logic ──────────────────────────────────────────────────────────

    // forceHex: see OpenFileByPath - works uniformly here for a real remote file (including a
    // disk image itself, downloaded whole) or a virtual entry inside one.
    private async Task OpenC64UFileInEditorAsync(C64UFileItem item, bool forceHex = false)
    {
        if (item.Content == null && ViewModel.C64UFtp == null) return;

        using var _ = BeginBusyCursor();

        bool wantsHexMode = forceHex || item.Kind == C64UFileKind.Ml;

        // Neither a real C64U FTP file nor a virtual disk-image entry has a local FilePath to
        // dedupe on, so use the FTP path (or, for a virtual entry, the disk image's own path
        // plus the entry's name) as a stable identity instead - re-activate an already-open tab
        // rather than opening a duplicate. If it's open in the OTHER mode, reload it into the
        // newly requested mode instead of silently doing nothing - see OpenFileByPath.
        string sourceId = item.IsVirtual ? $"{item.SourcePath}!{item.Name}" : item.FullPath;
        var existingTab = ViewModel.OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
        if (existingTab != null)
        {
            if (existingTab.IsHexMode == wantsHexMode)
            {
                ActivateTab(existingTab);
                return;
            }

            if (existingTab.IsModified)
            {
                ViewModel.SetStatus($"{item.Name} has unsaved changes - save or close it before reopening in a different view.", StatusType.Warning);
                ActivateTab(existingTab);
                return;
            }
        }

        try
        {
            var bytes = item.Content ?? await ViewModel.C64UFtp!.DownloadBytesAsync(item.FullPath);

            var tab = existingTab ?? new EditorTab { DisplayName = item.Name, VirtualSourceId = sourceId };
            tab.Kind = item.Kind;
            tab.Language = item.Kind == C64UFileKind.Asm ? EditorLanguage.Asm : EditorLanguage.Basic;

            if (wantsHexMode)
            {
                tab.RawBytes = bytes;
                tab.Document.Text = string.Empty;
            }
            else
            {
                tab.RawBytes = null;
                tab.Document.Text = item.Kind == C64UFileKind.Prg
                    ? PadLineNumbers(new PrgConverter().ConvertFromPrg(bytes))
                    : DecodeSourceText(bytes);
            }

            if (existingTab == null)
                ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            tab.IsModified = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening file: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteC64UItemAsync(C64UFileItem item)
    {
        string kind = item.IsFolder ? "folder" : "file";
        string extra = item.IsFolder ? " and all its contents" : "";
        if (MessageBox.Show($"Permanently delete {kind} \"{item.Name}\"{extra} from the C64 Ultimate?",
                $"Delete {(item.IsFolder ? "Folder" : "File")}", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            if (item.IsFolder)
                await ViewModel.C64UFtp!.DeleteFolderAsync(item.FullPath);
            else
                await ViewModel.C64UFtp!.DeleteFileAsync(item.FullPath);

            await RefreshC64UNode(GetC64UParentPath(item.FullPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Reloads the item at the given remote path (or the root listing if the path is "/" or no
    // matching node is found), so newly created/renamed/deleted entries appear immediately.
    private async Task RefreshC64UNode(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            await ViewModel.RefreshC64UFolderAsync();
            return;
        }

        var item = FindC64UItemByPath(path);
        if (item != null)
            await item.RefreshChildrenAsync();
        else
            await ViewModel.RefreshC64UFolderAsync();
    }

    private static string GetC64UParentPath(string path)
    {
        string trimmed = path.TrimEnd('/');
        int idx = trimmed.LastIndexOf('/');
        return idx <= 0 ? "/" : trimmed[..idx];
    }

    private static string CombineC64UPath(string directory, string name)
    {
        string trimmed = directory.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/" + name : trimmed + "/" + name;
    }

    private static C64UFileItem? GetC64UContextItem(object sender)
    {
        var contextMenu = GetOwningContextMenu(sender as MenuItem);
        return (contextMenu?.PlacementTarget as TreeViewItem)?.DataContext as C64UFileItem;
    }

    // A MenuItem's Parent is the ContextMenu directly only when it's a top-level item; for one
    // inside a submenu (e.g. "C64U" under "Run"), Parent is the enclosing MenuItem instead, so a
    // single Parent lookup returns null for those. Walks up through as many nested submenu levels
    // as needed to find the real owning ContextMenu.
    private static ContextMenu? GetOwningContextMenu(MenuItem? item)
    {
        DependencyObject? node = item;
        while (node is MenuItem menuItem)
            node = menuItem.Parent as DependencyObject;

        return node as ContextMenu;
    }

    private C64UFileItem? FindC64UItemByPath(string path)
    {
        foreach (var item in ViewModel.C64UFileItems)
        {
            var found = SearchC64UTree(item, path);
            if (found != null) return found;
        }
        return null;
    }

    private static C64UFileItem? SearchC64UTree(C64UFileItem item, string path)
    {
        if (string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)) return item;
        foreach (var child in item.Children)
        {
            var found = SearchC64UTree(child, path);
            if (found != null) return found;
        }
        return null;
    }

    private C64UFileItem? FindC64UParentFolder(C64UFileItem target)
        => FindC64UParentFolderRecursive(ViewModel.C64UFileItems, target);

    private static C64UFileItem? FindC64UParentFolderRecursive(IEnumerable<C64UFileItem> items, C64UFileItem target)
    {
        foreach (var folder in items.Where(i => i.IsFolder))
        {
            if (folder.Children.Contains(target)) return folder;
            var found = FindC64UParentFolderRecursive(folder.Children, target);
            if (found != null) return found;
        }
        return null;
    }

    private static TreeViewItem? FindC64UTreeViewItem(ItemsControl container, C64UFileItem target)
    {
        foreach (var raw in container.Items)
        {
            var tvi = container.ItemContainerGenerator.ContainerFromItem(raw) as TreeViewItem;
            if (tvi == null) continue;
            if (raw == target) return tvi;
            var found = FindC64UTreeViewItem(tvi, target);
            if (found != null) return found;
        }
        return null;
    }

    #endregion

    #region Inline Rename / New Folder

    private void BeginC64UInlineRename(C64UFileItem item)
    {
        item.IsRenaming = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            var tvi = FindC64UTreeViewItem(C64UFileTree, item);
            if (tvi == null) return;
            var box = FindVisualChild<TextBox>(tvi, "C64URenameBox");
            if (box == null) return;
            box.Focus();
            if (!item.IsFolder)
            {
                int dot = box.Text.LastIndexOf('.');
                box.Select(0, dot > 0 ? dot : box.Text.Length);
            }
            else
            {
                box.SelectAll();
            }
        });
    }

    private async Task CommitC64URename(TextBox box)
    {
        var item = box.DataContext as C64UFileItem;
        if (item == null || !item.IsRenaming) return;
        item.IsRenaming = false;

        if (item.IsNew)
        {
            if (item.Kind.IsDiskImageKind()) await CommitC64UNewDiskImage(item, box.Text.Trim(), item.Kind);
            else await CommitC64UNewFolder(item, box.Text.Trim());
            return;
        }

        if (item.IsVirtual)
        {
            await CommitC64UVirtualEntryRenameAsync(item, box.Text.Trim());
            return;
        }

        string newName = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        string parentPath = GetC64UParentPath(item.FullPath);
        string newPath = CombineC64UPath(parentPath, newName);

        try
        {
            await ViewModel.C64UFtp!.RenameAsync(item.FullPath, newPath);
            await RefreshC64UNode(parentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Renames a "virtual" entry (living only inside a disk image mounted over FTP) via
    // CommitC64URename's shared inline-rename flow - unlike a real remote file, there's no path
    // to FTP-rename, so this downloads the disk image, rewrites its directory-slot name field,
    // and re-uploads it.
    private async Task CommitC64UVirtualEntryRenameAsync(C64UFileItem item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name || item.SourcePath == null) return;

        try
        {
            byte[] diskBytes = await ViewModel.C64UFtp!.DownloadBytesAsync(item.SourcePath);
            var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).RenameEntry(diskBytes, item.Name, newName);
            await ViewModel.C64UFtp.UploadBytesAsync(item.SourcePath, updated);
            await RefreshC64UNode(item.SourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename \"{item.Name}\":\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelC64URename(TextBox box)
    {
        if (box.DataContext is not C64UFileItem item) return;
        item.IsRenaming = false;
        if (item.IsNew) RemoveC64UPendingItem(item);
    }

    private async void C64URenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)      { await CommitC64URename((TextBox)sender); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelC64URename((TextBox)sender);       e.Handled = true; }
    }

    private async void C64URenameBox_LostFocus(object sender, RoutedEventArgs e)
        => await CommitC64URename((TextBox)sender);

    // Inserts an editable, not-yet-created placeholder folder into the tree and puts it
    // straight into rename mode - mirrors the local Explorer's inline "new folder" flow.
    private async Task CreateC64UNewFolderInlineAsync(C64UFileItem? parentFolder)
    {
        string parentPath;
        ObservableCollection<C64UFileItem> targetCollection;

        if (parentFolder != null)
        {
            // Await the load (rather than just setting IsExpanded) so the folder's real
            // children are in place before we insert the pending item - IsExpanded's own
            // fire-and-forget load would otherwise wipe it out when it completes.
            if (!parentFolder.IsExpanded)
            {
                await parentFolder.LoadChildrenAsync();
                parentFolder.IsExpanded = true;
            }
            parentPath = parentFolder.FullPath;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentPath = "/";
            targetCollection = ViewModel.C64UFileItems;
        }

        var pendingItem = new C64UFileItem(parentPath);
        targetCollection.Insert(0, pendingItem);
        BeginC64UInlineRename(pendingItem);
    }

    private async Task CommitC64UNewFolder(C64UFileItem item, string folderName)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemoveC64UPendingItem(item);
        if (string.IsNullOrWhiteSpace(folderName)) return; // no name provided - create nothing

        string path = CombineC64UPath(parentPath, folderName);
        try
        {
            await ViewModel.C64UFtp!.CreateFolderAsync(path);
            await RefreshC64UNode(parentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create folder:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveC64UPendingItem(C64UFileItem item)
    {
        var parent = FindC64UParentFolder(item);
        if (parent != null) parent.Children.Remove(item);
        else ViewModel.C64UFileItems.Remove(item);
    }

    // Mirrors CreateC64UNewFolderInlineAsync for a blank disk image; the placeholder's Kind
    // (D64/D81) is threaded through so CommitC64URename knows to route it to CommitC64UNewDiskImage.
    private async Task CreateC64UNewDiskImageInlineAsync(C64UFileItem? parentFolder, C64UFileKind kind)
    {
        string parentPath;
        ObservableCollection<C64UFileItem> targetCollection;

        if (parentFolder != null)
        {
            if (!parentFolder.IsExpanded)
            {
                await parentFolder.LoadChildrenAsync();
                parentFolder.IsExpanded = true;
            }
            parentPath = parentFolder.FullPath;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentPath = "/";
            targetCollection = ViewModel.C64UFileItems;
        }

        var pendingItem = new C64UFileItem(parentPath, kind);
        targetCollection.Insert(0, pendingItem);
        BeginC64UInlineRename(pendingItem);
    }

    private async Task CommitC64UNewDiskImage(C64UFileItem item, string diskName, C64UFileKind kind)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemoveC64UPendingItem(item);
        if (string.IsNullOrWhiteSpace(diskName)) return; // no name provided - create nothing

        string extension = kind == C64UFileKind.D64 ? ".d64" : ".d81";
        string fileName = diskName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? diskName : diskName + extension;
        string path = CombineC64UPath(parentPath, fileName);

        try
        {
            byte[] blankImage = DiskImage.ForKind(kind).CreateBlankImage(Path.GetFileNameWithoutExtension(fileName));
            await ViewModel.C64UFtp!.UploadBytesAsync(path, blankImage);
            await RefreshC64UNode(parentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create disk image:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Drag and Drop

    private void C64UFileTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _c64uDragStartPoint = e.GetPosition(null);
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as C64UFileItem;
        // Virtual entries (found inside a mounted .d64) have no real FTP path of their own, so
        // they can't be dragged/moved like a real remote file.
        _c64uDragItem = item?.IsVirtual == true ? null : item;
    }

    private void C64UFileTreeItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _c64uDragItem == null) return;
        var delta = e.GetPosition(null) - _c64uDragStartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        // Move onto a folder, Copy onto a disk image (see C64UFileTreeItem_DragOver) - both must
        // be allowed here, or WPF silently rejects whichever one a DragOver handler tries to
        // request that isn't in this set, showing the "no drop" cursor no matter what Effects is
        // set to.
        DragDrop.DoDragDrop((DependencyObject)sender, _c64uDragItem, DragDropEffects.Move | DragDropEffects.Copy);
    }

    // A folder target moves the dragged item in (FTP rename to a path under the new directory);
    // a disk-image target embeds a copy of it as a new entry instead (see C64UFileTreeItem_Drop) -
    // mirrors FileTreeItem_DragOver's local-tree equivalent.
    private void C64UFileTreeItem_DragOver(object sender, DragEventArgs e)
    {
        var target = (sender as TreeViewItem)?.DataContext as C64UFileItem;
        bool isValidTarget = target != null && !target.IsVirtual && (target.IsFolder || target.Kind.IsDiskImageKind());
        if (!isValidTarget || _c64uDragItem == null || !IsValidC64UDrop(_c64uDragItem, target!))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (!ReferenceEquals(_c64uCurrentDropTarget, target))
        {
            if (_c64uCurrentDropTarget != null) _c64uCurrentDropTarget.IsDropTarget = false;
            _c64uCurrentDropTarget = target;
            target!.IsDropTarget = true;
        }
        e.Effects = target!.Kind.IsDiskImageKind() ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void C64UFileTreeItem_DragLeave(object sender, DragEventArgs e)
    {
        var target = (sender as TreeViewItem)?.DataContext as C64UFileItem;
        if (target != null && ReferenceEquals(_c64uCurrentDropTarget, target))
        {
            target.IsDropTarget = false;
            _c64uCurrentDropTarget = null;
        }
    }

    private async void C64UFileTreeItem_Drop(object sender, DragEventArgs e)
    {
        if (_c64uCurrentDropTarget != null) { _c64uCurrentDropTarget.IsDropTarget = false; _c64uCurrentDropTarget = null; }
        var target = (sender as TreeViewItem)?.DataContext as C64UFileItem;
        bool isValidTarget = target != null && !target.IsVirtual && (target.IsFolder || target.Kind.IsDiskImageKind());
        if (!isValidTarget || _c64uDragItem == null || !IsValidC64UDrop(_c64uDragItem, target!) || ViewModel.C64UFtp == null)
        {
            _c64uDragItem = null; e.Handled = true; return;
        }

        var dragItem = _c64uDragItem;
        _c64uDragItem = null;
        e.Handled = true;

        if (target!.Kind.IsDiskImageKind())
        {
            try
            {
                byte[] bytes = dragItem.Content ?? await ViewModel.C64UFtp.DownloadBytesAsync(dragItem.FullPath);
                if (!TryBuildDiskEntryPrgDataFromBytes(bytes, dragItem.Kind, out byte[]? prgData)) return;

                string entryName = Path.GetFileNameWithoutExtension(dragItem.Name);
                await AddEntryToC64UDiskImageAsync(prgData!, entryName, target);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not add file to disk image:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        string itemName = dragItem.Name;
        string sourceParent = GetC64UParentPath(dragItem.FullPath);
        string destination = CombineC64UPath(target.FullPath, itemName);
        try
        {
            await ViewModel.C64UFtp.RenameAsync(dragItem.FullPath, destination);
            await RefreshC64UNode(sourceParent);
            await RefreshC64UNode(target.FullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not move \"{itemName}\":\n{ex.Message}", "Move Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsValidC64UDrop(C64UFileItem source, C64UFileItem target)
    {
        // A disk image can only gain a new entry (a single file, not a folder full of them, and
        // not another disk image, which the real C64 format has no way to nest) - the
        // move-related checks below (same-parent/self-nesting) are meaningless for this target
        // kind anyway.
        if (target.Kind.IsDiskImageKind()) return !source.IsFolder && !source.Kind.IsDiskImageKind();

        if (!target.IsFolder) return false;
        if (string.Equals(GetC64UParentPath(source.FullPath), target.FullPath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (source.IsFolder)
        {
            string srcPrefix = source.FullPath.TrimEnd('/') + "/";
            string tgtPrefix = target.FullPath.TrimEnd('/') + "/";
            if (tgtPrefix.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    #endregion
}
