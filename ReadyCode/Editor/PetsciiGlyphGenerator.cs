// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ICSharpCode.AvalonEdit.Rendering;
using ReadyCode.Tokenizer;

namespace ReadyCode.Editor;

/// <summary>
/// Renders bytes outside the normal printable ASCII range using the glyph the C64 character ROM
/// shows for that screen code (e.g. CHR$(147)/CLR renders as the reverse-video heart), without
/// altering the underlying document text. This keeps round-tripping and existing text-based
/// features (keyword highlighting, line-number padding, tokenizing) working unchanged, since they
/// only ever see the original characters.
/// </summary>
public class PetsciiGlyphGenerator : VisualLineElementGenerator
{
    #region Public Methods

    /// <summary>
    /// Finds the offset of the next character that needs PETSCII glyph substitution.
    /// </summary>
    /// <param name="startOffset">The offset to search from.</param>
    /// <returns>The offset of the next character to substitute, or -1 if none remain on the line.</returns>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var document = CurrentContext.Document;
        int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;

        for (int i = startOffset; i < endOffset; i++)
        {
            char c = document.GetCharAt(i);
            // Also intercept '^' (0x5E): PETSCII maps it to screen code 0x1E (↑ up-arrow),
            // the C64 power operator, but the font renders 0x5E as a standard caret.
            if (c < 0x20 || c > 0x7E || c == '^')
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Constructs the visual element that renders the PETSCII glyph for the character at <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The offset of the character to substitute.</param>
    /// <returns>The glyph element, or null if the character is outside the representable range.</returns>
    public override VisualLineElement? ConstructElement(int offset)
    {
        char ch = CurrentContext.Document.GetCharAt(offset);
        if (ch > 0xFF)
            return null;

        byte screenCode = PetsciiScreenCodeMap.ToScreenCode((byte)ch);
        string glyph = ((char)(0xE000 + screenCode)).ToString();
        return new PetsciiGlyphElement(glyph);
    }

    #endregion

    private sealed class PetsciiGlyphElement : VisualLineElement
    {
        #region Private Fields

        private readonly string _glyph;

        #endregion

        #region Constructors

        public PetsciiGlyphElement(string glyph) : base(1, 1)
        {
            _glyph = glyph;
        }

        #endregion

        #region Public Methods

        public override System.Windows.Media.TextFormatting.TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            return new System.Windows.Media.TextFormatting.TextCharacters(_glyph, TextRunProperties);
        }

        #endregion
    }
}
