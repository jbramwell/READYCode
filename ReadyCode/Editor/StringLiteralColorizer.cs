// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Colors string literals in BASIC code - a double quote, everything up to the next double
/// quote (or end of line, if unterminated), and that closing quote - a single color.
/// </summary>
public class StringLiteralColorizer : DocumentColorizingTransformer
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw string literals, including their quotes.
    /// </summary>
    public Brush StringBrush { get; set; } = Brushes.Orange;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colors all string literals found on the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] != '"')
            {
                i++;
                continue;
            }

            int start = i;
            i++;
            while (i < text.Length && text[i] != '"') i++;
            if (i < text.Length) i++; // include the closing quote

            int absoluteStart = line.Offset + start;
            int absoluteEnd   = line.Offset + i;
            ChangeLinePart(absoluteStart, absoluteEnd, e => e.TextRunProperties.SetForegroundBrush(StringBrush));
        }
    }

    #endregion
}
