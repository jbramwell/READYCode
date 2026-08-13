// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Models;

/// <summary>
/// Which explorer panel a <see cref="ComparableFileRef"/> was selected from.
/// </summary>
public enum ComparableFileSource
{
    /// <summary>Selected from the local Folder Explorer.</summary>
    Local,

    /// <summary>Selected from the C64 Ultimate FTP Explorer.</summary>
    C64U,
}

/// <summary>
/// A lightweight, source-agnostic snapshot of a file picked for the File Compare feature - taken
/// at the moment the user chooses "Select file for comparison" or "Compare file", so the compare
/// pipeline can work uniformly over a file regardless of whether it came from the local
/// <see cref="FileTreeItem"/> tree or the remote <see cref="C64UFileItem"/> tree, without either
/// of those unrelated model classes needing a shared base type.
/// </summary>
public sealed class ComparableFileRef
{
    #region Public Properties

    /// <summary>
    /// Gets the file's display name (e.g. "GAME.BAS").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the full path to the file - a local file system path or a remote FTP path - or an
    /// empty string for a virtual entry (see <see cref="IsVirtual"/>).
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Gets the file's kind, used to decide how to resolve it to text and which two files are
    /// allowed to be compared with each other.
    /// </summary>
    public required C64UFileKind Kind { get; init; }

    /// <summary>
    /// Gets which explorer panel this file was selected from.
    /// </summary>
    public required ComparableFileSource Source { get; init; }

    /// <summary>
    /// Gets the file's raw content if it's a "virtual" entry read from inside a mounted disk
    /// image, already in memory - or null if its bytes still need to be fetched (from disk or
    /// over FTP) when the comparison actually runs.
    /// </summary>
    public byte[]? VirtualContent { get; init; }

    /// <summary>
    /// Gets the full path of the disk image this file was read from, for a "virtual" entry, or
    /// null otherwise. Display-only - re-fetching a virtual entry's bytes isn't needed since
    /// <see cref="VirtualContent"/> already holds them.
    /// </summary>
    public string? VirtualSourcePath { get; init; }

    /// <summary>
    /// Gets whether this reference is a "virtual" entry read from inside a mounted disk image,
    /// as opposed to a real file on disk or over FTP.
    /// </summary>
    public bool IsVirtual => VirtualContent != null;

    #endregion

    #region Public Methods

    /// <summary>
    /// Creates a <see cref="ComparableFileRef"/> snapshot of a local Folder Explorer file.
    /// </summary>
    public static ComparableFileRef FromLocal(FileTreeItem item) => new()
    {
        Name = item.Name,
        FullPath = item.FullPath,
        Kind = item.Kind,
        Source = ComparableFileSource.Local,
        VirtualContent = item.Content,
        VirtualSourcePath = item.SourcePath,
    };

    /// <summary>
    /// Creates a <see cref="ComparableFileRef"/> snapshot of a C64 Ultimate FTP Explorer file.
    /// </summary>
    public static ComparableFileRef FromC64U(C64UFileItem item) => new()
    {
        Name = item.Name,
        FullPath = item.FullPath,
        Kind = item.Kind,
        Source = ComparableFileSource.C64U,
        VirtualContent = item.Content,
        VirtualSourcePath = item.SourcePath,
    };

    /// <summary>
    /// Gets whether this reference points at the exact same file as <paramref name="other"/> -
    /// used to detect when the user re-selects the currently-pending comparison file (which
    /// clears the pending selection instead of replacing it).
    /// </summary>
    public bool IsSameFile(ComparableFileRef? other)
    {
        if (other == null) return false;
        if (Source != other.Source) return false;

        // A virtual entry has no real FullPath, so identity is its disk image path + name instead.
        if (IsVirtual || other.IsVirtual)
            return IsVirtual && other.IsVirtual
                && string.Equals(VirtualSourcePath, other.VirtualSourcePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

        return string.Equals(FullPath, other.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
