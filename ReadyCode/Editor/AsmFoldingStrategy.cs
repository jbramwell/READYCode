// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Linq;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace ReadyCode.Editor;

/// <summary>
/// Computes collapsible fold regions for 6502 assembly source: runs of 2+ consecutive full-line
/// ";" comments. Label-region folding is deliberately not attempted - there is no
/// dialect-agnostic, unambiguous rule for where such a region ends.
/// </summary>
public class AsmFoldingStrategy
{
    #region Public Methods

    /// <summary>
    /// Recomputes fold regions for <paramref name="document"/> and applies them to <paramref name="manager"/>.
    /// </summary>
    /// <param name="manager">The folding manager to update.</param>
    /// <param name="document">The document to compute fold regions for.</param>
    public void UpdateFoldings(FoldingManager manager, TextDocument document) =>
        manager.UpdateFoldings(CreateNewFoldings(document), -1);

    /// <summary>
    /// Computes every fold region in <paramref name="document"/>, sorted by start offset (required
    /// by <see cref="FoldingManager.UpdateFoldings"/>).
    /// </summary>
    /// <param name="document">The document to compute fold regions for.</param>
    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var lines = document.Lines.ToList();
        int runStart = -1;

        for (int i = 0; i <= lines.Count; i++)
        {
            bool isFullCommentLine = i < lines.Count && IsFullCommentLine(document, lines[i]);
            if (isFullCommentLine)
            {
                if (runStart < 0) runStart = i;
                continue;
            }

            if (runStart >= 0 && i - runStart >= 2)
                foldings.Add(new NewFolding(lines[runStart].EndOffset, lines[i - 1].EndOffset));
            runStart = -1;
        }

        return foldings;
    }

    #endregion

    #region Private Methods

    // A line's only content is a ";" comment (possibly with leading whitespace) - a trailing
    // inline comment (e.g. "LDA #0 ; note") isn't a full-line comment, so it never starts or
    // extends a run.
    private static bool IsFullCommentLine(TextDocument document, DocumentLine line)
    {
        string text = document.GetText(line).TrimStart();
        return text.StartsWith(';');
    }

    #endregion
}
