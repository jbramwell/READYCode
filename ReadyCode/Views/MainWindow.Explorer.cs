// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ReadyCode.Assembler;
using ReadyCode.C64U;
using ReadyCode.Diff;
using ReadyCode.IO;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Views;

// The local folder Explorer panel: the file tree, drag and drop / cut, copy, paste, context menus, disk image entries, file compare, and inline rename / create.
public partial class MainWindow
{
    #region Explorer Tree

    private void FileTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as FileTreeItem;
        // Disk images aren't openable as text (see OpenFileByPath) - leave the event unhandled,
        // same as a folder, so WPF's default double-click-to-toggle-expansion behavior runs instead.
        if (item == null || item.IsFolder || item.IsDiskImage) return;

        if (item.IsVirtual)
        {
            if (item.IsOpenable) _ = OpenLocalVirtualFileInEditor(item);
        }
        else
        {
            OpenFileByPath(item.FullPath);
        }
        e.Handled = true;
    }

    private void FileTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as FileTreeItem;
        // Virtual entries (found inside a mounted .d64) have no real path on disk, so they
        // can't be dragged/moved like a real file.
        _dragItem = item?.IsVirtual == true ? null : item;
    }

    private FileTreeItem? FindItemByPath(string path)
    {
        foreach (var item in ViewModel.FolderItems)
        {
            var found = SearchTree(item, path);
            if (found != null) return found;
        }
        return null;
    }

    private static FileTreeItem? SearchTree(FileTreeItem item, string path)
    {
        if (string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)) return item;
        foreach (var child in item.Children)
        {
            var found = SearchTree(child, path);
            if (found != null) return found;
        }
        return null;
    }

    private void ExplorerNewFile_Click(object sender, RoutedEventArgs e) => CreateNewFileInline(null);

    private void ExplorerNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var selected = FileTree.SelectedItem as FileTreeItem;
        FileTreeItem? parentFolder = selected == null ? null
            : selected.IsFolder ? selected
            : FindParentFolder(selected);
        CreateNewFolderInline(parentFolder);
    }

    private void ExplorerRefresh_Click(object sender, RoutedEventArgs e)
    {
        string folder = ViewModel.Project.RootPath;
        if (!string.IsNullOrEmpty(folder))
            ViewModel.LoadFolder(folder);
    }

    private void ExplorerCollapse_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in ViewModel.FolderItems)
            item.CollapseAll();
    }

    private void FileTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FileTree.SelectedItem is not FileTreeItem item || item.IsRenaming) return;

        // Virtual entries (found inside a mounted .d64) have no real path on disk, so none of
        // the file-management shortcuts below apply to them.
        if (item.IsVirtual)
        {
            if (e.Key == Key.Return && item.IsOpenable)
            {
                _ = OpenLocalVirtualFileInEditor(item);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.F2)
        {
            BeginInlineRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (item.IsFolder) DeleteFolder(item); else DeleteFile(item);
            e.Handled = true;
        }
        else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetClipboardFile(item.FullPath, cut: true);
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetClipboardFile(item.FullPath, cut: false);
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PasteToFolder(item.IsFolder ? item.FullPath : Path.GetDirectoryName(item.FullPath) ?? item.FullPath);
            e.Handled = true;
        }
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl container, FileTreeItem target)
    {
        foreach (var raw in container.Items)
        {
            var tvi = container.ItemContainerGenerator.ContainerFromItem(raw) as TreeViewItem;
            if (tvi == null) continue;
            if (raw == target) return tvi;
            var found = FindTreeViewItem(tvi, target);
            if (found != null) return found;
        }
        return null;
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl container, VariableInfo target)
    {
        foreach (var raw in container.Items)
        {
            var tvi = container.ItemContainerGenerator.ContainerFromItem(raw) as TreeViewItem;
            if (tvi == null) continue;
            if (raw == target) return tvi;
            var found = FindTreeViewItem(tvi, target);
            if (found != null) return found;
        }
        return null;
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl container, DebugVariableNode target)
    {
        foreach (var raw in container.Items)
        {
            var tvi = container.ItemContainerGenerator.ContainerFromItem(raw) as TreeViewItem;
            if (tvi == null) continue;
            if (raw == target) return tvi;
            var found = FindTreeViewItem(tvi, target);
            if (found != null) return found;
        }
        return null;
    }

    // Shared by the Folder Explorer and C64U trees: selects and focuses whichever item was
    // actually right-clicked before its context menu opens, so the highlighted row always
    // matches what the menu is about to act on.
    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;
        item.IsSelected = true;
        item.Focus();
    }

    private void OpenFolderDialog()
    {
        var dialog = new OpenFolderDialog { Title = "Open Folder" };
        if (ViewModel.Project.IsOpen)
            dialog.InitialDirectory = ViewModel.Project.RootPath;

        if (dialog.ShowDialog(this) == true)
        {
            string path = dialog.FolderName;
            ViewModel.LoadFolder(path);
            ViewModel.Project.RootPath = path;
            ViewModel.Settings.Save();

            if (ExplorerToggle.IsChecked != true)
            {
                ExplorerToggle.IsChecked = true;
                LeftPanelCol.Width = new GridLength(ViewModel.Settings.LeftPanelWidth);
                LeftSplitterCol.Width = new GridLength(4);
            }
        }
    }

    private void CloseFolder()
    {
        if (!HasFolderOpen()) return;

        ViewModel.FolderItems.Clear();
        ViewModel.ExplorerTitle = "EXPLORER";
        ViewModel.Project.RootPath = "";
        ViewModel.Settings.Save();
    }

    // Continuously re-clamps FolderTreeRow during an active drag so it can never grow large
    // enough to push the Variable Explorer's header past the bottom of the visible column -
    // RowDefinition's own MinHeight only bounds the row being shrunk, not the one being grown,
    // and VariablesRow's nominal "*" sizing doesn't protect it once a GridSplitter has touched it.
    private void ExplorerRowSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double maxFolderTreeHeight = ExplorerColumnGrid.ActualHeight - ExplorerRowSplitter.ActualHeight - VariablesRow.MinHeight;
        if (maxFolderTreeHeight >= FolderTreeRow.MinHeight && FolderTreeRow.Height.Value > maxFolderTreeHeight)
            FolderTreeRow.Height = new GridLength(maxFolderTreeHeight);
    }

    #endregion

    #region Drag and Drop / Cut, Copy, Paste

    private void FileTreeItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
        var delta = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        // Move onto a folder, Copy onto a disk image (see FileTreeItem_DragOver) - both must be
        // allowed here, or WPF silently rejects whichever one a DragOver handler tries to request
        // that isn't in this set, showing the "no drop" cursor no matter what Effects is set to.
        DragDrop.DoDragDrop((DependencyObject)sender, _dragItem, DragDropEffects.Move | DragDropEffects.Copy);
    }

    // A folder target moves the dragged item in; a disk-image target embeds a copy of it as a
    // new entry instead (see IsValidDrop/FileTreeItem_Drop) - only a single file can become a
    // disk entry, so a dragged folder is rejected there.
    private void FileTreeItem_DragOver(object sender, DragEventArgs e)
    {
        var target = (sender as TreeViewItem)?.DataContext as FileTreeItem;
        bool isValidTarget = target != null && (target.IsFolder || target.Kind.IsDiskImageKind());
        if (!isValidTarget || _dragItem == null || !IsValidDrop(_dragItem, target!))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (!ReferenceEquals(_currentDropTarget, target))
        {
            if (_currentDropTarget != null) _currentDropTarget.IsDropTarget = false;
            _currentDropTarget = target;
            target!.IsDropTarget = true;
        }
        e.Effects = target!.Kind.IsDiskImageKind() ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void FileTreeItem_DragLeave(object sender, DragEventArgs e)
    {
        var target = (sender as TreeViewItem)?.DataContext as FileTreeItem;
        if (target != null && ReferenceEquals(_currentDropTarget, target))
        {
            target.IsDropTarget = false;
            _currentDropTarget = null;
        }
    }

    private void FileTreeItem_Drop(object sender, DragEventArgs e)
    {
        if (_currentDropTarget != null) { _currentDropTarget.IsDropTarget = false; _currentDropTarget = null; }
        var target = (sender as TreeViewItem)?.DataContext as FileTreeItem;
        bool isValidTarget = target != null && (target.IsFolder || target.Kind.IsDiskImageKind());
        if (!isValidTarget || _dragItem == null || !IsValidDrop(_dragItem, target!))
        {
            _dragItem = null; e.Handled = true; return;
        }

        if (target!.Kind.IsDiskImageKind())
        {
            AddFileToLocalDiskImage(_dragItem.FullPath, target);
            _dragItem = null;
            e.Handled = true;
            return;
        }

        var targetFolder = target;
        string itemName    = Path.GetFileName(_dragItem.FullPath);
        string destination = Path.Combine(targetFolder.FullPath, itemName);
        if ((_dragItem.IsFolder && Directory.Exists(destination)) || (!_dragItem.IsFolder && File.Exists(destination)))
        {
            MessageBox.Show($"A {(_dragItem.IsFolder ? "folder" : "file")} named \"{itemName}\" already exists in \"{targetFolder.Name}\".",
                "Move Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            _dragItem = null; e.Handled = true; return;
        }
        string movedFrom = _dragItem.FullPath;
        _dragItem = null;
        try
        {
            if (Directory.Exists(movedFrom)) Directory.Move(movedFrom, destination);
            else                             File.Move(movedFrom, destination);

            UpdateCurrentFilePathAfterMove(movedFrom, destination);
            RefreshAfterMove(movedFrom, targetFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not move \"{itemName}\":\n{ex.Message}", "Move Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    private void RootHeader_DragOver(object sender, DragEventArgs e)
    {
        if (_dragItem == null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        string? rootPath = ViewModel.Project.RootPath;
        if (string.IsNullOrEmpty(rootPath)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        // Block drop if already at root level
        if (string.Equals(Path.GetDirectoryName(_dragItem.FullPath), rootPath, StringComparison.OrdinalIgnoreCase))
        {
            e.Effects = DragDropEffects.None; e.Handled = true; return;
        }
        ExplorerHeaderBorder.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF3CD"));
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void RootHeader_DragLeave(object sender, DragEventArgs e)
    {
        ExplorerHeaderBorder.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8"));
    }

    private void RootHeader_Drop(object sender, DragEventArgs e)
    {
        ExplorerHeaderBorder.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8"));
        string? rootPath = ViewModel.Project.RootPath;
        if (_dragItem == null || string.IsNullOrEmpty(rootPath))
        {
            _dragItem = null; e.Handled = true; return;
        }
        if (string.Equals(Path.GetDirectoryName(_dragItem.FullPath), rootPath, StringComparison.OrdinalIgnoreCase))
        {
            _dragItem = null; e.Handled = true; return;
        }
        string itemName    = Path.GetFileName(_dragItem.FullPath);
        string destination = Path.Combine(rootPath, itemName);
        if ((_dragItem.IsFolder && Directory.Exists(destination)) || (!_dragItem.IsFolder && File.Exists(destination)))
        {
            MessageBox.Show($"A {(_dragItem.IsFolder ? "folder" : "file")} named \"{itemName}\" already exists in the root folder.",
                "Move Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            _dragItem = null; e.Handled = true; return;
        }
        string movedFrom = _dragItem.FullPath;
        _dragItem = null;
        try
        {
            if (Directory.Exists(movedFrom)) Directory.Move(movedFrom, destination);
            else                             File.Move(movedFrom, destination);
            UpdateCurrentFilePathAfterMove(movedFrom, destination);
            // Source parent is a subfolder — refresh it; root gets a full refresh
            var sourceParent = FindItemByPath(Path.GetDirectoryName(movedFrom)!);
            sourceParent?.RefreshChildren();
            ViewModel.RefreshRootItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not move \"{itemName}\":\n{ex.Message}", "Move Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    private void RootContextPaste_Click(object sender, RoutedEventArgs e)
    {
        string? rootPath = ViewModel.Project.RootPath;
        if (!string.IsNullOrEmpty(rootPath))
            PasteToFolder(rootPath);
    }

    private void PasteToFolder(string targetFolderPath)
    {
        if (!Clipboard.ContainsFileDropList()) return;
        var files = Clipboard.GetFileDropList();
        if (files.Count == 0) return;

        bool isCut = false;
        if (Clipboard.GetData("Preferred DropEffect") is System.IO.MemoryStream ms)
            isCut = ms.ReadByte() == 2;

        bool anyMoved = false;
        foreach (string? sourcePath in files)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            string itemName    = Path.GetFileName(sourcePath);
            string destination = Path.Combine(targetFolderPath, itemName);
            bool isFolder      = Directory.Exists(sourcePath);
            if ((isFolder && Directory.Exists(destination)) || (!isFolder && File.Exists(destination)))
            {
                MessageBox.Show($"A {(isFolder ? "folder" : "file")} named \"{itemName}\" already exists.",
                    "Paste Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }
            try
            {
                if (isCut)
                {
                    if (isFolder) Directory.Move(sourcePath, destination);
                    else          File.Move(sourcePath, destination);
                    UpdateCurrentFilePathAfterMove(sourcePath, destination);
                }
                else
                {
                    if (isFolder) CopyDirectoryRecursive(sourcePath, destination);
                    else          File.Copy(sourcePath, destination);
                }
                anyMoved = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not paste \"{itemName}\":\n{ex.Message}", "Paste Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (!anyMoved) return;
        if (isCut) Clipboard.Clear();
        ViewModel.RefreshRootItems();
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        foreach (string dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    private void UpdateCurrentFilePathAfterMove(string movedFrom, string destination)
    {
        string prefix = movedFrom.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var tab in ViewModel.OpenTabs)
        {
            if (string.IsNullOrEmpty(tab.FilePath)) continue;
            if (tab.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                tab.FilePath = destination + tab.FilePath[movedFrom.Length..];
            else if (string.Equals(tab.FilePath, movedFrom, StringComparison.OrdinalIgnoreCase))
                tab.FilePath = destination;
        }
    }

    private void RefreshAfterMove(string movedFrom, FileTreeItem targetFolder)
    {
        string sourceParentPath = Path.GetDirectoryName(movedFrom)!;
        bool sourceIsRoot = string.Equals(sourceParentPath, ViewModel.Project.RootPath,
            StringComparison.OrdinalIgnoreCase);

        if (sourceIsRoot)
        {
            // Remove the moved item directly from FolderItems to avoid a full reload
            var toRemove = ViewModel.FolderItems.FirstOrDefault(i =>
                string.Equals(i.FullPath, movedFrom, StringComparison.OrdinalIgnoreCase));
            if (toRemove != null) ViewModel.FolderItems.Remove(toRemove);
        }
        else
        {
            var sourceParent = FindItemByPath(sourceParentPath);
            sourceParent?.RefreshChildren();
        }

        targetFolder.RefreshChildren();
        targetFolder.IsExpanded = true;
    }

    private static bool IsValidDrop(FileTreeItem source, FileTreeItem target)
    {
        // A disk image can only gain a new entry (a single file, not a folder full of them, and
        // not another disk image, which the real C64 format has no way to nest) - the
        // move-related checks below (same-parent/self-nesting) are meaningless for this target
        // kind anyway.
        if (target.Kind.IsDiskImageKind()) return !source.IsFolder && !source.Kind.IsDiskImageKind();

        if (!target.IsFolder) return false;
        if (string.Equals(Path.GetDirectoryName(source.FullPath), target.FullPath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (source.IsFolder)
        {
            string srcPrefix = source.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string tgtPrefix = target.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (tgtPrefix.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static void SetClipboardFile(string path, bool cut)
    {
        var paths = new StringCollection { path };
        var data = new DataObject();
        data.SetFileDropList(paths);
        byte[] effect = { (byte)(cut ? 2 : 5), 0, 0, 0 }; // 2=move, 5=copy
        data.SetData("Preferred DropEffect", new System.IO.MemoryStream(effect));
        Clipboard.SetDataObject(data, true);
    }

    // Shared by the root/folder/file Explorer context menus: only enable "Paste" when the
    // clipboard actually holds a cut/copied file or folder.
    private void PasteContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var pasteItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Header, "Paste"));
        if (pasteItem != null)
            pasteItem.IsEnabled = Clipboard.ContainsFileDropList();

        UpdateCompareMenuItems(menu, (menu.PlacementTarget as TreeViewItem)?.DataContext);
    }

    #endregion

    #region Context Menus

    private void FolderContextNewFile_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) CreateNewFileInline(item);
    }

    private void FolderContextNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) CreateNewFolderInline(item);
    }

    private void FolderContextNewD64_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) CreateNewDiskImageInline(item, C64UFileKind.D64);
    }

    private void FolderContextNewD81_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) CreateNewDiskImageInline(item, C64UFileKind.D81);
    }

    private void FolderContextReveal_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", item.FullPath) { UseShellExecute = true });
    }

    private void FolderContextCut_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        SetClipboardFile(item.FullPath, cut: true);
    }

    private void FolderContextCopy_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        SetClipboardFile(item.FullPath, cut: false);
    }

    private void FolderContextPaste_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) PasteToFolder(item.FullPath);
    }

    private void FolderContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) BeginInlineRename(item);
    }

    private void FolderContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) DeleteFolder(item);
    }

    private void DeleteFolder(FileTreeItem item)
    {
        if (MessageBox.Show($"Permanently delete folder \"{item.Name}\" and all its contents?",
                "Delete Folder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            Directory.Delete(item.FullPath, recursive: true);
            ViewModel.LoadFolder(ViewModel.Project.RootPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete folder:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── File context menu ─────────────────────────────────────────────────────

    private void FileContextReveal_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
    }

    private void FileContextOpen_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) OpenFileByPath(item.FullPath);
    }

    // "Open as Hex" - forces the hex editor for any file type READYCode supports (not just the
    // ones it auto-detects as needing it), including a confirmed-BASIC .prg or a disk image.
    private void FileContextOpenAsHex_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) OpenFileByPath(item.FullPath, forceHex: true);
    }

    private void FileContextCut_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        SetClipboardFile(item.FullPath, cut: true);
    }

    private void FileContextCopy_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        SetClipboardFile(item.FullPath, cut: false);
    }

    private void FileContextPaste_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        string? parent = Path.GetDirectoryName(item.FullPath);
        if (!string.IsNullOrEmpty(parent)) PasteToFolder(parent);
    }

    private void FileContextCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        Clipboard.SetText(item.FullPath);
    }

    private void FileContextCopyRelativePath_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        string relative = Path.GetRelativePath(ViewModel.Project.RootPath, item.FullPath);
        Clipboard.SetText(relative);
    }

    private void FileContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) BeginInlineRename(item);
    }

    private void FileContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) DeleteFile(item);
    }

    private void DeleteFile(FileTreeItem item)
    {
        if (MessageBox.Show($"Permanently delete \"{item.Name}\"?",
                "Delete File", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            File.Delete(item.FullPath);
            // Close any open tab for this file
            var openTab = ViewModel.OpenTabs.FirstOrDefault(t =>
                string.Equals(t.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase));
            if (openTab != null)
            {
                openTab.IsModified = false; // file is gone; no need to save
                CloseTab(openTab);
            }
            // Remove the node directly instead of reloading the whole tree, so other
            // expanded folders stay expanded.
            RemovePendingItem(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static FileTreeItem? GetContextItem(object sender)
    {
        var contextMenu = GetOwningContextMenu(sender as MenuItem);
        return (contextMenu?.PlacementTarget as TreeViewItem)?.DataContext as FileTreeItem;
    }

    #endregion

    #region Disk Image Entries

    private void LocalDiskEntryContextOpen_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) _ = OpenLocalVirtualFileInEditor(item);
    }

    // "Open as Hex" - see FileContextOpenAsHex_Click. Virtual entries have no FullPath, so this
    // goes through OpenLocalVirtualFileInEditor (already holds the entry's bytes in memory)
    // rather than OpenFileByPath.
    private void LocalDiskEntryContextOpenAsHex_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) _ = OpenLocalVirtualFileInEditor(item, forceHex: true);
    }

    // Reuses the same inline-rename UI as real files/folders (BeginInlineRename/CommitRename) -
    // the virtual entry still renders in the tree with the same RenameBox template, just routes
    // through CommitVirtualEntryRename instead of File.Move since it has no real FullPath.
    private void LocalDiskEntryContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) BeginInlineRename(item);
    }

    private void LocalDiskEntryContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item?.SourcePath == null) return;

        if (MessageBox.Show($"Permanently delete \"{item.Name}\" from this disk image?",
                "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            byte[] diskBytes = File.ReadAllBytes(item.SourcePath);
            var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).DeleteEntry(diskBytes, item.Name);
            AtomicFile.WriteAllBytes(item.SourcePath, updated);

            string sourceId = $"{item.SourcePath}!{item.Name}";
            var openTab = ViewModel.OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
            if (openTab != null)
            {
                openTab.IsModified = false; // its source entry is gone; no need to save
                CloseTab(openTab);
            }

            RefreshDiskImageNode(item.SourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete \"{item.Name}\":\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // "Add File..." on a .d64/.d81 node itself - lets the user pick a local .prg (embedded as-is,
    // since a .prg file's bytes already are the on-disk PRG format, load address included) or
    // .bas (tokenized first via the same converter SaveFile uses) to add into the image.
    private void LocalDiskImageContextAddFile_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Commodore 64 Programs (*.prg)|*.prg|BASIC Source (*.bas)|*.bas|6502 Assembly (*.asm;*.s)|*.asm;*.s|All Files (*.*)|*.*",
            Title = "Add File to Disk Image"
        };
        if (dialog.ShowDialog() != true) return;

        AddFileToLocalDiskImage(dialog.FileName, item);
    }

    // Builds prgData from a local file path (mirroring TryBuildDiskEntryPrgDataFromBytes, see
    // there) and adds it to a local disk image's bytes on disk - the shared core behind both
    // "Add File to Disk Image" and dragging a file onto a disk image row.
    private void AddFileToLocalDiskImage(string sourcePath, FileTreeItem diskImageItem)
    {
        var sourceKind = FileClassifier.Classify(sourcePath, isFolder: false);
        if (!TryBuildDiskEntryPrgDataFromBytes(File.ReadAllBytes(sourcePath), sourceKind, out byte[]? prgData))
            return;

        try
        {
            string entryName = Path.GetFileNameWithoutExtension(sourcePath);
            var entryKind = FileClassifier.Classify(entryName + ".prg", isFolder: false, () => prgData!);

            byte[] diskBytes = File.ReadAllBytes(diskImageItem.FullPath);
            var diskKind = FileClassifier.Classify(diskImageItem.FullPath, isFolder: false);
            byte[] updated = DiskImage.ForKind(diskKind).AddEntry(diskBytes, entryName, entryKind, prgData!);
            AtomicFile.WriteAllBytes(diskImageItem.FullPath, updated);

            RefreshDiskImageNode(diskImageItem.FullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not add file to disk image:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Builds ready-to-add-to-disk .prg bytes for "Add File to Disk Image" from a local file path:
    // tokenizes .bas source, assembles .asm/.s source fresh, or passes an already-tokenized
    // .prg/.ml file through unchanged - see TryBuildDiskEntryPrgDataFromBytes for the shared core
    // (used directly when the source lives on the C64U rather than the local disk).
    private bool TryBuildDiskEntryPrgData(string sourcePath, out byte[]? prgData)
    {
        var kind = FileClassifier.Classify(sourcePath, isFolder: false);
        return TryBuildDiskEntryPrgDataFromBytes(File.ReadAllBytes(sourcePath), kind, out prgData);
    }

    // Builds ready-to-add-to-disk .prg bytes from raw source bytes: assembles .asm/.s source
    // fresh (mirroring LoadOrRunFileAsync - a real C64 can't do anything useful with raw assembly
    // text sitting on a disk), tokenizes .bas source, or passes an already-tokenized .prg/.ml
    // file through unchanged. On an assembly failure, shows the same "Assembly Errors" dialog the
    // Load/Run commands do and returns false so the caller aborts without touching the disk image.
    private bool TryBuildDiskEntryPrgDataFromBytes(byte[] sourceBytes, C64UFileKind sourceKind, out byte[]? prgData)
    {
        if (sourceKind == C64UFileKind.Asm)
        {
            var result = new Asm6502Assembler().Assemble(
                DecodeSourceText(sourceBytes), ViewModel.Settings.AsmOutputMode == "Standalone",
                (ushort)ViewModel.Settings.AsmDefaultOriginAddress);
            if (!result.Success)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.Errors.Select(err => $"Line {err.LineNumber}: {err.Message}")),
                    "Assembly Errors", MessageBoxButton.OK, MessageBoxImage.Error);
                prgData = null;
                return false;
            }

            prgData = result.PrgBytes;
            return true;
        }

        prgData = sourceKind == C64UFileKind.Bas
            ? new PrgConverter().ConvertToPrg(DecodeSourceText(sourceBytes))
            : sourceBytes;
        return true;
    }

    // Reloads a disk image node's virtual children after its bytes were rewritten (add/delete/
    // rename/replace), so the tree reflects the change without a full explorer reload.
    private void RefreshDiskImageNode(string diskImagePath) => FindItemByPath(diskImagePath)?.RefreshChildren();

    #endregion

    #region Compare

    // Wired to every comparable-kind context menu that doesn't already have an Opened handler
    // for something else (PasteContextMenu_Opened covers the local top-level file menus, which
    // also need the Paste-enablement check above).
    private void CompareContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        UpdateCompareMenuItems(menu, (menu.PlacementTarget as TreeViewItem)?.DataContext);
    }

    // Shows/hides and enables/disables "Compare file" based on whether a file is pending
    // ("Select file for comparison") and, if so, whether the clicked item is itself a
    // comparable kind - "Select file for comparison" itself needs no gating, it's always
    // available. Any two comparable kinds can now be compared with each other (e.g. .bas vs
    // .prg, .bas vs .asm) - each side is resolved to text independently.
    private void UpdateCompareMenuItems(ContextMenu menu, object? clickedItem)
    {
        var compareItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Header, "Compare file"));
        if (compareItem == null) return;

        var pending = ViewModel.PendingCompareFile;
        if (pending == null)
        {
            compareItem.Visibility = Visibility.Collapsed;
            compareItem.ToolTip = null;
            return;
        }

        ComparableFileRef? clicked = clickedItem switch
        {
            FileTreeItem local => ComparableFileRef.FromLocal(local),
            C64UFileItem remote => ComparableFileRef.FromC64U(remote),
            _ => null,
        };

        compareItem.Visibility = Visibility.Visible;
        bool canCompare = clicked != null && CompareFileResolver.CanCompare(pending, clicked);
        compareItem.IsEnabled = canCompare;
        compareItem.ToolTip = canCompare
            ? null
            : $"Can't compare '{pending.Name}' with this file - unsupported file type.";
    }

    // "Select file for comparison" - shared by every comparable-kind context menu in both
    // explorers. Stores the clicked file as the pending "left" side of a File Compare; clicking
    // it again on the same file that's already pending clears the selection instead, which is
    // the only way to cancel a pending comparison.
    private void CompareSelectFile_Click(object sender, RoutedEventArgs e)
    {
        var picked = GetClickedComparableFile(sender);
        if (picked == null) return;

        ViewModel.PendingCompareFile = picked.IsSameFile(ViewModel.PendingCompareFile) ? null : picked;
    }

    // "Compare file" - shared by every comparable-kind context menu in both explorers. Compares
    // the pending "left" file (see CompareSelectFile_Click) against the clicked file.
    private async void CompareWithSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        var pending = ViewModel.PendingCompareFile;
        var second = GetClickedComparableFile(sender);
        if (pending == null || second == null) return;

        await OpenCompareTabAsync(pending, second);
    }

    private static ComparableFileRef? GetClickedComparableFile(object sender) =>
        GetContextItem(sender) is { } local ? ComparableFileRef.FromLocal(local)
        : GetC64UContextItem(sender) is { } remote ? ComparableFileRef.FromC64U(remote)
        : null;

    // Resolves both files to text, computes their diff, and opens the result in a new read-only
    // File Compare tab - mirrors OpenC64UFileInEditorAsync's own in-memory-only fetch pattern
    // (no temp files) for whichever side is a real remote file.
    private async Task OpenCompareTabAsync(ComparableFileRef left, ComparableFileRef right)
    {
        if (!CompareFileResolver.CanCompare(left, right))
        {
            MessageBox.Show("These two files can't be compared - one or both are an unsupported file type.",
                "Compare Files", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var _ = BeginBusyCursor();
        try
        {
            byte[] leftBytes = await ReadComparableFileBytesAsync(left);
            byte[] rightBytes = await ReadComparableFileBytesAsync(right);

            var leftResolved = CompareFileResolver.Resolve(left.Name, leftBytes, left.Kind);
            var rightResolved = CompareFileResolver.Resolve(right.Name, rightBytes, right.Kind);

            if (leftResolved.Warning != null && rightResolved.Warning != null)
            {
                MessageBox.Show($"Could not compare these files:{Environment.NewLine}{leftResolved.Warning}{Environment.NewLine}{rightResolved.Warning}",
                    "Compare Files", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = FileCompareEngine.Compute(
                leftResolved.DisplayName, leftResolved.Text, leftResolved.IsAsciiStyled, leftResolved.Warning,
                rightResolved.DisplayName, rightResolved.Text, rightResolved.IsAsciiStyled, rightResolved.Warning,
                ignoreWhitespace: false);

            var tab = new EditorTab
            {
                DisplayName = $"{left.Name} ↔ {right.Name}",
                CompareResult = result,
            };

            ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            ViewModel.PendingCompareFile = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error comparing files: {ex.Message}", "Compare Files",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<byte[]> ReadComparableFileBytesAsync(ComparableFileRef fileRef)
    {
        if (fileRef.VirtualContent != null) return fileRef.VirtualContent;
        if (fileRef.Source == ComparableFileSource.Local) return File.ReadAllBytes(fileRef.FullPath);
        if (ViewModel.C64UFtp == null) throw new InvalidOperationException("Not connected to a C64 Ultimate.");
        return await ViewModel.C64UFtp.DownloadBytesAsync(fileRef.FullPath);
    }

    #endregion

    #region Inline Rename / Create

    private void BeginInlineRename(FileTreeItem item)
    {
        item.IsRenaming = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            var tvi = FindTreeViewItem(FileTree, item);
            if (tvi == null) return;
            var box = FindVisualChild<TextBox>(tvi, "RenameBox");
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

    private void CommitRename(TextBox box)
    {
        var item = box.DataContext as FileTreeItem;
        if (item == null || !item.IsRenaming) return;
        item.IsRenaming = false;

        if (item.IsNew)
        {
            if (item.IsFolder) CommitNewFolder(item, box.Text.Trim());
            else if (item.Kind.IsDiskImageKind()) CommitNewDiskImage(item, box.Text.Trim(), item.Kind);
            else CommitNewFile(item, box.Text.Trim());
            return;
        }

        if (item.IsVirtual)
        {
            CommitVirtualEntryRename(item, box.Text.Trim());
            return;
        }

        string newName = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        string newPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, newName);
        try
        {
            if (item.IsFolder)
            {
                Directory.Move(item.FullPath, newPath);
            }
            else
            {
                File.Move(item.FullPath, newPath);
                foreach (var tab in ViewModel.OpenTabs)
                {
                    if (string.Equals(tab.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase))
                        tab.FilePath = newPath;
                }
            }
            // Refresh only the parent folder's children instead of the whole tree, so other
            // expanded folders stay expanded.
            string parentPath = Path.GetDirectoryName(item.FullPath)!;
            RefreshAfterCreate(parentPath, newPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Renames a "virtual" entry (living only inside a disk image) via CommitRename's shared
    // inline-rename flow - unlike a real file/folder, there's no path to File.Move, so this
    // rewrites the entry's directory-slot name field in the source disk image bytes instead.
    private void CommitVirtualEntryRename(FileTreeItem item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name || item.SourcePath == null) return;

        try
        {
            byte[] diskBytes = File.ReadAllBytes(item.SourcePath);
            var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).RenameEntry(diskBytes, item.Name, newName);
            AtomicFile.WriteAllBytes(item.SourcePath, updated);
            RefreshDiskImageNode(item.SourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename \"{item.Name}\":\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelRename(TextBox box)
    {
        if (box.DataContext is not FileTreeItem item) return;
        item.IsRenaming = false;
        if (item.IsNew)
            RemovePendingItem(item);
    }

    // Inserts an editable, not-yet-created placeholder into the tree (at the boundary between
    // folders and files) and puts it straight into rename mode - mirrors VS Code's "new file"
    // flow instead of popping a separate name-entry dialog.
    private void CreateNewFileInline(FileTreeItem? parentFolder)
    {
        string parentDirectory;
        ObservableCollection<FileTreeItem> targetCollection;

        if (parentFolder != null)
        {
            parentDirectory = parentFolder.FullPath;
            parentFolder.IsExpanded = true;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentDirectory = ViewModel.Project.RootPath;
            if (string.IsNullOrEmpty(parentDirectory)) return;
            targetCollection = ViewModel.FolderItems;
        }

        int insertIndex = 0;
        while (insertIndex < targetCollection.Count && targetCollection[insertIndex].IsFolder)
            insertIndex++;

        var pendingItem = new FileTreeItem(parentDirectory, false, isNewPending: true);
        targetCollection.Insert(insertIndex, pendingItem);
        BeginInlineRename(pendingItem);
    }

    private void CommitNewFile(FileTreeItem item, string fileName)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemovePendingItem(item);
        if (string.IsNullOrWhiteSpace(fileName)) return; // no name provided - create nothing

        string path = Path.Combine(parentPath, fileName);
        try
        {
            File.WriteAllText(path, string.Empty);
            RefreshAfterCreate(parentPath, path);

            // Open the blank tab directly instead of routing through OpenFileByPath, which re-reads the
            // file from disk and, for .prg paths, runs it through the PRG binary parser - a freshly
            // created empty file is too small to be a valid PRG and would fail that parse.
            var tab = new EditorTab { FilePath = path, Language = LanguageClassifier.Classify(path) };
            ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            tab.IsModified = false;
            TrackRecentFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Mirrors CreateNewFileInline for a blank disk image; the placeholder's Kind (D64/D81) is
    // threaded through so CommitRename knows to route it to CommitNewDiskImage.
    private void CreateNewDiskImageInline(FileTreeItem? parentFolder, C64UFileKind kind)
    {
        string parentDirectory;
        ObservableCollection<FileTreeItem> targetCollection;

        if (parentFolder != null)
        {
            parentDirectory = parentFolder.FullPath;
            parentFolder.IsExpanded = true;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentDirectory = ViewModel.Project.RootPath;
            if (string.IsNullOrEmpty(parentDirectory)) return;
            targetCollection = ViewModel.FolderItems;
        }

        int insertIndex = 0;
        while (insertIndex < targetCollection.Count && targetCollection[insertIndex].IsFolder)
            insertIndex++;

        var pendingItem = new FileTreeItem(parentDirectory, isNewPending: true, kind);
        targetCollection.Insert(insertIndex, pendingItem);
        BeginInlineRename(pendingItem);
    }

    private void CommitNewDiskImage(FileTreeItem item, string diskName, C64UFileKind kind)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemovePendingItem(item);
        if (string.IsNullOrWhiteSpace(diskName)) return; // no name provided - create nothing

        string extension = kind == C64UFileKind.D64 ? ".d64" : ".d81";
        string fileName = diskName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? diskName : diskName + extension;
        string path = Path.Combine(parentPath, fileName);

        try
        {
            byte[] blankImage = DiskImage.ForKind(kind).CreateBlankImage(Path.GetFileNameWithoutExtension(fileName));
            File.WriteAllBytes(path, blankImage);
            RefreshAfterCreate(parentPath, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create disk image:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Mirrors CreateNewFileInline, just with the placeholder always sorted to the very top
    // (folders sort before files regardless of name, until the post-create reload re-sorts everything).
    private void CreateNewFolderInline(FileTreeItem? parentFolder)
    {
        string parentDirectory;
        ObservableCollection<FileTreeItem> targetCollection;

        if (parentFolder != null)
        {
            parentDirectory = parentFolder.FullPath;
            parentFolder.IsExpanded = true;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentDirectory = ViewModel.Project.RootPath;
            if (string.IsNullOrEmpty(parentDirectory)) return;
            targetCollection = ViewModel.FolderItems;
        }

        var pendingItem = new FileTreeItem(parentDirectory, true, isNewPending: true);
        targetCollection.Insert(0, pendingItem);
        BeginInlineRename(pendingItem);
    }

    private void CommitNewFolder(FileTreeItem item, string folderName)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemovePendingItem(item);
        if (string.IsNullOrWhiteSpace(folderName)) return; // no name provided - create nothing

        string path = Path.Combine(parentPath, folderName);
        try
        {
            Directory.CreateDirectory(path);
            RefreshAfterCreate(parentPath, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create folder:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Reloads only the affected folder (instead of the whole tree via LoadFolder) so sibling
    // and ancestor folders stay expanded, then selects and focuses the newly created item once
    // its TreeViewItem exists.
    private void RefreshAfterCreate(string parentPath, string newItemPath)
    {
        var parentItem = FindItemByPath(parentPath);

        if (parentItem != null)
            parentItem.RefreshChildren();
        else
            ViewModel.RefreshRootItems();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            var newItem = FindItemByPath(newItemPath);

            if (newItem == null) return;
            
            var tvi = FindTreeViewItem(FileTree, newItem);
            
            if (tvi == null) return;
            
            tvi.IsSelected = true;
            tvi.BringIntoView();
            tvi.Focus();
        });
    }

    // Returns the folder containing target, or null if target is already at the root level.
    private FileTreeItem? FindParentFolder(FileTreeItem target)
    {
        if (ViewModel.FolderItems.Contains(target)) return null;
        return FindParentFolderRecursive(ViewModel.FolderItems, target);
    }

    private static FileTreeItem? FindParentFolderRecursive(IEnumerable<FileTreeItem> items, FileTreeItem target)
    {
        foreach (var folder in items.Where(i => i.IsFolder))
        {
            if (folder.Children.Contains(target)) return folder;
            var found = FindParentFolderRecursive(folder.Children, target);
            if (found != null) return found;
        }
        return null;
    }

    private void RemovePendingItem(FileTreeItem item)
    {
        if (ViewModel.FolderItems.Remove(item)) return;
        RemoveFromChildrenRecursive(ViewModel.FolderItems, item);
    }

    private static bool RemoveFromChildrenRecursive(IEnumerable<FileTreeItem> items, FileTreeItem target)
    {
        foreach (var folder in items.Where(i => i.IsFolder))
        {
            if (folder.Children.Remove(target)) return true;
            if (RemoveFromChildrenRecursive(folder.Children, target)) return true;
        }
        return false;
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)        { CommitRename((TextBox)sender); e.Handled = true; }
        else if (e.Key == Key.Escape)   { CancelRename((TextBox)sender); e.Handled = true; }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
        => CommitRename((TextBox)sender);

    #endregion
}
