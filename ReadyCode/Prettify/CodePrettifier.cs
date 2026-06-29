// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;
using ReadyCode.Minify;
using ReadyCode.Tokenizer;

namespace ReadyCode.Prettify;

/// <summary>
/// Applies readability-oriented transformations to C64 BASIC source code, such as keyword
/// spacing, standard numeric notation, explicit NEXT variables, and line renumbering.
/// </summary>
public static class CodePrettifier
{
    #region Private Fields

    // Keywords sorted longest-first for greedy matching; skip single-char math operators.
    private static readonly List<string> _spacedKeywords =
        BasicTokens.TokenMap.Keys
            .Where(k => !(k.Length == 1 && "+-*/^>=<".Contains(k[0])))
            .OrderByDescending(k => k.Length)
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToList();

    // Function/array keywords: don't add a trailing space (the '(' follows immediately).
    private static readonly HashSet<string> _functionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SGN", "INT", "ABS", "USR", "FRE", "POS", "SQR", "RND", "LOG", "EXP",
        "COS", "SIN", "TAN", "ATN", "PEEK", "LEN", "STR$", "VAL", "ASC",
        "CHR$", "LEFT$", "RIGHT$", "MID$", "FN", "TAB(", "SPC(",
        // File-I/O: no space between keyword and file number
        "PRINT#", "INPUT#", "GET#",
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// Applies the requested prettification passes in a fixed order.
    /// </summary>
    public static string Prettify(string source,
        bool addWhitespace,
        bool replacePeriodWithZero,
        bool useStandardNotation,
        bool addNextVariables,
        bool renumberLines,
        int lineNumberIncrement = 10,
        int lineNumberPadding   = 0)
    {
        // Structural passes first so later text passes see the updated code
        if (addNextVariables)      source = AddNextVariables(source);
        if (replacePeriodWithZero) source = ReplacePeriodWithZero(source);
        if (useStandardNotation)   source = UseStandardNotation(source);
        if (renumberLines)         source = RenumberLines(source, lineNumberIncrement, lineNumberIncrement, lineNumberPadding);
        // Whitespace pass last — all other passes already emit "linenum space code"
        if (addWhitespace)         source = AddWhitespace(source);
        return source;
    }

    /// <summary>
    /// Inserts spaces around BASIC keywords throughout the source, leaving string literals untouched.
    /// </summary>
    public static string AddWhitespace(string source)
    {
        var result = new List<string>();
        foreach (var line in SplitLines(source))
        {
            // Preserve the exact digits of the line number (including leading zeros from
            // RenumberLines padding). SplitBasicLine strips leading zeros, so we parse manually.
            int j = 0;
            while (j < line.Length && line[j] == ' ') j++;
            if (j >= line.Length || !char.IsDigit(line[j])) { result.Add(line); continue; }
            int numStart = j;
            while (j < line.Length && char.IsDigit(line[j])) j++;
            string rawLineNum = line[numStart..j];
            while (j < line.Length && line[j] == ' ') j++;
            string code = j < line.Length ? line[j..] : string.Empty;

            result.Add(rawLineNum + " " + SpaceKeywords(code));
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Replaces a leading bare period (e.g. ".5") with an explicit leading zero ("0.5")
    /// outside string literals.
    /// </summary>
    public static string ReplacePeriodWithZero(string source)
    {
        var result = new List<string>();
        foreach (var line in SplitLines(source))
        {
            var (lineNum, code) = CodeMinifier.SplitBasicLine(line);
            if (lineNum == null) { result.Add(line); continue; }
            string transformed = TransformOutsideStrings(code,
                s => Regex.Replace(s, @"(?<![0-9])\.(\d)", "0.$1"));
            result.Add(lineNum + " " + transformed);
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Expands scientific (E) notation integer literals back into their full decimal form
    /// outside string literals.
    /// </summary>
    public static string UseStandardNotation(string source)
    {
        var result = new List<string>();
        foreach (var line in SplitLines(source))
        {
            var (lineNum, code) = CodeMinifier.SplitBasicLine(line);
            if (lineNum == null) { result.Add(line); continue; }
            string transformed = TransformOutsideStrings(code, ExpandENotation);
            result.Add(lineNum + " " + transformed);
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Adds the matching FOR loop variable to bare NEXT statements
    /// (e.g. "NEXT" inside a "FOR I" loop becomes "NEXT I").
    /// </summary>
    public static string AddNextVariables(string source)
    {
        var forStack = new Stack<string>();
        var result   = new List<string>();

        foreach (var line in SplitLines(source))
        {
            var (lineNum, code) = CodeMinifier.SplitBasicLine(line);
            if (lineNum == null) { result.Add(line); continue; }

            string updatedCode = ProcessStatements(code, forStack);
            result.Add(lineNum + " " + updatedCode);
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Renumbers all BASIC line numbers starting at <paramref name="start"/> in steps of
    /// <paramref name="increment"/>, optionally zero-padded to <paramref name="padding"/> digits,
    /// updating any line-number references to match.
    /// </summary>
    public static string RenumberLines(string source, int start, int increment, int padding)
    {
        var numbered = new List<(int oldNum, string code)>();
        foreach (var line in SplitLines(source))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var (lineNumStr, code) = CodeMinifier.SplitBasicLine(line);
            if (lineNumStr != null && int.TryParse(lineNumStr, out int n))
                numbered.Add((n, code));
        }

        var mapping = new Dictionary<int, int>(numbered.Count);
        for (int i = 0; i < numbered.Count; i++)
            mapping[numbered[i].oldNum] = start + i * increment;

        var result = new List<string>(numbered.Count);
        foreach (var (oldNum, code) in numbered)
        {
            int newNum    = mapping[oldNum];
            string numStr = padding > 0 ? newNum.ToString().PadLeft(padding, '0') : newNum.ToString();
            string updated = UpdateLineReferences(code, mapping);
            result.Add($"{numStr} {updated}");
        }
        return JoinLines(result);
    }

    #endregion

    #region Private Methods

    // Insert spaces around C64 BASIC keywords while leaving string literals untouched.
    private static string SpaceKeywords(string code)
    {
        var sb           = new StringBuilder(code.Length + 16);
        int i            = 0;
        bool prevIsSpace = true; // treat line start as "preceded by space"

        while (i < code.Length)
        {
            char c = code[i];

            // ── String literal — copy verbatim ───────────────────────────────
            if (c == '"')
            {
                prevIsSpace = false;
                sb.Append(c); i++;
                while (i < code.Length && code[i] != '"') { sb.Append(code[i++]); }
                if (i < code.Length) { sb.Append(code[i++]); }
                continue;
            }

            // ── Statement separator ':' — resets to "start of statement" ────
            if (c == ':')
            {
                sb.Append(c); i++;
                prevIsSpace = true;
                continue;
            }

            // ── Existing space — normalise to single space ───────────────────
            if (c == ' ')
            {
                if (!prevIsSpace) sb.Append(' ');
                prevIsSpace = true;
                i++;
                continue;
            }

            // ── Try to match a keyword (longest first) ───────────────────────
            string? kw = null;
            foreach (var candidate in _spacedKeywords)
            {
                if (i + candidate.Length <= code.Length &&
                    code.AsSpan(i, candidate.Length).Equals(
                        candidate.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    kw = candidate;
                    break;
                }
            }

            if (kw != null)
            {
                // Space before keyword (unless already at statement start)
                if (!prevIsSpace) sb.Append(' ');

                sb.Append(kw.ToUpperInvariant());
                i += kw.Length;
                prevIsSpace = false;

                // REM: copy rest of line verbatim (it's a comment)
                if (kw.Equals("REM", StringComparison.OrdinalIgnoreCase))
                {
                    if (i < code.Length && code[i] != ' ') sb.Append(' ');
                    while (i < code.Length) sb.Append(code[i++]);
                    break;
                }

                // DATA: emit values compactly — no spaces around commas.
                // In C64 BASIC, DATA extends to end of the logical line.
                // String literals inside DATA are preserved verbatim (spaces included).
                if (kw.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    while (i < code.Length && code[i] == ' ') i++; // skip leading spaces
                    if (i < code.Length) sb.Append(' ');            // one space before values
                    while (i < code.Length)
                    {
                        if (code[i] == ' ') { i++; continue; }     // strip spaces around commas
                        if (code[i] == '"')
                        {
                            sb.Append('"'); i++;
                            while (i < code.Length && code[i] != '"') sb.Append(code[i++]);
                            if (i < code.Length) { sb.Append('"'); i++; }
                        }
                        else { sb.Append(code[i++]); }
                    }
                    break;
                }

                // Space after keyword — skip for function-like keywords and
                // when followed by a separator that needs no space
                bool isFunc  = _functionKeywords.Contains(kw);
                char nextCh  = i < code.Length ? code[i] : '\0';
                bool needsSpace = !isFunc
                    && nextCh != '\0'
                    && nextCh != ':'
                    && nextCh != ';'
                    && nextCh != ','
                    && nextCh != '('
                    && nextCh != ' ';

                if (needsSpace) { sb.Append(' '); prevIsSpace = true; }
            }
            else
            {
                sb.Append(c);
                prevIsSpace = false;
                i++;
            }
        }

        return sb.ToString();
    }

    // Process FOR/NEXT tracking across the statements of a single source line.
    private static string ProcessStatements(string code, Stack<string> forStack)
    {
        var statements = SplitStatements(code);
        var result     = new List<string>(statements.Count);

        foreach (var stmt in statements)
        {
            string trimmed = stmt.TrimStart();
            int    indent  = stmt.Length - trimmed.Length;
            string prefix  = stmt[..indent];

            // FOR var = ...  →  push variable (handles both "FOR I=" and "FORI=")
            var forMatch = Regex.Match(trimmed, @"^FOR\s*([A-Z][A-Z0-9$]?)\s*=", RegexOptions.IgnoreCase);
            if (forMatch.Success)
            {
                forStack.Push(forMatch.Groups[1].Value.ToUpperInvariant());
                result.Add(stmt);
                continue;
            }

            // NEXT with no variable  →  restore top-of-stack variable
            if (Regex.IsMatch(trimmed, @"^NEXT\s*$", RegexOptions.IgnoreCase))
            {
                result.Add(forStack.Count > 0
                    ? prefix + "NEXT " + forStack.Pop()
                    : stmt);
                continue;
            }

            // NEXT var[,var...]  →  pop one entry per variable already present (handles "NEXT I" and "NEXTI")
            var nextVarMatch = Regex.Match(trimmed,
                @"^NEXT\s*(?:[A-Z][A-Z0-9$]*\s*,\s*)*[A-Z][A-Z0-9$]*", RegexOptions.IgnoreCase);
            if (nextVarMatch.Success)
            {
                int varCount = trimmed[4..].Split(',').Length; // everything after "NEXT"
                for (int i = 0; i < varCount && forStack.Count > 0; i++) forStack.Pop();
                result.Add(stmt);
                continue;
            }

            result.Add(stmt);
        }

        return string.Join(":", result);
    }

    // Split code on ':' while respecting string literals.
    private static List<string> SplitStatements(string code)
    {
        var statements = new List<string>();
        var current    = new StringBuilder();
        bool inString  = false;

        foreach (char c in code)
        {
            if (c == '"')  { inString = !inString; current.Append(c); }
            else if (!inString && c == ':') { statements.Add(current.ToString()); current.Clear(); }
            else           { current.Append(c); }
        }
        statements.Add(current.ToString());
        return statements;
    }

    private static string ExpandENotation(string segment)
    {
        return Regex.Replace(segment, @"(?<![A-Z0-9\.])(\d+(?:\.\d+)?)E(\d+)(?![A-Z0-9])", m =>
        {
            if (!double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double mantissa))
                return m.Value;
            if (!int.TryParse(m.Groups[2].Value, out int exponent)) return m.Value;

            double value = mantissa * Math.Pow(10, exponent);
            // Only expand if result is a clean integer in safe range
            if (value != Math.Floor(value) || value > 1e15 || value < 0) return m.Value;

            return ((long)value).ToString();
        }, RegexOptions.IgnoreCase);
    }

    private static string TransformOutsideStrings(string code, Func<string, string> transform)
    {
        var sb = new StringBuilder(code.Length);
        int i  = 0;
        while (i < code.Length)
        {
            if (code[i] == '"')
            {
                int start = i++;
                while (i < code.Length && code[i] != '"') i++;
                if (i < code.Length) i++;
                sb.Append(code[start..i]);
            }
            else
            {
                int start = i;
                while (i < code.Length && code[i] != '"') i++;
                sb.Append(transform(code[start..i]));
            }
        }
        return sb.ToString();
    }

    private static string UpdateLineReferences(string code, Dictionary<int, int> mapping)
    {
        // No \b anchor: in minified code keywords like GOTO appear with no preceding
        // space (e.g. "SGOTO24"), so a word boundary would silently skip them.
        return Regex.Replace(code,
            @"(GOTO|GOSUB|THEN|RESTORE|RUN)\s*(\d+(?:\s*,\s*\d+)*)",
            m =>
            {
                string keyword = m.Groups[1].Value;
                string nums    = Regex.Replace(m.Groups[2].Value, @"\d+", n =>
                {
                    if (int.TryParse(n.Value, out int old) && mapping.TryGetValue(old, out int @new))
                        return @new.ToString(); // references are never zero-padded
                    return n.Value;
                });
                return keyword + " " + nums;
            },
            RegexOptions.IgnoreCase);
    }

    private static List<string> SplitLines(string source) =>
        [.. source.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)];

    private static string JoinLines(List<string> lines) => string.Join("\n", lines);

    #endregion
}
