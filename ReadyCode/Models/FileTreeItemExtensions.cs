// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Models;

/// <summary>
/// Bridges the WPF-side <see cref="FileTreeItem"/> to the cross-platform core's file models.
/// </summary>
public static class FileTreeItemExtensions
{
    #region Public Methods

    /// <summary>
    /// Creates a <see cref="ComparableFileRef"/> snapshot of a local Folder Explorer file.
    /// </summary>
    public static ComparableFileRef ToComparableFileRef(this FileTreeItem item) => new()
    {
        Name = item.Name,
        FullPath = item.FullPath,
        Kind = item.Kind,
        Source = ComparableFileSource.Local,
        VirtualContent = item.Content,
        VirtualSourcePath = item.SourcePath,
    };

    #endregion
}
