// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Tokenizer;

/// <summary>
/// Result of tokenizing a single BASIC line.
/// </summary>
public class TokenizeLineResult
{
    #region Public Properties

    /// <summary>
    /// Gets or sets whether tokenization succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the tokenized bytes produced for the line.
    /// </summary>
    public byte[] Tokens { get; set; } = [];

    /// <summary>
    /// Gets or sets the error message describing why tokenization failed, or null if it succeeded.
    /// </summary>
    public string? ErrorMessage { get; set; }

    #endregion
}

/// <summary>
/// Tokenizes C64 BASIC source into PRG token format.
/// Uses greedy longest-first keyword scanning so minified code (e.g. FORI=1TO10) and
/// spaced code (FOR I=1 TO 10) both produce correctly tokenized keywords.
/// Whitespace runs outside strings are collapsed to a single 0x20 byte so that
/// non-minified transfers preserve source formatting when LISTed on the C64.
/// </summary>
public class BasicTokenizer
{
    #region Private Fields

    private static readonly byte _remToken = BasicTokens.TokenMap["REM"];

    #endregion

    #region Public Methods

    /// <summary>
    /// Tokenizes a single BASIC line (without the line number prefix).
    /// </summary>
    public TokenizeLineResult TokenizeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new TokenizeLineResult { Success = true, Tokens = [] };

        try
        {
            var tokens = new List<byte>();
            int pos = 0;

            while (pos < line.Length)
            {
                // Whitespace: collapse consecutive runs to one space.
                // The C64 CRUNCH routine strips spaces, but we preserve one per run so
                // non-minified transfers keep source formatting visible when LISTed on the C64.
                if (char.IsWhiteSpace(line[pos]))
                {
                    if (tokens.Count > 0 && tokens[^1] != (byte)' ')
                        tokens.Add((byte)' ');
                    while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                        pos++;
                    continue;
                }

                // String literal: emit bytes verbatim until the closing quote.
                if (line[pos] == '"')
                {
                    tokens.Add((byte)'"');
                    pos++;
                    while (pos < line.Length && line[pos] != '"')
                        tokens.Add((byte)line[pos++]);
                    if (pos < line.Length) { tokens.Add((byte)'"'); pos++; }
                    continue;
                }

                // Greedy keyword scan — mirrors the C64 BASIC CRUNCH routine, including its
                // keyboard shift-abbreviations (e.g. "Li" for LIST) and PRINT's "?" synonym.
                // Try every keyword/abbreviation at the current position, keeping the longest match.
                if (TryMatchKeywordOrAbbreviation(line, pos, out string keyword, out int matchedLength))
                {
                    byte token = BasicTokens.TokenMap[keyword];
                    tokens.Add(token);
                    pos += matchedLength;

                    // TAB( and SPC( bake the opening paren into the token's own display text on
                    // real hardware, so a literal "(" here must be swallowed, not re-emitted, or
                    // LISTing the .prg on VICE/C64U shows a doubled paren.
                    if (BasicTokens.ParenInclusiveTokens.Contains(token) && pos < line.Length && line[pos] == '(')
                        pos++;

                    // After REM the rest of the line is a comment — copy verbatim.
                    if (token == _remToken)
                    {
                        // Preserve a single leading space if present
                        if (pos < line.Length && line[pos] == ' ')
                        {
                            tokens.Add((byte)' ');
                            pos++;
                        }
                        while (pos < line.Length)
                            tokens.Add((byte)line[pos++]);
                    }
                }
                else
                {
                    // Literal character (variable name letter, digit, punctuation …). Only
                    // ASCII letters get uppercased, so variable names survive the round-trip -
                    // raw PETSCII bytes above 0x7F (e.g. Pi, $FF) must pass through unchanged.
                    // char.ToUpperInvariant on those maps to an unrelated Unicode codepoint
                    // (e.g. $FF 'ÿ' -> U+0178 'Ÿ'), which truncates to a different, wrong byte
                    // once cast - for Pi specifically, that corrupts it into $78, the clubs
                    // card-suit graphic, instead of preserving Pi's own byte value.
                    char c = line[pos];
                    tokens.Add((byte)(c is >= 'a' and <= 'z' ? char.ToUpperInvariant(c) : c));
                    pos++;
                }
            }

            return new TokenizeLineResult { Success = true, Tokens = [..tokens] };
        }
        catch (Exception ex)
        {
            return new TokenizeLineResult
            {
                Success = false,
                ErrorMessage = $"Tokenization error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Tokenizes a complete BASIC program (splits on line breaks first).
    /// </summary>
    public List<TokenizeLineResult> TokenizeProgram(string sourceCode)
    {
        var results = new List<TokenizeLineResult>();
        foreach (var line in sourceCode.Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
            results.Add(TokenizeLine(line));
        return results;
    }

    #endregion

    #region Private Methods

    // Recognizes full keyword spellings, their C64 keyboard shift-abbreviations (see
    // BasicKeywordAbbreviations), and PRINT's "?" synonym at the given position.
    private static bool TryMatchKeywordOrAbbreviation(string line, int pos, out string keyword, out int matchedLength)
    {
        if (line[pos] == '?')
        {
            keyword = "PRINT";
            matchedLength = 1;
            return true;
        }

        return BasicKeywordAbbreviations.TryMatchKeywordOrAbbreviation(
            line, pos, BasicTokens.AllKeywordsLongestFirst, out keyword, out matchedLength);
    }

    #endregion
}
