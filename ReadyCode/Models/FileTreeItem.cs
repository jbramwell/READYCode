// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace ReadyCode.Models;

/// <summary>
/// Represents a single file or folder node in the folder explorer tree, with children loaded lazily.
/// </summary>
public class FileTreeItem : INotifyPropertyChanged
{
    #region Private Fields

    private bool _isExpanded;
    private bool _childrenLoaded;
    private bool _isDropTarget;
    private bool _isRenaming;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTreeItem"/> class for an existing file or folder.
    /// </summary>
    /// <param name="path">The full file system path.</param>
    /// <param name="isFolder">Whether the path is a folder.</param>
    public FileTreeItem(string path, bool isFolder)
    {
        FullPath = path;
        Name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(Name)) Name = path;
        IsFolder = isFolder;

        // Placeholder child so WPF shows the expand toggle arrow on folders.
        // LoadChildren() removes it on first expansion before WPF renders.
        if (isFolder)
            Children.Add(new FileTreeItem());
    }

    // Placeholder constructor used internally for the lazy-expansion child marker above.
    private FileTreeItem()
    {
        FullPath = string.Empty;
        Name = string.Empty;
        IsFolder = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTreeItem"/> class as a pending placeholder
    /// for an inline "new file" or "new folder" entry not yet created on disk.
    /// </summary>
    /// <param name="parentDirectory">The directory the new entry will be created in.</param>
    /// <param name="isFolder">Whether the pending entry is a folder.</param>
    /// <param name="isNewPending">Whether this is a pending placeholder. Always true for this overload.</param>
    public FileTreeItem(string parentDirectory, bool isFolder, bool isNewPending)
    {
        FullPath = parentDirectory;
        Name = string.Empty;
        IsFolder = isFolder;
        IsNew = isNewPending;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the file or folder's display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the full file system path.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Gets whether this item represents a folder rather than a file.
    /// </summary>
    public bool IsFolder { get; }

    /// <summary>
    /// Gets the child items, populated lazily via <see cref="LoadChildren"/>.
    /// </summary>
    public ObservableCollection<FileTreeItem> Children { get; } = new();

    /// <summary>
    /// Gets whether this is a not-yet-created placeholder for an inline "new file" entry.
    /// While true, <see cref="FullPath"/> holds the parent directory rather than a file path.
    /// </summary>
    public bool IsNew { get; }

    /// <summary>
    /// Gets or sets whether the folder is expanded in the tree view.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            if (value && IsFolder && !_childrenLoaded)
                LoadChildren();
        }
    }

    /// <summary>
    /// Gets or sets whether this item is currently the target of a drag-and-drop operation.
    /// </summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value) return;
            _isDropTarget = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets whether this item is currently being renamed inline.
    /// </summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value) return;
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads this folder's child files and folders from disk, replacing any existing children.
    /// </summary>
    public void LoadChildren()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;
        Children.Clear();

        try
        {
            foreach (string dir in Directory.GetDirectories(FullPath)
                                            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                Children.Add(new FileTreeItem(dir, true));

            foreach (string file in Directory.GetFiles(FullPath)
                                             .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                Children.Add(new FileTreeItem(file, false));
        }
        catch { /* Access denied, path too long, etc. */ }
    }

    /// <summary>
    /// Reloads this folder's children from disk, preserving the expanded state of any child folders.
    /// </summary>
    public void RefreshChildren()
    {
        if (!_childrenLoaded) return;
        var expandedPaths = Children
            .Where(c => c.IsFolder && c.IsExpanded)
            .Select(c => c.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _childrenLoaded = false;
        LoadChildren();
        foreach (var child in Children.Where(c => c.IsFolder && expandedPaths.Contains(c.FullPath)))
            child.IsExpanded = true;
    }

    /// <summary>
    /// Collapses this item and all of its descendants.
    /// </summary>
    public void CollapseAll()
    {
        IsExpanded = false;
        foreach (var child in Children)
            child.CollapseAll();
    }

    #endregion

    #region Interface Implementations

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    private void OnPropertyChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    #endregion
}
