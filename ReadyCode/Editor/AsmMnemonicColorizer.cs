// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ReadyCode.Tokenizer;

namespace ReadyCode.Editor;

/// <summary>
/// Highlights 6502 mnemonics. Every standard mnemonic is exactly 3 characters, so a match
/// requires a word boundary on both sides (e.g. the "ADC" inside a longer identifier like
/// "MYADCVAL" is not colored, and the "ADC" in a hex literal like "$ADCD" is excluded by its
/// missing right boundary). Runs before <see cref="AsmCommentColorizer"/>, which overrides any
/// mnemonic-looking text inside a comment.
/// </summary>
public class AsmMnemonicColorizer : DocumentColorizingTransformer
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw matched mnemonics.
    /// </summary>
    public Brush MnemonicBrush { get; set; } = Brushes.Blue;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colorizes all mnemonics found on the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int i = 0;

        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]))
            {
                i++;
                continue;
            }

            bool leftBoundary = i == 0 || !IsWordChar(text[i - 1]);
            if (leftBoundary && i + 3 <= text.Length)
            {
                bool rightBoundary = i + 3 == text.Length || !IsWordChar(text[i + 3]);
                if (rightBoundary && AsmTokens.IsMnemonic(text.Substring(i, 3)))
                {
                    int absoluteStart = line.Offset + i;
                    ChangeLinePart(absoluteStart, absoluteStart + 3,
                        e => e.TextRunProperties.SetForegroundBrush(MnemonicBrush));
                    i += 3;
                    continue;
                }
            }

            // Not a mnemonic at this position - skip the rest of this identifier so its
            // interior letters aren't re-tested at a non-boundary position.
            while (i < text.Length && IsWordChar(text[i])) i++;
        }
    }

    #endregion

    #region Private Methods

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion
}
