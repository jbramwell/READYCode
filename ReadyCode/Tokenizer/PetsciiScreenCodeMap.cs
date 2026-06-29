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

    #endregion

    #region Public Methods

    /// <summary>
    /// Converts a PETSCII byte value to its corresponding C64 character ROM screen code.
    /// </summary>
    /// <param name="petscii">The PETSCII byte value to convert.</param>
    /// <returns>The screen code used to look up the glyph for this byte.</returns>
    public static byte ToScreenCode(byte petscii) => _toScreenCodeTable[petscii];

    #endregion

    #region Private Methods

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
