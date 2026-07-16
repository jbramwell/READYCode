// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ReadyCode.C64U;
using ReadyCode.Tokenizer;

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
        Kind = DetermineKind(path, isFolder);

        // Placeholder child so WPF shows the expand toggle arrow on folders and .d64 disk images.
        // LoadChildren() removes it on first expansion before WPF renders.
        if (isFolder || Kind == C64UFileKind.D64)
            Children.Add(new FileTreeItem());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTreeItem"/> class for a "virtual" file
    /// that exists only inside a disk image (e.g. a .d64), backed by already-extracted bytes
    /// rather than a real path on disk.
    /// </summary>
    /// <param name="name">The file's name, as read from the disk image's directory.</param>
    /// <param name="content">The file's raw content, already extracted from the disk image.</param>
    /// <param name="kind">The file kind, used to pick its badge and whether it can be opened.</param>
    /// <param name="sourcePath">The full path of the disk image this file was read from.</param>
    public FileTreeItem(string name, byte[] content, C64UFileKind kind, string sourcePath)
    {
        FullPath = string.Empty;
        Name = name;
        IsFolder = false;
        Kind = kind;
        Content = content;
        SourcePath = sourcePath;
    }

    // Placeholder constructor used internally for the lazy-expansion child marker above.
    private FileTreeItem()
    {
        FullPath = string.Empty;
        Name = string.Empty;
        IsFolder = false;
        Kind = C64UFileKind.Other;
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
        Kind = isFolder ? C64UFileKind.Folder : C64UFileKind.Other;
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
    /// Gets the broad category of this item, used to pick its badge and (for .d64 disk images)
    /// whether it can be expanded.
    /// </summary>
    public C64UFileKind Kind { get; }

    /// <summary>
    /// Gets the short type badge shown right-aligned next to this item's name (e.g. "D64",
    /// "PRG", "BAS", "ML"), or null for kinds that don't show one (folders and unrecognized
    /// file types).
    /// </summary>
    public string? Badge => Kind switch
    {
        C64UFileKind.D64 => "D64",
        C64UFileKind.D81 => "D81",
        C64UFileKind.Prg => "PRG",
        C64UFileKind.Bas => "BAS",
        C64UFileKind.Ml => "ML",
        _ => null,
    };

    /// <summary>
    /// Gets whether this file can be opened in the editor (BASIC source or a confirmed-BASIC
    /// tokenized program).
    /// </summary>
    public bool IsRunnable => Kind == C64UFileKind.Bas || Kind == C64UFileKind.Prg;

    /// <summary>
    /// Gets the Segoe MDL2 Assets glyph shown next to this item's name: a floppy disk glyph for
    /// disk images, a document glyph for other files, and a folder glyph for folders - the same
    /// glyphs used for the same file/folder types in the C64U Explorer.
    /// </summary>
    public string IconGlyph
    {
        get
        {
            if (IsFolder) return "";

            return Kind switch
            {
                C64UFileKind.D64 or C64UFileKind.D81 => "",
                _ => "",
            };
        }
    }

    /// <summary>
    /// Gets the raw content of this file if it's a "virtual" entry read from inside a disk
    /// image, or null for a real file on disk.
    /// </summary>
    public byte[]? Content { get; }

    /// <summary>
    /// Gets whether this item exists only inside a disk image rather than as a real file on
    /// disk - it has no <see cref="FullPath"/> and its content is already in memory.
    /// </summary>
    public bool IsVirtual => Content != null;

    /// <summary>
    /// Gets the full path of the disk image this file was read from, for a "virtual" entry, or
    /// null for a real file on disk.
    /// </summary>
    public string? SourcePath { get; }

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
            if (value && (IsFolder || Kind == C64UFileKind.D64) && !_childrenLoaded)
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
    /// Loads this folder's child files and folders from disk, or - for a .d64 disk image - the
    /// files stored inside it, replacing any existing children.
    /// </summary>
    public void LoadChildren()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;
        Children.Clear();

        if (Kind == C64UFileKind.D64)
        {
            try
            {
                var diskBytes = File.ReadAllBytes(FullPath);
                var entries = new D64Image().ReadDirectory(diskBytes);
                foreach (var entry in entries)
                    Children.Add(new FileTreeItem(entry.Name, entry.Content, entry.Kind, FullPath));
            }
            catch
            {
                // Read/parse failed (not a standard 35-track image, locked file, etc.).
                _childrenLoaded = false;
                Children.Clear();
            }
            return;
        }

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

    private static C64UFileKind DetermineKind(string path, bool isFolder)
    {
        if (isFolder) return C64UFileKind.Folder;

        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".bas": return C64UFileKind.Bas;
            case ".d64": return C64UFileKind.D64;
            case ".d81": return C64UFileKind.D81;
            case ".prg":
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    return new PrgConverter().IsBasicProgram(bytes) ? C64UFileKind.Prg : C64UFileKind.Ml;
                }
                catch
                {
                    // Couldn't read the file (locked, permissions, etc.) - fall back to the
                    // previous always-treat-.prg-as-runnable behavior rather than guessing wrong.
                    return C64UFileKind.Prg;
                }
            default: return C64UFileKind.Other;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    #endregion
}
