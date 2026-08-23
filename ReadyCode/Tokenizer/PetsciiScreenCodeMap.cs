// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Tokenizer;

/// <summary>
/// Converts PETSCII byte values to the C64 character ROM screen codes used to look up glyphs,
/// following the standard conversion table (https://sta.c64.org/cbm64pettoscr.html). This is the
/// same conversion the KERNAL applies when sending a byte to the screen, which is why control
/// codes such as CHR$(147) (CLR/HOME) display as the familiar reverse-video heart in listings.
/// </summary>
public static class PetsciiScreenCodeMap
{
    #region Private Fields

    private static readonly byte[] _toScreenCodeTable = BuildTable();
    private static readonly byte[] _toPetsciiTable = BuildInverseTable();

    #endregion

    #region Public Methods

    /// <summary>
    /// Converts a PETSCII byte value to its corresponding C64 character ROM screen code.
    /// </summary>
    /// <param name="petscii">The PETSCII byte value to convert.</param>
    /// <returns>The screen code used to look up the glyph for this byte.</returns>
    public static byte ToScreenCode(byte petscii) => _toScreenCodeTable[petscii];

    /// <summary>
    /// Converts a C64 character ROM screen code back to a PETSCII byte value - the inverse of
    /// <see cref="ToScreenCode"/>. The underlying table has genuine many-to-one aliasing (an
    /// authentic property of the real C64 charset - several PETSCII byte values render as the
    /// same glyph), so this picks one canonical PETSCII byte per screen code rather than
    /// guaranteeing a byte-perfect round trip for every possible input; it IS exact for the
    /// control-code ranges callers care about in practice (<c>0x00-0x1F</c>, <c>0x80-0x9F</c>),
    /// since those don't collide with anything else in the table.
    /// </summary>
    /// <param name="screenCode">The C64 character ROM screen code to convert.</param>
    /// <returns>A PETSCII byte value that renders as this screen code's glyph.</returns>
    public static byte ToPetscii(byte screenCode) => _toPetsciiTable[screenCode];

    /// <summary>
    /// Determines whether a PETSCII byte needs its glyph substituted for correct display - true
    /// for control codes and the ranges where PETSCII's graphics diverge from ASCII, even though
    /// some of those code points fall inside the otherwise-identical printable ASCII range.
    /// </summary>
    /// <param name="petscii">The PETSCII byte value to check.</param>
    public static bool NeedsGlyphSubstitution(byte petscii) =>
        petscii < 0x20 || petscii > 0x7E || petscii == 0x5C || petscii >= 0x5E;

    /// <summary>
    /// Converts a string of raw PETSCII byte values (one per <see cref="char"/>, as read live from
    /// C64 memory) into its Private-Use-Area display form - substituting any byte
    /// <see cref="NeedsGlyphSubstitution"/> flags with <c>U+E000 + screenCode</c>, the same
    /// convention <c>ReadyCode.Editor.PetsciiGlyphGenerator</c> uses to render the code editor, so
    /// a plain control using the "Pet Me 64" font displays it identically. Printable ASCII bytes
    /// pass through unchanged. The inverse of <see cref="FromDisplayText"/>.
    /// </summary>
    /// <param name="raw">Raw PETSCII bytes, one per <see cref="char"/> (0-255).</param>
    public static string ToDisplayText(string raw)
    {
        var chars = new char[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            chars[i] = c <= 0xFF && NeedsGlyphSubstitution((byte)c)
                ? (char)(0xE000 + ToScreenCode((byte)c))
                : c;
        }
        return new string(chars);
    }

    /// <summary>
    /// Reverses <see cref="ToDisplayText"/>: converts PUA-substituted display text back to raw
    /// PETSCII bytes-as-chars, for encoding a live variable edit back to memory. A no-op on any
    /// string with no PUA characters in it, so it's safe to apply unconditionally to plain text.
    /// </summary>
    /// <param name="display">Display text, possibly containing <c>U+E000-U+E0FF</c> glyph chars.</param>
    public static string FromDisplayText(string display)
    {
        var chars = new char[display.Length];
        for (int i = 0; i < display.Length; i++)
        {
            char c = display[i];
            chars[i] = c is >= (char)0xE000 and <= (char)0xE0FF
                ? (char)ToPetscii((byte)(c - 0xE000))
                : c;
        }
        return new string(chars);
    }

    #endregion

    #region Private Methods

    private static byte[] BuildInverseTable()
    {
        var inverse = new byte[256];
        for (int petscii = 0; petscii <= 255; petscii++)
            inverse[_toScreenCodeTable[petscii]] = (byte)petscii;
        return inverse;
    }

    private static byte[] BuildTable()
    {
        var table = new byte[256];
        for (int petscii = 0; petscii <= 255; petscii++)
        {
            if (petscii == 0xFF)
            {
                table[petscii] = 0x5E;            // PETSCII $FF (pi) maps directly to screen code $5E
                continue;
            }

            int offset = petscii switch
            {
                <= 0x1F => +0x80,
                <= 0x3F => 0,
                <= 0x5F => -0x40,
                <= 0x7F => -0x20,
                <= 0x9F => +0x40,
                <= 0xBF => -0x40,
                _ => -0x80
            };

            table[petscii] = (byte)((petscii + offset) & 0xFF);
        }

        return table;
    }

    #endregion
}
