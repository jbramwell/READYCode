// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using DiffPlex.DiffBuilder.Model;

namespace ReadyCode.Diff;

/// <summary>
/// The computed result of comparing two files' text via <see cref="FileCompareEngine"/> - both
/// DiffPlex's own split and unified diff models, plus the hunk/fold analysis
/// <see cref="Views.FileCompareControl"/> needs for Previous/Next navigation and collapsing long
/// unchanged runs. Deliberately free of any WPF/AvalonEdit types so it (and the engine that
/// produces it) can be unit tested from <c>ReadyCode.Tests</c>, which has WPF disabled.
/// </summary>
public sealed class FileCompareResult
{
    #region Public Properties

    /// <summary>Gets the left (old) file's display name.</summary>
    public required string LeftName { get; init; }

    /// <summary>Gets the right (new) file's display name.</summary>
    public required string RightName { get; init; }

    /// <summary>
    /// Gets the left file's resolved text that was diffed, kept so the compare view can
    /// recompute <see cref="SideBySide"/>/<see cref="Unified"/> locally (e.g. when the user
    /// toggles "ignore whitespace") without re-fetching or re-resolving the file.
    /// </summary>
    public required string LeftText { get; init; }

    /// <summary>Gets the right file's resolved text that was diffed - see <see cref="LeftText"/>.</summary>
    public required string RightText { get; init; }

    /// <summary>
    /// Gets whether the left file should render in the ASCII/Consolas editor font, as opposed to
    /// the PETSCII font (see <see cref="CompareFileResolver.Resolve"/>).
    /// </summary>
    public required bool LeftIsAsciiStyled { get; init; }

    /// <summary>Gets whether the right file should render in the ASCII/Consolas editor font.</summary>
    public required bool RightIsAsciiStyled { get; init; }

    /// <summary>
    /// Gets a warning message if the left file couldn't be fully resolved to meaningful text
    /// (e.g. detokenization or disassembly failed), or null if it resolved cleanly.
    /// </summary>
    public string? LeftWarning { get; init; }

    /// <summary>Gets a warning message for the right file, or null if it resolved cleanly.</summary>
    public string? RightWarning { get; init; }

    /// <summary>
    /// Gets the side-by-side (split view) diff model, with both sides padded to equal length by
    /// DiffPlex's own <see cref="ChangeType.Imaginary"/> rows so the two panes stay vertically
    /// aligned.
    /// </summary>
    public required SideBySideDiffModel SideBySide { get; init; }

    /// <summary>
    /// Gets the unified (single-pane, inline) diff model.
    /// </summary>
    public required DiffPaneModel Unified { get; init; }

    /// <summary>
    /// Gets the 0-based row indices, into <see cref="SideBySide"/>'s equal-length
    /// <c>OldText.Lines</c>/<c>NewText.Lines</c>, where each change hunk begins - for the split
    /// view's Previous/Next navigation.
    /// </summary>
    public required IReadOnlyList<int> SplitHunkStartRows { get; init; }

    /// <summary>
    /// Gets the 0-based line indices into <see cref="Unified"/>'s <c>Lines</c> where each change
    /// hunk begins - for the unified view's Previous/Next navigation.
    /// </summary>
    public required IReadOnlyList<int> UnifiedHunkStartLines { get; init; }

    /// <summary>
    /// Gets the (Start, Count) row ranges, into <see cref="SideBySide"/>'s aligned rows, of
    /// unchanged runs long enough to collapse by default in the split view.
    /// </summary>
    public required IReadOnlyList<(int Start, int Count)> SplitCollapsedRuns { get; init; }

    /// <summary>
    /// Gets the (Start, Count) line ranges, into <see cref="Unified"/>'s lines, of unchanged runs
    /// long enough to collapse by default in the unified view.
    /// </summary>
    public required IReadOnlyList<(int Start, int Count)> UnifiedCollapsedRuns { get; init; }

    /// <summary>
    /// Gets the (Start, Count) row ranges, into <see cref="SideBySide"/>'s aligned rows, where the
    /// left/old pane has real (non-Imaginary) deleted or modified content - drives the left gutter
    /// change-indicator strip's red marks, each sized to the run's actual line count.
    /// </summary>
    public required IReadOnlyList<(int Start, int Count)> LeftChangeRuns { get; init; }

    /// <summary>
    /// Gets the (Start, Count) row ranges, into <see cref="SideBySide"/>'s aligned rows, where the
    /// right/new pane has real (non-Imaginary) inserted or modified content - drives the right
    /// gutter change-indicator strip's green marks.
    /// </summary>
    public required IReadOnlyList<(int Start, int Count)> RightChangeRuns { get; init; }

    /// <summary>
    /// Gets the (Start, Count) line ranges, into <see cref="Unified"/>'s lines, of deleted runs -
    /// drives the unified gutter change-indicator strip's red marks.
    /// </summary>
    public required IReadOnlyList<(int Start, int Count)> UnifiedDeletedRuns { get; init; }

    /// <summary>
    /// Gets the (Start, Count) line ranges, into <see cref="Unified"/>'s lines, of inserted runs -
    /// drives the unified gutter change-indicator strip's green marks.
    /// </summary>
    public required IReadOnlyList<(int Start, int Count)> UnifiedInsertedRuns { get; init; }

    #endregion
}
