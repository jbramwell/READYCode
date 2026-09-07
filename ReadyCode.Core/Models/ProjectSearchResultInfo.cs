// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReadyCode.Models;

/// <summary>
/// Represents a single file with at least one match in the project-wide Search panel's results
/// tree, with every match in that file as a child node.
/// </summary>
public class ProjectSearchFileResult : INotifyPropertyChanged
{
    #region Private Fields

    private bool _isExpanded = true;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchFileResult"/> class.
    /// </summary>
    /// <param name="filePath">The full path of the matching file.</param>
    /// <param name="relativeDisplayPath">The file's path relative to the open project's root, for display.</param>
    public ProjectSearchFileResult(string filePath, string relativeDisplayPath)
    {
        FilePath = filePath;
        RelativeDisplayPath = relativeDisplayPath;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the full path of the matching file.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the file's path relative to the open project's root, shown in the results tree.
    /// </summary>
    public string RelativeDisplayPath { get; }

    /// <summary>
    /// Gets every match found in this file, in document order.
    /// </summary>
    public ObservableCollection<ProjectSearchMatchInfo> Matches { get; } = new();

    /// <summary>
    /// Gets or sets whether this file's node is expanded in the results tree.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
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

/// <summary>
/// Represents a single match within a file, shown as a leaf node under its
/// <see cref="ProjectSearchFileResult"/> in the project-wide Search panel's results tree.
/// </summary>
public class ProjectSearchMatchInfo
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchMatchInfo"/> class.
    /// </summary>
    /// <param name="file">The file this match was found in.</param>
    /// <param name="lineNumber">The 1-based document line this match is on.</param>
    /// <param name="columnOffset">The 0-based character offset of the match within the line.</param>
    /// <param name="matchLength">The length, in characters, of the matched text.</param>
    /// <param name="linePreviewText">The line's trimmed text, for display.</param>
    public ProjectSearchMatchInfo(ProjectSearchFileResult file, int lineNumber, int columnOffset, int matchLength, string linePreviewText)
    {
        File = file;
        LineNumber = lineNumber;
        ColumnOffset = columnOffset;
        MatchLength = matchLength;
        LinePreviewText = linePreviewText;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the file this match was found in.
    /// </summary>
    public ProjectSearchFileResult File { get; }

    /// <summary>
    /// Gets the 1-based document line this match is on.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the 0-based character offset of the match within its line.
    /// </summary>
    public int ColumnOffset { get; }

    /// <summary>
    /// Gets the length, in characters, of the matched text.
    /// </summary>
    public int MatchLength { get; }

    /// <summary>
    /// Gets the line's trimmed text, shown as a preview of the match's context.
    /// </summary>
    public string LinePreviewText { get; }

    /// <summary>
    /// Gets the text shown for this node, e.g. "Line 10: PRINT "HELLO"".
    /// </summary>
    public string DisplayText => $"Line {LineNumber}: {LinePreviewText}";

    #endregion
}
