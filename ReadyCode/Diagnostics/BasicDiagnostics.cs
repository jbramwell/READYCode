// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using ReadyCode.Prettify;
using ReadyCode.Tokenizer;

namespace ReadyCode.Diagnostics;

/// <summary>
/// A single flagged problem in a source document (BASIC or assembly): an offset/length span
/// (matching AvalonEdit's document offset model) plus a human-readable message.
/// </summary>
/// <param name="Offset">The character offset into the analyzed source where the problem starts.</param>
/// <param name="Length">The number of characters the problem spans.</param>
/// <param name="Message">A human-readable description of the problem.</param>
public readonly record struct EditorDiagnostic(int Offset, int Length, string Message);

/// <summary>
/// Analyzes C64 BASIC source for common mistakes: undefined GOTO/GOSUB/THEN targets, unmatched
/// FOR/NEXT pairs, unterminated string literals, and duplicate line numbers.
/// </summary>
public static class BasicDiagnostics
{
    #region Private Fields

    // Internal (not private): reused as-is by BasicFoldingStrategy for FOR/NEXT fold detection.
    internal static readonly Regex _forRegex =
        new(@"^FOR\s*([A-Z][A-Z0-9$]?)\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static readonly Regex _bareNextRegex =
        new(@"^NEXT\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static readonly Regex _nextVarsRegex =
        new(@"^NEXT\s*(?:[A-Z][A-Z0-9$]*\s*,\s*)*[A-Z][A-Z0-9$]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches one NEXT variable name at a time (used to walk "NEXT X,Y,Z" left to right).
    private static readonly Regex _variableRegex =
        new(@"[A-Z][A-Z0-9$]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    #endregion

    #region Public Methods

    /// <summary>
    /// Analyzes the given BASIC source and returns every problem found, ordered by offset.
    /// </summary>
    /// <param name="source">The full BASIC source to analyze.</param>
    public static IReadOnlyList<EditorDiagnostic> Analyze(string source)
    {
        var diagnostics    = new List<EditorDiagnostic>();
        var declaredLines  = new Dictionary<int, List<(int Offset, int Length)>>();
        var pendingTargets = new List<(int Number, int Offset, int Length)>();
        var forStack       = new Stack<(string Variable, int Offset)>();
        var tokenizer      = new BasicTokenizer();

        foreach (var (line, lineOffset) in EnumerateLines(source))
            AnalyzeLine(line, lineOffset, tokenizer, declaredLines, pendingTargets, forStack, diagnostics);

        // Targets can be forward references, so they're only resolvable once every declared
        // line number on the whole document is known.
        foreach (var (number, offset, length) in pendingTargets)
        {
            if (!declaredLines.ContainsKey(number))
                diagnostics.Add(new EditorDiagnostic(offset, length, $"Line {number} does not exist."));
        }

        // Any FOR left on the stack after the whole document never found its NEXT.
        foreach (var (variable, offset) in forStack)
            diagnostics.Add(new EditorDiagnostic(offset, 3, $"FOR {variable} has no matching NEXT."));

        foreach (var (number, occurrences) in declaredLines)
        {
            if (occurrences.Count < 2) continue;
            foreach (var (offset, length) in occurrences)
                diagnostics.Add(new EditorDiagnostic(offset, length, $"Duplicate line number {number}."));
        }

        diagnostics.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return diagnostics;
    }

    /// <summary>
    /// Mirrors <see cref="ReadyCode.Minify.CodeMinifier.SplitBasicLine"/>'s leading-number parse,
    /// but keeps the raw offset/length (SplitBasicLine's zero-stripped string return can't place
    /// a squiggle - or a fold boundary - precisely). Reused by <c>BasicFoldingStrategy</c>.
    /// </summary>
    internal static bool TryParseLineNumber(string line, out int number, out int offset, out int length, out int codeStart)
    {
        number = 0; offset = 0; length = 0; codeStart = 0;

        int j = 0;
        while (j < line.Length && line[j] == ' ') j++;
        if (j >= line.Length || !char.IsDigit(line[j])) return false;

        int numStart = j;
        while (j < line.Length && char.IsDigit(line[j])) j++;
        if (!int.TryParse(line.AsSpan(numStart, j - numStart), out number)) return false;

        offset = numStart;
        length = j - numStart;

        while (j < line.Length && line[j] == ' ') j++;
        codeStart = j;
        return true;
    }

    /// <summary>
    /// Finds where a top-level (not inside a string) REM keyword starts in <paramref name="code"/>,
    /// or <c>code.Length</c> if none. Reused by <c>BasicFoldingStrategy</c>.
    /// </summary>
    internal static int FindTopLevelRemStart(string code)
    {
        bool inString = false;
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            if (c == '"') { inString = !inString; i++; continue; }
            if (inString) { i++; continue; }

            if (char.IsLetter(c))
            {
                if (BasicTokens.TryMatchKeyword(code, i, BasicTokens.WordKeywordsLongestFirst, out string keyword))
                {
                    if (string.Equals(keyword, "REM", StringComparison.OrdinalIgnoreCase)) return i;
                    i += keyword.Length;
                    continue;
                }
            }
            i++;
        }
        return code.Length;
    }

    /// <summary>
    /// Splits source into (lineText, absoluteOffset) pairs on \r\n, \r, or \n - unlike
    /// CodePrettifier's SplitLines/JoinLines, this preserves exact offsets. Reused by
    /// <c>VariableCrossReference</c>.
    /// </summary>
    internal static IEnumerable<(string Line, int Offset)> EnumerateLines(string source)
    {
        int pos = 0;
        while (true)
        {
            int i = pos;
            while (i < source.Length && source[i] != '\r' && source[i] != '\n') i++;
            yield return (source[pos..i], pos);

            if (i >= source.Length) yield break;
            i += source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n' ? 2 : 1;
            if (i > source.Length) yield break;
            pos = i;
        }
    }

    #endregion

    #region Private Methods

    private static void AnalyzeLine(
        string line, int lineOffset, BasicTokenizer tokenizer,
        Dictionary<int, List<(int Offset, int Length)>> declaredLines,
        List<(int Number, int Offset, int Length)> pendingTargets,
        Stack<(string Variable, int Offset)> forStack,
        List<EditorDiagnostic> diagnostics)
    {
        if (!TryParseLineNumber(line, out int lineNumber, out int numOffset, out int numLength, out int codeStart))
            return; // no leading line number - not a program line, nothing to analyze

        // PrgConverter.ParseLineNumberAndCode parses the line number into a ushort and silently
        // drops the whole line from the saved/deployed .prg when it doesn't fit - flag that here
        // too, with the same bound, so the live squiggle/Errors-tab view agrees with what
        // actually reaches the .prg instead of only finding out at save/deploy time.
        if (lineNumber > ushort.MaxValue)
            diagnostics.Add(new EditorDiagnostic(lineOffset + numOffset, numLength,
                $"Line number {lineNumber} is out of range (must be 0-65535)."));

        if (!declaredLines.TryGetValue(lineNumber, out var occurrences))
            declaredLines[lineNumber] = occurrences = new List<(int, int)>();
        occurrences.Add((lineOffset + numOffset, numLength));

        string code = line[codeStart..];
        int codeOffset = lineOffset + codeStart;

        // PrgConverter.ConvertToPrg also silently drops the whole line if BasicTokenizer can't
        // tokenize it (rare - the tokenizer is deliberately permissive - but not impossible).
        // Same reasoning as the line-number check above: surface it before Save/Deploy, not only
        // after.
        var tokenResult = tokenizer.TokenizeLine(code);
        if (!tokenResult.Success)
            diagnostics.Add(new EditorDiagnostic(codeOffset, Math.Max(code.Length, 1),
                tokenResult.ErrorMessage ?? "Tokenization failed."));

        // REM makes the rest of the physical line a comment - not split into statements, and
        // not scanned for GOTO/FOR/NEXT targets, matching CodePrettifier.SpaceKeywords' handling.
        string activeCode = code[..FindTopLevelRemStart(code)];

        int stmtOffset = codeOffset;
        foreach (var stmt in CodePrettifier.SplitStatements(activeCode))
        {
            AnalyzeForNext(stmt, stmtOffset, forStack, diagnostics);
            AnalyzeTargetsAndStrings(stmt, stmtOffset, pendingTargets, diagnostics);
            stmtOffset += stmt.Length + 1; // +1 for the ':' separator consumed between statements
        }
    }

    // Detects FOR/bare-NEXT/NEXT-with-vars in a single statement, pushing/popping forStack -
    // same regexes CodePrettifier.ProcessStatements uses to rewrite bare NEXTs, but here a stack
    // underflow (a NEXT with nothing to close) is flagged instead of silently left alone.
    private static void AnalyzeForNext(
        string stmt, int stmtOffset,
        Stack<(string Variable, int Offset)> forStack,
        List<EditorDiagnostic> diagnostics)
    {
        string trimmed       = stmt.TrimStart();
        int    trimmedOffset = stmtOffset + (stmt.Length - trimmed.Length);

        // A FOR/NEXT can start either at the very beginning of the statement, or immediately
        // after a "THEN" within it - "IF X THEN FOR I=1 TO 10" and "IF X THEN NEXT" are both
        // valid, legal BASIC, and the whole IF...THEN clause is one statement here since
        // SplitStatements only splits on colons (same reason AnalyzeTargetsAndStrings' own THEN
        // scan looks anywhere in the statement rather than just at its start). Without this, a
        // FOR embedded after THEN never gets pushed, and its matching NEXT then pops whatever
        // unrelated loop is actually on top of the stack instead.
        string candidate       = trimmed;
        int    candidateOffset = trimmedOffset;
        int    thenEnd         = FindThenEnd(trimmed);
        if (thenEnd >= 0)
        {
            string afterThen = trimmed[thenEnd..].TrimStart();
            if (afterThen.Length > 0)
            {
                candidate       = afterThen;
                candidateOffset = trimmedOffset + (trimmed.Length - afterThen.Length);
            }
        }

        var forMatch = _forRegex.Match(candidate);
        if (forMatch.Success)
        {
            forStack.Push((forMatch.Groups[1].Value.ToUpperInvariant(), candidateOffset));
            return;
        }

        if (_bareNextRegex.IsMatch(candidate))
        {
            if (forStack.Count > 0) forStack.Pop();
            else diagnostics.Add(new EditorDiagnostic(candidateOffset, 4, "NEXT without a matching FOR."));
            return;
        }

        if (_nextVarsRegex.IsMatch(candidate))
        {
            // NEXT var[,var...] must close its FOR loops in reverse order, same variable each
            // time (e.g. "FOR X ... NEXT Y" is a mistake, not just "some loop closed").
            foreach (Match varMatch in _variableRegex.Matches(candidate, 4))
            {
                string varName   = varMatch.Value.ToUpperInvariant();
                int    varOffset = candidateOffset + varMatch.Index;

                if (forStack.Count == 0)
                {
                    diagnostics.Add(new EditorDiagnostic(candidateOffset, 4, "NEXT without a matching FOR."));
                    break;
                }

                var (forVariable, _) = forStack.Pop();
                if (!string.Equals(forVariable, varName, StringComparison.OrdinalIgnoreCase))
                    diagnostics.Add(new EditorDiagnostic(varOffset, varMatch.Length,
                        $"NEXT {varName} does not match FOR {forVariable}."));
            }
        }
    }

    // Finds the offset just past a top-level (not inside a string) "THEN" keyword in `text`, or
    // -1 if none exists. A statement has at most one THEN (an IF's own), so the first match found
    // while skipping string contents is the right one. Same keyword-boundary + inString-toggle
    // scan as AnalyzeTargetsAndStrings below, kept separate since that method needs to keep
    // scanning the rest of the statement afterward and this only needs THEN's own end position.
    // Internal (not private): reused by BasicFoldingStrategy for the same "FOR/NEXT embedded
    // after THEN" reason _forRegex/_bareNextRegex/_nextVarsRegex already are.
    internal static int FindThenEnd(string text)
    {
        bool inString = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"') { inString = !inString; i++; continue; }
            if (inString) { i++; continue; }

            if (char.IsLetter(c))
            {
                if (BasicTokens.TryMatchKeyword(text, i, BasicTokens.WordKeywordsLongestFirst, out string keyword))
                {
                    if (string.Equals(keyword, "THEN", StringComparison.OrdinalIgnoreCase))
                        return i + keyword.Length;
                    i += keyword.Length;
                    continue;
                }
            }
            i++;
        }
        return -1;
    }

    // Scans a statement (already REM-truncated by the caller) for GOTO/GOSUB/THEN targets and an
    // unterminated string literal - reuses the same keyword-boundary + inString-toggle scan as
    // MainWindow.TryGetGotoTarget, but collects every target on the statement instead of just the
    // one under a given caret column.
    private static void AnalyzeTargetsAndStrings(
        string stmt, int stmtOffset,
        List<(int Number, int Offset, int Length)> pendingTargets,
        List<EditorDiagnostic> diagnostics)
    {
        bool inString  = false;
        int  quoteStart = -1;
        int  i = 0;

        while (i < stmt.Length)
        {
            char c = stmt[i];

            if (c == '"')
            {
                if (!inString) quoteStart = i;
                inString = !inString;
                i++;
                continue;
            }
            if (inString) { i++; continue; }

            if (char.IsLetter(c))
            {
                if (!BasicTokens.TryMatchKeyword(stmt, i, BasicTokens.WordKeywordsLongestFirst, out string keyword))
                { i++; continue; }

                bool isThen =
                    string.Equals(keyword, "THEN",  StringComparison.OrdinalIgnoreCase);
                bool isTarget = isThen ||
                    string.Equals(keyword, "GOTO",  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(keyword, "GOSUB", StringComparison.OrdinalIgnoreCase);

                int keywordStart = i;
                i += keyword.Length;
                if (!isTarget) continue;

                bool foundAnyTarget = false;
                while (true)
                {
                    while (i < stmt.Length && stmt[i] == ' ') i++;
                    int numStart = i;
                    while (i < stmt.Length && char.IsDigit(stmt[i])) i++;
                    if (i == numStart) break; // not followed by a number - no target list here
                    foundAnyTarget = true;

                    if (int.TryParse(stmt.AsSpan(numStart, i - numStart), out int target))
                        pendingTargets.Add((target, stmtOffset + numStart, i - numStart));

                    while (i < stmt.Length && stmt[i] == ' ') i++;
                    if (i < stmt.Length && stmt[i] == ',') { i++; continue; }
                    break;
                }

                // GOTO/GOSUB always need a numeric target - real hardware doesn't support a
                // computed jump. THEN is the one exception: it may instead be followed by an
                // ordinary statement (a keyword, e.g. "THEN PRINT...", or a variable name for
                // implicit LET, e.g. "THEN X=1") rather than a line number, so it's only an
                // error there if what follows isn't even the start of a valid statement (a
                // string literal, bare unassigned text, stray punctuation, or nothing at all
                // before end of line).
                if (!foundAnyTarget)
                {
                    // The loop above broke on its very first iteration (no target found), so i
                    // is still sitting exactly where it left it: right after the keyword and any
                    // following spaces, having advanced past zero digits.
                    bool validThenStatement = isThen && i < stmt.Length && IsValidThenConsequent(stmt, i);

                    if (!validThenStatement)
                    {
                        string expected = isThen ? "a line number or a statement" : "a line number";
                        diagnostics.Add(new EditorDiagnostic(stmtOffset + keywordStart, keyword.Length,
                            $"{keyword} must be followed by {expected}."));
                    }
                }
                continue;
            }

            i++;
        }

        if (inString)
            diagnostics.Add(new EditorDiagnostic(stmtOffset + quoteStart, stmt.Length - quoteStart, "Unterminated string literal."));
    }

    // Determines whether stmt[pos..] looks like the start of a genuine BASIC statement, for
    // classifying "THEN <this>" - either a recognized keyword (e.g. "PRINT", "GOTO"), or an
    // implicit LET (e.g. "X=1", "A$(1)=5"). Deliberately doesn't fully parse the assignment
    // target (which could be an arbitrarily complex array-subscript expression) - just checks
    // that the text starts with a letter (every valid target does) and that an '=' appears
    // somewhere later in the statement, which bare unassigned text (e.g. "HA HA HA", a common
    // way missing quotes around a string end up looking) never has.
    private static bool IsValidThenConsequent(string stmt, int pos)
    {
        if (BasicTokens.TryMatchKeyword(stmt, pos, BasicTokens.WordKeywordsLongestFirst, out _))
            return true;

        return char.IsLetter(stmt[pos]) && stmt.AsSpan(pos).Contains('=');
    }

    #endregion
}
