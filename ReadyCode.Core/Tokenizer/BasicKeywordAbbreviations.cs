// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Tokenizer;

/// <summary>
/// Maps each BASIC keyword's C64 keyboard shift-abbreviation - an unshifted letter prefix
/// followed by one shifted letter, exactly as the CRUNCH routine recognizes it while typing - to
/// the keyword it stands for. An entry's document form always has its prefix in uppercase
/// (unshifted keypresses render as capital letters in the default C64 charset) and its final
/// letter in lowercase (a shifted keypress is a PETSCII graphic whose byte value equals the
/// lowercase ASCII code of that letter - see <see cref="PetsciiScreenCodeMap"/>). Keys are
/// therefore case-sensitive: the case encodes which letter was shifted.
/// </summary>
public static class BasicKeywordAbbreviations
{
    #region Public Properties

    /// <summary>
    /// Maps each abbreviation's exact document text (e.g. "Li" for LIST) to the full keyword it
    /// represents.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ToKeyword = new Dictionary<string, string>
    {
        ["Ab"]  = "ABS",
        ["An"]  = "AND",
        ["As"]  = "ASC",
        ["At"]  = "ATN",
        ["Ch"]  = "CHR$",
        ["CLo"] = "CLOSE",
        ["Cl"]  = "CLR",
        ["Cm"]  = "CMD",
        ["Co"]  = "CONT",
        ["Da"]  = "DATA",
        ["De"]  = "DEF",
        ["Di"]  = "DIM",
        ["En"]  = "END",
        ["Ex"]  = "EXP",
        ["Fo"]  = "FOR",
        ["Fr"]  = "FRE",
        ["Ge"]  = "GET",
        ["GOs"] = "GOSUB",
        ["Go"]  = "GOTO",
        ["In"]  = "INPUT#",
        ["LEf"] = "LEFT$",
        ["Le"]  = "LET",
        ["Li"]  = "LIST",
        ["Lo"]  = "LOAD",
        ["Mi"]  = "MID$",
        ["Ne"]  = "NEXT",
        ["No"]  = "NOT",
        ["Op"]  = "OPEN",
        ["Pe"]  = "PEEK",
        ["Po"]  = "POKE",
        ["Pr"]  = "PRINT#",
        ["Re"]  = "READ",
        ["REs"] = "RESTORE",
        ["REt"] = "RETURN",
        ["Ri"]  = "RIGHT$",
        ["Rn"]  = "RND",
        ["Ru"]  = "RUN",
        ["Sa"]  = "SAVE",
        ["Sg"]  = "SGN",
        ["Si"]  = "SIN",
        ["Sp"]  = "SPC",
        ["Sq"]  = "SQR",
        ["STe"] = "STEP",
        ["St"]  = "STOP",
        ["STr"] = "STR$",
        ["Sy"]  = "SYS",
        ["Ta"]  = "TAB",
        ["Us"]  = "USR",
        ["Va"]  = "VAL",
        ["Ve"]  = "VERIFY",
        ["Wa"]  = "WAIT",
    };

    /// <summary>
    /// The longest abbreviation length, used to bound how far back a caller needs to look from
    /// the caret (or scan position) when checking whether text completes an abbreviation.
    /// </summary>
    public static readonly int MaxLength = ToKeyword.Keys.Max(k => k.Length);

    #endregion

    #region Public Methods

    /// <summary>
    /// Finds the longest match at <paramref name="position"/> in <paramref name="text"/>, trying
    /// both the full keyword spelling (case-insensitive, via <see cref="BasicTokens.TryMatchKeyword"/>)
    /// and any shift-abbreviation (case-sensitive, see <see cref="ToKeyword"/>) - mirroring the
    /// CRUNCH routine's greedy longest-match behavior. Shared by the tokenizer and the
    /// hover-tooltip keyword lookup so abbreviations resolve identically everywhere a line of
    /// BASIC is scanned for keywords.
    /// </summary>
    /// <param name="text">The line text to scan.</param>
    /// <param name="position">The position to match at.</param>
    /// <param name="keywordCandidates">The full-keyword candidate list to try (e.g. <see cref="BasicTokens.WordKeywordsLongestFirst"/> or <see cref="BasicTokens.AllKeywordsLongestFirst"/>).</param>
    /// <param name="keyword">The full keyword matched, or "" if none.</param>
    /// <param name="matchedLength">The number of characters consumed at <paramref name="position"/>.</param>
    public static bool TryMatchKeywordOrAbbreviation(string text, int position,
        IReadOnlyList<string> keywordCandidates, out string keyword, out int matchedLength)
    {
        keyword = "";
        matchedLength = 0;

        if (BasicTokens.TryMatchKeyword(text, position, keywordCandidates, out string fullKeyword))
        {
            keyword = fullKeyword;
            matchedLength = fullKeyword.Length;
        }

        for (int len = MaxLength; len > matchedLength; len--)
        {
            if (position + len > text.Length) continue;
            if (ToKeyword.TryGetValue(text.Substring(position, len), out string? abbrevKeyword))
            {
                keyword = abbrevKeyword;
                matchedLength = len;
                break;
            }
        }

        return matchedLength > 0;
    }

    #endregion
}
