// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace ReadyCode.Diff;

/// <summary>
/// Computes a <see cref="FileCompareResult"/> from two already-resolved texts, using DiffPlex for
/// the underlying line and word-level diff. Free of WPF/AvalonEdit types so it's directly unit
/// testable.
/// </summary>
public static class FileCompareEngine
{
    #region Private Fields

    // Runs of this many or more consecutive unchanged lines/rows collapse by default in the
    // compare view (see FileCompareResult.SplitCollapsedRuns/UnifiedCollapsedRuns), the same way
    // GitHub hides long unchanged context behind an "Expand" affordance.
    private const int CollapseThreshold = 7;

    #endregion

    #region Public Methods

    /// <summary>
    /// Computes the split and unified diff models for two files' text, along with the hunk and
    /// collapsed-region analysis the compare view needs.
    /// </summary>
    public static FileCompareResult Compute(
        string leftName, string leftText, bool leftIsAsciiStyled, string? leftWarning,
        string rightName, string rightText, bool rightIsAsciiStyled, string? rightWarning,
        bool ignoreWhitespace)
    {
        // WordChunker.Instance as the sub-piece chunker lets DiffPlex populate DiffPiece.SubPieces
        // with a word-level diff of each paired "Modified" line, so the rendering layer can
        // highlight just the differing word(s) rather than the whole line.
        var sideBySideBuilder = new SideBySideDiffBuilder(Differ.Instance, LineChunker.Instance, WordChunker.Instance);
        var sideBySide = sideBySideBuilder.BuildDiffModel(leftText, rightText, ignoreWhitespace, ignoreCase: false);

        // The unified view is derived from the split model rather than built via a separate
        // InlineDiffBuilder call: unlike SideBySideDiffBuilder (whose two-chunker constructor
        // cleanly separates the outer line-level chunker from the sub-piece word chunker),
        // InlineDiffBuilder.BuildDiffModel's lone IChunker parameter sets the *outer* granularity
        // for the whole document - passing WordChunker there diffs the entire file word-by-word
        // instead of line-by-line. Deriving from the already-correct, already word-sub-diffed
        // split model sidesteps that entirely and keeps both views consistent.
        var unified = BuildUnifiedFromSideBySide(sideBySide);

        int rowCount = Math.Min(sideBySide.OldText.Lines.Count, sideBySide.NewText.Lines.Count);
        var (splitHunkStarts, splitCollapsed) = AnalyzeRuns(
            row => sideBySide.OldText.Lines[row].Type != ChangeType.Unchanged
                || sideBySide.NewText.Lines[row].Type != ChangeType.Unchanged,
            rowCount);

        var (unifiedHunkStarts, unifiedCollapsed) = AnalyzeRuns(
            line => unified.Lines[line].Type != ChangeType.Unchanged,
            unified.Lines.Count);

        // Per-side/per-type runs for the gutter change-indicator strips - unlike
        // SplitHunkStartRows/UnifiedHunkStartLines (which combine both sides into one navigable
        // hunk), these are kept separate so each strip can color a mark red or green and size it
        // to the actual number of lines that side contributes, rather than one fixed-size mark per
        // hunk regardless of how big it is.
        var leftChangeRuns = FindRuns(
            row => sideBySide.OldText.Lines[row].Type is ChangeType.Deleted or ChangeType.Modified, rowCount);
        var rightChangeRuns = FindRuns(
            row => sideBySide.NewText.Lines[row].Type is ChangeType.Inserted or ChangeType.Modified, rowCount);
        var unifiedDeletedRuns = FindRuns(line => unified.Lines[line].Type == ChangeType.Deleted, unified.Lines.Count);
        var unifiedInsertedRuns = FindRuns(line => unified.Lines[line].Type == ChangeType.Inserted, unified.Lines.Count);

        return new FileCompareResult
        {
            LeftName = leftName,
            RightName = rightName,
            LeftText = leftText,
            RightText = rightText,
            LeftIsAsciiStyled = leftIsAsciiStyled,
            RightIsAsciiStyled = rightIsAsciiStyled,
            LeftWarning = leftWarning,
            RightWarning = rightWarning,
            SideBySide = sideBySide,
            Unified = unified,
            SplitHunkStartRows = splitHunkStarts,
            UnifiedHunkStartLines = unifiedHunkStarts,
            SplitCollapsedRuns = splitCollapsed,
            UnifiedCollapsedRuns = unifiedCollapsed,
            LeftChangeRuns = leftChangeRuns,
            RightChangeRuns = rightChangeRuns,
            UnifiedDeletedRuns = unifiedDeletedRuns,
            UnifiedInsertedRuns = unifiedInsertedRuns,
        };
    }

    #endregion

    #region Private Methods

    // Walks the split model's aligned rows and flattens them into a single GitHub-style unified
    // listing: an unchanged row emits one line, a pure insert/delete row emits just its one real
    // side (skipping the other side's Imaginary alignment padding), and a Modified row - paired
    // old/new lines that are similar but not identical - emits the old line first (Deleted
    // flavor) then the new line (Inserted flavor), carrying over the SubPieces the split model
    // already computed so word-level highlighting stays identical between the two views.
    private static DiffPaneModel BuildUnifiedFromSideBySide(SideBySideDiffModel sideBySide)
    {
        var pane = new DiffPaneModel();
        int rowCount = Math.Min(sideBySide.OldText.Lines.Count, sideBySide.NewText.Lines.Count);

        for (int row = 0; row < rowCount; row++)
        {
            var oldPiece = sideBySide.OldText.Lines[row];
            var newPiece = sideBySide.NewText.Lines[row];

            if (oldPiece.Type == ChangeType.Unchanged && newPiece.Type == ChangeType.Unchanged)
            {
                pane.Lines.Add(new DiffPiece(oldPiece.Text, ChangeType.Unchanged, oldPiece.Position));
                continue;
            }

            if (oldPiece.Type != ChangeType.Imaginary)
            {
                var type = oldPiece.Type == ChangeType.Modified ? ChangeType.Deleted : oldPiece.Type;
                pane.Lines.Add(new DiffPiece(oldPiece.Text, type, oldPiece.Position) { SubPieces = oldPiece.SubPieces });
            }

            if (newPiece.Type != ChangeType.Imaginary)
            {
                var type = newPiece.Type == ChangeType.Modified ? ChangeType.Inserted : newPiece.Type;
                pane.Lines.Add(new DiffPiece(newPiece.Text, type, newPiece.Position) { SubPieces = newPiece.SubPieces });
            }
        }

        return pane;
    }

    // Scans a sequence of "is this row/line part of a change" flags once, returning both the
    // 0-based start index of every maximal run of changed entries (for Previous/Next hunk
    // navigation) and every maximal run of CollapseThreshold+ unchanged entries (for default
    // collapsing of long unchanged context).
    private static (IReadOnlyList<int> HunkStarts, IReadOnlyList<(int Start, int Count)> CollapsedRuns) AnalyzeRuns(
        Func<int, bool> isChanged, int count)
    {
        var hunkStarts = FindRuns(isChanged, count).Select(r => r.Start).ToList();
        var collapsedRuns = FindRuns(i => !isChanged(i), count).Where(r => r.Count >= CollapseThreshold).ToList();
        return (hunkStarts, collapsedRuns);
    }

    // Finds every maximal run of consecutive indices where predicate is true, as (Start, Count)
    // pairs - the shared building block behind hunk/collapse analysis and the gutter strips'
    // per-side/per-type colored, size-proportional run lists.
    private static List<(int Start, int Count)> FindRuns(Func<int, bool> predicate, int count)
    {
        var runs = new List<(int Start, int Count)>();
        int i = 0;
        while (i < count)
        {
            if (!predicate(i)) { i++; continue; }

            int start = i;
            while (i < count && predicate(i))
                i++;
            runs.Add((start, i - start));
        }

        return runs;
    }

    #endregion
}
