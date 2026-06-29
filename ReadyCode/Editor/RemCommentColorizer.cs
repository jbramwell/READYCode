// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Colors the REM keyword and all trailing comment text in a BASIC line.
/// Runs after BasicKeywordColorizer so it overrides the keyword color on REM itself.
/// Skips REM occurrences inside string literals.
/// </summary>
public class RemCommentColorizer : DocumentColorizingTransformer
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw the REM keyword and its trailing comment text.
    /// </summary>
    public Brush CommentBrush { get; set; } = Brushes.Green;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colors the REM keyword and the remainder of the line, if a REM is found outside a string literal.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        bool inString = false;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            // Greedy match: REM at this exact position (no word-boundary guards —
            // CBM BASIC tokenizes LOREM as L·O·REM, coloring from REM to end of line).
            if (i + 3 > text.Length) break;

            bool isRem = char.ToUpperInvariant(text[i])     == 'R'
                      && char.ToUpperInvariant(text[i + 1]) == 'E'
                      && char.ToUpperInvariant(text[i + 2]) == 'M';

            if (!isRem) continue;

            int start = line.Offset + i;
            int end   = line.Offset + text.Length;
            ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(CommentBrush));
            return;
        }
    }

    #endregion
}
