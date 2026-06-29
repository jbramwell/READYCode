// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace ReadyCode.Minify;

/// <summary>
/// Applies size-reduction transformations to C64 BASIC source code, such as whitespace removal,
/// comment stripping, numeric literal shortening, and line renumbering.
/// </summary>
public static class CodeMinifier
{
    #region Public Methods

    /// <summary>
    /// Applies the requested minification passes in a fixed order.
    /// </summary>
    public static string Minify(string source,
        bool removeWhitespace,
        bool replace0WithPeriod,
        bool useScientificNotation,
        bool removeComments,
        bool simplifyNextStatements,
        bool renumberLines)
    {
        // Remove comments first — fewer lines for subsequent passes to process.
        // Whitespace removal goes last so earlier passes can rely on normal spacing.
        if (removeComments)         source = RemoveComments(source);
        if (replace0WithPeriod)     source = Replace0WithPeriod(source);
        if (useScientificNotation)  source = UseScientificNotation(source);
        if (simplifyNextStatements) source = SimplifyNextStatements(source);
        // Renumber before whitespace removal so the regex patterns still see spaces.
        if (renumberLines)          source = RenumberLines(source);
        // Whitespace last — removes all spaces outside strings, including after line numbers.
        if (removeWhitespace)       source = RemoveWhitespace(source);
        return source;
    }

    /// <summary>
    /// Removes all whitespace outside string literals from each line, including the space
    /// between the line number and the first token.
    /// </summary>
    public static string RemoveWhitespace(string source)
    {
        var result = new List<string>();
        foreach (var line in SplitLines(source))
        {
            var (lineNum, code) = SplitBasicLine(line);
            if (lineNum == null) { result.Add(line); continue; }
            // Remove every space outside string literals. The space between the line
            // number and the first token is also removed — C64 BASIC does not require it.
            string compact = TransformOutsideStrings(code.Trim(), s => s.Replace(" ", ""));
            result.Add(lineNum + compact);
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Replaces a leading zero before a decimal point (e.g. "0.5") with a bare period (".5")
    /// outside string literals.
    /// </summary>
    public static string Replace0WithPeriod(string source)
    {
        var result = new List<string>();
        foreach (var line in SplitLines(source))
        {
            var (lineNum, code) = SplitBasicLine(line);
            if (lineNum == null) { result.Add(line); continue; }
            // "0.d" where 0 is not preceded by another digit → ".d"
            string transformed = TransformOutsideStrings(code,
                s => Regex.Replace(s, @"(?<!\d)0\.(\d)", ".$1"));
            result.Add(lineNum + " " + transformed);
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Shortens large integer literals to scientific (E) notation where doing so is strictly shorter.
    /// </summary>
    public static string UseScientificNotation(string source)
    {
        var result = new List<string>();
        foreach (var line in SplitLines(source))
        {
            var (lineNum, code) = SplitBasicLine(line);
            if (lineNum == null) { result.Add(line); continue; }
            string transformed = TransformOutsideStrings(code, ShortenIntegers);
            result.Add(lineNum + " " + transformed);
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Removes REM statements and trailing ": REM ..." comments, redirecting any GOTO/GOSUB/THEN
    /// references that pointed at a removed line to its next surviving line.
    /// </summary>
    public static string RemoveComments(string source)
    {
        var lines = SplitLines(source);

        // First pass: build a redirect map so that any GOTO/GOSUB/THEN pointing at a
        // removed REM line is updated to point at the next surviving line instead.
        var parsed = new List<(int num, string code)>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var (lineNumStr, code) = SplitBasicLine(line);
            if (lineNumStr != null && int.TryParse(lineNumStr, out int n))
                parsed.Add((n, code));
        }

        var redirectMap = new Dictionary<int, int>();
        var pendingNums = new List<int>(); // REM line numbers awaiting a redirect target
        foreach (var (num, code) in parsed)
        {
            if (IsRemStatement(code.TrimStart()))
            {
                pendingNums.Add(num);
            }
            else
            {
                foreach (int remNum in pendingNums)
                    redirectMap[remNum] = num;
                pendingNums.Clear();
            }
        }
        // Trailing REM lines have no successor — leave any references to them dangling
        // (they point into removed code, which is a logic error in the original source).

        // Second pass: emit surviving lines with references redirected.
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var (lineNum, code) = SplitBasicLine(line);
            if (lineNum == null) { result.Add(line); continue; }

            // Drop lines whose entire statement is REM
            if (IsRemStatement(code.TrimStart())) continue;

            // Strip ": REM ..." from the end of compound lines
            string stripped = StripInlineRem(code).TrimEnd();

            // Redirect any references that previously pointed at removed REM lines
            if (redirectMap.Count > 0)
                stripped = UpdateLineReferences(stripped, redirectMap);

            result.Add(lineNum + " " + stripped);
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Strips the variable name(s) from NEXT statements (e.g. "NEXT I" and "NEXT I,J,K" both become "NEXT").
    /// </summary>
    public static string SimplifyNextStatements(string source)
    {
        var result = new List<string>();
        foreach (var line in SplitLines(source))
        {
            var (lineNum, code) = SplitBasicLine(line);
            if (lineNum == null) { result.Add(line); continue; }
            // NEXT I  →  NEXT,   NEXT I,J,K  →  NEXT
            string simplified = TransformOutsideStrings(code,
                s => Regex.Replace(s,
                    @"\bNEXT\s+[A-Z][A-Z0-9$]*(?:\s*,\s*[A-Z][A-Z0-9$]*)*",
                    "NEXT",
                    RegexOptions.IgnoreCase));
            result.Add(lineNum + " " + simplified);
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Renumbers all BASIC line numbers sequentially starting at 1, updating any
    /// GOTO/GOSUB/THEN/RESTORE/RUN references to match.
    /// </summary>
    public static string RenumberLines(string source)
    {
        var numbered = new List<(int oldNum, string code)>();
        foreach (var line in SplitLines(source))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var (lineNumStr, code) = SplitBasicLine(line);
            if (lineNumStr != null && int.TryParse(lineNumStr, out int n))
                numbered.Add((n, code));
        }

        // Build old→new mapping; new numbers start at 1
        var mapping = new Dictionary<int, int>(numbered.Count);
        for (int i = 0; i < numbered.Count; i++)
            mapping[numbered[i].oldNum] = i + 1;

        var result = new List<string>(numbered.Count);
        foreach (var (oldNum, code) in numbered)
        {
            int newNum = mapping[oldNum];
            string updatedCode = UpdateLineReferences(code, mapping);
            result.Add($"{newNum} {updatedCode}");
        }
        return JoinLines(result);
    }

    /// <summary>
    /// Splits a source line into its BASIC line number and the remaining code, removing any
    /// leading zero-padding from the line number. Returns a null line number for lines that
    /// don't start with a number.
    /// </summary>
    public static (string? lineNum, string code) SplitBasicLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return (null, line);
        int i = 0;
        while (i < line.Length && line[i] == ' ') i++;
        if (i >= line.Length || !char.IsDigit(line[i])) return (null, line);
        int numStart = i;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        // Remove zero-padding; keep "0" if the line number is literally 0
        string lineNum = line[numStart..i].TrimStart('0');
        if (lineNum.Length == 0) lineNum = "0";
        while (i < line.Length && line[i] == ' ') i++;
        string code = i < line.Length ? line[i..] : string.Empty;
        return (lineNum, code);
    }

    #endregion

    #region Private Methods

    // Applies transform only to the segments of `code` that lie outside string literals.
    private static string TransformOutsideStrings(string code, Func<string, string> transform)
    {
        var sb = new StringBuilder(code.Length);
        int i = 0;
        while (i < code.Length)
        {
            if (code[i] == '"')
            {
                int start = i++;
                while (i < code.Length && code[i] != '"') i++;
                if (i < code.Length) i++; // closing quote
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

    private static bool IsRemStatement(string code)
    {
        if (!code.StartsWith("REM", StringComparison.OrdinalIgnoreCase)) return false;
        return code.Length == 3 || code[3] == ' ' || code[3] == '\t';
    }

    private static string StripInlineRem(string code)
    {
        bool inString = false;
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            if (c == '"') { inString = !inString; i++; continue; }
            if (!inString && c == ':')
            {
                int colonPos = i++;
                while (i < code.Length && code[i] == ' ') i++;
                // Check for REM (not part of a longer word)
                if (i + 3 <= code.Length &&
                    code[i..(i + 3)].Equals("REM", StringComparison.OrdinalIgnoreCase) &&
                    (i + 3 >= code.Length || !char.IsLetterOrDigit(code[i + 3])))
                    return code[..colonPos];
                continue;
            }
            i++;
        }
        return code;
    }

    // Replace integers ≥ 10 000 with E notation when that is strictly shorter.
    private static string ShortenIntegers(string segment)
    {
        return Regex.Replace(segment, @"(?<!\d)\d+(?!\d)", m =>
        {
            string original = m.Value;
            if (!long.TryParse(original, out long value)) return original;
            if (value < 10_000) return original;

            long temp = value;
            int zeros = 0;
            while (temp % 10 == 0) { temp /= 10; zeros++; }

            if (zeros == 0) return original; // no trailing zeros → E form not shorter

            string eForm = $"{temp}E{zeros}";
            return eForm.Length < original.Length ? eForm : original;
        });
    }

    private static string UpdateLineReferences(string code, Dictionary<int, int> mapping)
    {
        // No \b anchor: in minified code keywords appear without a preceding space
        // (e.g. "SGOTO24"), so a word boundary would silently skip them.
        return Regex.Replace(code,
            @"(GOTO|GOSUB|THEN|RESTORE|RUN)\s*(\d+(?:\s*,\s*\d+)*)",
            m =>
            {
                string keyword = m.Groups[1].Value;
                string nums = Regex.Replace(m.Groups[2].Value, @"\d+", n =>
                {
                    if (int.TryParse(n.Value, out int old) && mapping.TryGetValue(old, out int @new))
                        return @new.ToString();
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
