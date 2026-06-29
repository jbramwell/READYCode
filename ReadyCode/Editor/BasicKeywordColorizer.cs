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
    #region Private Fields

    // All word-style keywords (start with a letter), sorted longest-first so that greedy
    // matching picks the right one when keywords share a prefix (GOSUB before GO,
    // PRINT# before PRINT, RESTORE before REM, LEFT$ before LET, etc.).
    private static readonly string[] _keywords =
        BasicTokens.TokenMap.Keys
            .Where(k => char.IsLetter(k[0]))
            .OrderByDescending(k => k.Length)
            .ToArray();

    #endregion

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
            bool matched = false;
            foreach (string keyword in _keywords)
            {
                int kwLen = keyword.Length;
                if (i + kwLen > text.Length) continue;

                bool isMatch = true;
                for (int k = 0; k < kwLen; k++)
                {
                    if (char.ToUpperInvariant(text[i + k]) != keyword[k])
                    {
                        isMatch = false;
                        break;
                    }
                }

                if (!isMatch) continue;

                int absoluteStart = line.Offset + i;
                ChangeLinePart(absoluteStart, absoluteStart + kwLen,
                    e => e.TextRunProperties.SetForegroundBrush(KeywordBrush));
                i += kwLen;
                matched = true;
                break;
            }

            if (!matched)
                i++;
        }
    }

    #endregion
}
