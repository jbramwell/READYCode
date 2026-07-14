// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ReadyCode.C64U;

namespace ReadyCode.Models;

/// <summary>
/// Broad category of a <see cref="C64UFileItem"/>, used to pick its context menu and badge.
/// </summary>
public enum C64UFileKind
{
    /// <summary>A folder.</summary>
    Folder,

    /// <summary>An untokenized BASIC source file (.bas).</summary>
    Bas,

    /// <summary>A tokenized program file (.prg).</summary>
    Prg,

    /// <summary>A 1541 disk image (.d64).</summary>
    D64,

    /// <summary>A 1581 disk image (.d81).</summary>
    D81,

    /// <summary>Any other file type.</summary>
    Other,
}

/// <summary>
/// Represents a single file or folder node in the C64 Ultimate FTP explorer tree, with
/// children loaded lazily over FTP.
/// </summary>
public class C64UFileItem : INotifyPropertyChanged
{
    #region Private Fields

    private readonly C64UFtpClient? _ftpClient;
    private bool _isExpanded;
    private bool _childrenLoaded;
    private bool _isRenaming;
    private bool _isDropTarget;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="C64UFileItem"/> class for an existing
    /// remote file or folder.
    /// </summary>
    /// <param name="ftpClient">The connected FTP client used to lazily load this folder's children.</param>
    /// <param name="fullPath">The full remote path.</param>
    /// <param name="isFolder">Whether the path is a folder.</param>
    /// <param name="size">The file size in bytes, or 0 for folders.</param>
    public C64UFileItem(C64UFtpClient ftpClient, string fullPath, bool isFolder, long size = 0)
    {
        _ftpClient = ftpClient;
        FullPath = fullPath;
        Name = fullPath.TrimEnd('/').Split('/').LastOrDefault() ?? fullPath;
        if (string.IsNullOrEmpty(Name)) Name = fullPath;
        IsFolder = isFolder;
        Size = size;
        Kind = DetermineKind(Name, isFolder);

        // Placeholder child so WPF shows the expand toggle arrow on folders.
        // LoadChildrenAsync() removes it on first expansion before WPF renders.
        if (isFolder)
            Children.Add(new C64UFileItem());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="C64UFileItem"/> class as a pending
    /// placeholder for an inline "new folder" entry not yet created on the server.
    /// </summary>
    /// <param name="parentDirectory">The remote directory the new folder will be created in.</param>
    public C64UFileItem(string parentDirectory)
    {
        FullPath = parentDirectory;
        Name = string.Empty;
        IsFolder = true;
        Kind = C64UFileKind.Folder;
        IsNew = true;
    }

    // Placeholder constructor used internally for the lazy-expansion child marker above.
    private C64UFileItem()
    {
        FullPath = string.Empty;
        Name = string.Empty;
        IsFolder = false;
        Kind = C64UFileKind.Other;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the file or folder's display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the full remote path.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Gets whether this item represents a folder rather than a file.
    /// </summary>
    public bool IsFolder { get; }

    /// <summary>
    /// Gets the file size in bytes, or 0 for folders.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Gets the broad category of this item, used to pick its context menu and badge.
    /// </summary>
    public C64UFileKind Kind { get; }

    /// <summary>
    /// Gets whether this is a not-yet-created placeholder for an inline "new folder" entry.
    /// While true, <see cref="FullPath"/> holds the parent directory rather than a remote path.
    /// </summary>
    public bool IsNew { get; }

    /// <summary>
    /// Gets the short type badge shown next to disk image names (e.g. "D64", "D81"), or
    /// null for item kinds that don't show a badge.
    /// </summary>
    public string? Badge => Kind switch
    {
        C64UFileKind.D64 => "D64",
        C64UFileKind.D81 => "D81",
        _ => null,
    };

    /// <summary>
    /// Gets whether this file can be sent to the C64 Ultimate to run/load/open in the editor
    /// (BASIC source or an already-tokenized program).
    /// </summary>
    public bool IsRunnable => Kind == C64UFileKind.Bas || Kind == C64UFileKind.Prg;

    /// <summary>
    /// Gets the child items, populated lazily via <see cref="LoadChildrenAsync"/>.
    /// </summary>
    public ObservableCollection<C64UFileItem> Children { get; } = new();

    /// <summary>
    /// Gets or sets whether the folder is expanded in the tree view. Setting this to true
    /// triggers an asynchronous FTP directory listing the first time a folder is expanded.
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
                _ = LoadChildrenAsync();
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

    /// <summary>
    /// Gets or sets whether this item is currently the target of a drag-and-drop operation.
    /// Not currently wired to any drag/drop events; present so this model can share the
    /// Folder Explorer's tree item style, which binds to it.
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

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads this folder's child files and folders over FTP, replacing any existing children.
    /// </summary>
    public async Task LoadChildrenAsync()
    {
        if (_childrenLoaded || _ftpClient == null) return;
        _childrenLoaded = true;

        try
        {
            var entries = await _ftpClient.ListDirectoryAsync(FullPath);
            Children.Clear();
            foreach (var entry in entries)
                Children.Add(new C64UFileItem(_ftpClient, entry.FullPath, entry.IsFolder, entry.Size));
        }
        catch
        {
            // Listing failed (disconnected, permission denied, etc.) - leave the folder collapsed-looking.
            _childrenLoaded = false;
            Children.Clear();
        }
    }

    /// <summary>
    /// Reloads this folder's children over FTP, preserving the expanded state of any child folders.
    /// </summary>
    public async Task RefreshChildrenAsync()
    {
        if (!_childrenLoaded) return;
        var expandedPaths = Children
            .Where(c => c.IsFolder && c.IsExpanded)
            .Select(c => c.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _childrenLoaded = false;
        await LoadChildrenAsync();
        foreach (var child in Children.Where(c => c.IsFolder && expandedPaths.Contains(c.FullPath)))
            child.IsExpanded = true;
    }

    #endregion

    #region Interface Implementations

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    private static C64UFileKind DetermineKind(string name, bool isFolder)
    {
        if (isFolder) return C64UFileKind.Folder;

        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".bas" => C64UFileKind.Bas,
            ".prg" => C64UFileKind.Prg,
            ".d64" => C64UFileKind.D64,
            ".d81" => C64UFileKind.D81,
            _ => C64UFileKind.Other,
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    #endregion
}
