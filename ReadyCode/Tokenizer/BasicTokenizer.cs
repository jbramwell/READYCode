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

    // Keywords sorted longest-first so that PRINT# is tried before PRINT, etc.
    private static readonly (string Keyword, byte Token)[] _sortedKeywords =
        BasicTokens.TokenMap
            .OrderByDescending(kv => kv.Key.Length)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value))
            .ToArray();

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

                // Greedy keyword scan — mirrors the C64 BASIC CRUNCH routine.
                // Try every keyword at the current position, keeping the longest match.
                bool matched = false;
                foreach (var (keyword, token) in _sortedKeywords)
                {
                    if (pos + keyword.Length > line.Length) continue;
                    if (!line.AsSpan(pos, keyword.Length)
                              .Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    tokens.Add(token);
                    pos += keyword.Length;
                    matched = true;

                    // After REM the rest of the line is a comment — copy verbatim.
                    if (token == _remToken)
                    {
                        // Preserve a single leading space if present
                        if (pos < line.Length && line[pos] == ' ')
                            tokens.Add((byte)' ');
                        while (pos < line.Length)
                            tokens.Add((byte)line[pos++]);
                    }
                    break;
                }

                if (!matched)
                {
                    // Literal character (variable name letter, digit, punctuation …).
                    // Uppercase so variable names survive the round-trip.
                    tokens.Add((byte)char.ToUpperInvariant(line[pos]));
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
}
