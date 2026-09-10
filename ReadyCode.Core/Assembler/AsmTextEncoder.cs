// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;

namespace ReadyCode.Assembler;

/// <summary>
/// Converts a single source character to the byte a ".byte"/".text" string literal should emit
/// for it, per the active <see cref="AsmTextEncoding"/> - see AsmLineParser's ".encoding" handling.
/// </summary>
public static class AsmTextEncoder
{
    #region Public Methods

    /// <summary>
    /// Encodes a single character under the given text encoding.
    /// </summary>
    /// <param name="c">The source character.</param>
    /// <param name="encoding">The active encoding.</param>
    /// <returns>The byte to emit for this character.</returns>
    public static byte Encode(char c, AsmTextEncoding encoding)
    {
        byte petscii = encoding switch
        {
            AsmTextEncoding.PetsciiUpper or AsmTextEncoding.ScreenCodeUpper => ToPetsciiUpper(c),
            AsmTextEncoding.PetsciiMixed or AsmTextEncoding.ScreenCodeMixed => ToPetsciiMixed(c),
            _ => (byte)c,
        };

        return encoding is AsmTextEncoding.ScreenCodeUpper or AsmTextEncoding.ScreenCodeMixed
            ? PetsciiScreenCodeMap.ToScreenCode(petscii)
            : petscii;
    }

    #endregion

    #region Private Methods

    private static byte ToPetsciiUpper(char c) => char.IsAsciiLetter(c) ? (byte)char.ToUpperInvariant(c) : (byte)c;

    private static byte ToPetsciiMixed(char c)
    {
        if (!char.IsAsciiLetter(c)) return (byte)c;

        byte upper = (byte)char.ToUpperInvariant(c);
        return char.IsUpper(c) ? (byte)(upper + 0x80) : upper;
    }

    #endregion
}
