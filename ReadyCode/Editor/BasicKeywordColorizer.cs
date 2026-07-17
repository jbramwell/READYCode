// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ReadyCode.Tokenizer;

namespace ReadyCode.Editor;

/// <summary>
/// Highlights BASIC keywords using the same greedy left-to-right scan the CBM BASIC ROM
/// tokenizer uses, so keywords packed without spaces (e.g. FORT=1TO10) are correctly
/// identified. Skips content inside string literals.
/// </summary>
public class BasicKeywordColorizer : DocumentColorizingTransformer
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw matched keywords.
    /// </summary>
    public Brush KeywordBrush { get; set; } = Brushes.Blue;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colorizes all BASIC keywords found on the given line, skipping content inside string literals.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        bool inString = false;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '"')
            {
                inString = !inString;
                i++;
                continue;
            }

            if (inString || !char.IsLetter(c))
            {
                i++;
                continue;
            }

            // At a letter outside a string: try the longest keyword that fits here.
            if (BasicTokens.TryMatchKeyword(text, i, BasicTokens.WordKeywordsLongestFirst, out string keyword))
            {
                int absoluteStart = line.Offset + i;
                ChangeLinePart(absoluteStart, absoluteStart + keyword.Length,
                    e => e.TextRunProperties.SetForegroundBrush(KeywordBrush));
                i += keyword.Length;
            }
            else
            {
                i++;
            }
        }
    }

    #endregion
}
