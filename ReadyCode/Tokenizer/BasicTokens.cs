// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Tokenizer;

/// <summary>
/// Commodore 64 BASIC token definitions.
/// Maps keywords to their token values (0x80-0xFF).
/// </summary>
public static class BasicTokens
{
    #region Public Properties

    /// <summary>
    /// Dictionary of BASIC keywords to their token values.
    /// </summary>
    public static readonly Dictionary<string, byte> TokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Control Flow
        { "END", 0x80 },
        { "FOR", 0x81 },
        { "NEXT", 0x82 },
        { "DATA", 0x83 },
        { "INPUT#", 0x84 },
        { "INPUT", 0x85 },
        { "DIM", 0x86 },
        { "READ", 0x87 },
        { "LET", 0x88 },
        { "GOTO", 0x89 },
        { "RUN", 0x8A },
        { "IF", 0x8B },
        { "RESTORE", 0x8C },
        { "GOSUB", 0x8D },
        { "RETURN", 0x8E },
        { "REM", 0x8F },
        { "STOP", 0x90 },

        // Functions & I/O
        { "ON", 0x91 },
        { "WAIT", 0x92 },
        { "LOAD", 0x93 },
        { "SAVE", 0x94 },
        { "VERIFY", 0x95 },
        { "DEF", 0x96 },
        { "POKE", 0x97 },
        { "PRINT#", 0x98 },
        { "PRINT", 0x99 },
        { "CONT", 0x9A },
        { "LIST", 0x9B },
        { "CLR", 0x9C },
        { "CMD", 0x9D },
        { "SYS", 0x9E },
        { "OPEN", 0x9F },
        { "CLOSE", 0xA0 },
        { "GET", 0xA1 },
        { "NEW", 0xA2 },

        // More Keywords
        { "TAB", 0xA3 },
        { "TO", 0xA4 },
        { "FN", 0xA5 },
        { "SPC", 0xA6 },
        { "THEN", 0xA7 },
        { "NOT", 0xA8 },
        { "STEP", 0xA9 },

        // Operators
        { "+", 0xAA },
        { "-", 0xAB },
        { "*", 0xAC },
        { "/", 0xAD },
        { "^", 0xAE },
        { "AND", 0xAF },
        { "OR", 0xB0 },
        { ">", 0xB1 },
        { "=", 0xB2 },
        { "<", 0xB3 },

        // Math/String Functions
        { "SGN", 0xB4 },
        { "INT", 0xB5 },
        { "ABS", 0xB6 },
        { "USR", 0xB7 },
        { "FRE", 0xB8 },
        { "POS", 0xB9 },
        { "SQR", 0xBA },
        { "RND", 0xBB },
        { "LOG", 0xBC },
        { "EXP", 0xBD },
        { "COS", 0xBE },
        { "SIN", 0xBF },
        { "TAN", 0xC0 },
        { "ATN", 0xC1 },
        { "PEEK", 0xC2 },
        { "LEN", 0xC3 },
        { "STR$", 0xC4 },
        { "VAL", 0xC5 },
        { "ASC", 0xC6 },
        { "CHR$", 0xC7 },
        { "LEFT$", 0xC8 },
        { "RIGHT$", 0xC9 },
        { "MID$", 0xCA },
        { "GO", 0xCB },
    };

    /// <summary>
    /// Reverse map for converting token bytes back to keywords.
    /// </summary>
    public static readonly Dictionary<byte, string> ReverseTokenMap =
        TokenMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    #endregion

    #region Public Methods

    /// <summary>
    /// Checks whether a word is a recognized BASIC keyword token.
    /// </summary>
    public static bool IsToken(string word) => TokenMap.ContainsKey(word);

    /// <summary>
    /// Gets the token value for a keyword.
    /// </summary>
    public static bool TryGetToken(string word, out byte token) =>
        TokenMap.TryGetValue(word, out token);

    #endregion
}
