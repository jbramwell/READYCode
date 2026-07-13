// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ReadyCode.Tokenizer;

namespace ReadyCode.Editor;

/// <summary>
/// Colors unquoted, non-numeric values inside a DATA statement's comma-separated argument list
/// as string literals (e.g. the "MEMORYERROR" in "DATA MEMORYERROR,NOSUCHCOMMAND"), since BASIC
/// treats any DATA value that isn't purely numeric as a string even without quotes. Quoted
/// values are left to <see cref="StringLiteralColorizer"/> and purely numeric ones are left to
/// <see cref="NumberLiteralColorizer"/>.
/// </summary>
public class DataLiteralColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Regex _numericValuePattern =
        new(@"^[+-]?\d+(\.\d+)?([Ee][+-]?\d+)?$", RegexOptions.Compiled);

    // Mirrors BasicKeywordColorizer's keyword list so this colorizer agrees with it on where
    // the DATA keyword (and other keywords) start and end.
    private static readonly string[] _keywords =
        BasicTokens.TokenMap.Keys
            .Where(k => char.IsLetter(k[0]))
            .OrderByDescending(k => k.Length)
            .ToArray();

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw unquoted string values in a DATA statement.
    /// </summary>
    public Brush StringBrush { get; set; } = Brushes.Orange;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colors the unquoted, non-numeric values of every DATA statement found on the given line.
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

            if (inString)
            {
                i++;
                continue;
            }

            if (char.IsLetter(c))
            {
                int kwLen = MatchKeywordLength(text, i);
                if (kwLen > 0)
                {
                    bool isData = kwLen == 4 &&
                        string.Compare(text, i, "DATA", 0, 4, StringComparison.OrdinalIgnoreCase) == 0;
                    i += kwLen;
                    if (isData)
                        i = ColorizeArguments(text, i, line);
                    continue;
                }

                i++;
                continue;
            }

            i++;
        }
    }

    #endregion

    #region Private Methods

    // Colors each unquoted, non-numeric, comma-separated value following a DATA keyword, up to
    // the next unquoted ':' (a new statement) or the end of the line. Returns the index just
    // after the last character consumed.
    private int ColorizeArguments(string text, int i, DocumentLine line)
    {
        int segmentStart = i;
        bool inString = false;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '"')
            {
                inString = !inString;
                i++;
                continue;
            }

            if (inString)
            {
                i++;
                continue;
            }

            if (c == ',')
            {
                ColorizeSegment(text, segmentStart, i, line);
                i++;
                segmentStart = i;
                continue;
            }

            if (c == ':')
            {
                ColorizeSegment(text, segmentStart, i, line);
                return i;
            }

            i++;
        }

        ColorizeSegment(text, segmentStart, i, line);
        return i;
    }

    // Colors one DATA value (the [segStart, segEnd) span, trimmed of surrounding spaces) as a
    // string literal, unless it's already quoted or is purely numeric.
    private void ColorizeSegment(string text, int segStart, int segEnd, DocumentLine line)
    {
        int s = segStart;
        while (s < segEnd && text[s] == ' ') s++;
        int e = segEnd;
        while (e > s && text[e - 1] == ' ') e--;

        if (e <= s) return;
        if (text[s] == '"') return;
        if (_numericValuePattern.IsMatch(text.Substring(s, e - s))) return;

        int start = line.Offset + s;
        int end   = line.Offset + e;
        ChangeLinePart(start, end, el => el.TextRunProperties.SetForegroundBrush(StringBrush));
    }

    // Returns the length of the longest keyword matching at position i, or 0 if none match.
    private static int MatchKeywordLength(string text, int i)
    {
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

            if (isMatch) return kwLen;
        }

        return 0;
    }

    #endregion
}
