// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Editor;

/// <summary>
/// Computes collapsible fold regions for 6502 assembly source: runs of 2+ consecutive full-line
/// ";" comments. Label-region folding is deliberately not attempted - there is no
/// dialect-agnostic, unambiguous rule for where such a region ends. Free of any editor-control
/// types so it lives in the cross-platform core.
/// </summary>
public static class AsmFoldRegionFinder
{
    #region Public Methods

    /// <summary>
    /// Computes every fold region in <paramref name="text"/>, sorted by start offset.
    /// </summary>
    /// <param name="text">The full assembly source text.</param>
    public static List<FoldRegion> Find(string text) => Find(SourceLine.Split(text));

    /// <summary>
    /// Computes every fold region across <paramref name="lines"/>, sorted by start offset.
    /// </summary>
    /// <param name="lines">The document's lines, in order.</param>
    public static List<FoldRegion> Find(IReadOnlyList<SourceLine> lines)
    {
        var foldings = new List<FoldRegion>();
        int runStart = -1;

        for (int i = 0; i <= lines.Count; i++)
        {
            bool isFullCommentLine = i < lines.Count && IsFullCommentLine(lines[i]);
            if (isFullCommentLine)
            {
                if (runStart < 0) runStart = i;
                continue;
            }

            if (runStart >= 0 && i - runStart >= 2)
                foldings.Add(new FoldRegion(lines[runStart].EndOffset, lines[i - 1].EndOffset));
            runStart = -1;
        }

        return foldings;
    }

    #endregion

    #region Private Methods

    // A line's only content is a ";" comment (possibly with leading whitespace) - a trailing
    // inline comment (e.g. "LDA #0 ; note") isn't a full-line comment, so it never starts or
    // extends a run.
    private static bool IsFullCommentLine(SourceLine line) => line.Text.TrimStart().StartsWith(';');

    #endregion
}
