// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Prettify;
using ReadyCode.Tokenizer;

namespace ReadyCode.Diagnostics;

/// <summary>
/// A single occurrence of a variable in a BASIC source document: an offset/length span (matching
/// AvalonEdit's document offset model) plus whether this occurrence assigns the variable (a
/// "write") or merely uses its value (a "read").
/// </summary>
/// <param name="Name">The variable's full name, including any trailing $ or % suffix, upper-invariant.</param>
/// <param name="Offset">The character offset into the analyzed source where the name starts.</param>
/// <param name="Length">The number of characters the name spans (not including any array subscript).</param>
/// <param name="IsWrite">Whether this occurrence assigns the variable, rather than just reading it.</param>
public readonly record struct VariableReference(string Name, int Offset, int Length, bool IsWrite);

/// <summary>
/// Finds every variable in a BASIC source document and classifies each occurrence as a read or a
/// write (assignment via bare/<c>LET</c>, <c>FOR</c>, <c>INPUT</c>/<c>READ</c>/<c>GET</c>, or a
/// <c>DEF FN</c> parameter - including array targets like <c>A(I)=5</c>).
/// </summary>
public static class VariableCrossReference
{
    #region Public Methods

    /// <summary>
    /// Analyzes the given BASIC source and returns every variable occurrence found, in source order.
    /// </summary>
    /// <param name="source">The full BASIC source to analyze.</param>
    public static IReadOnlyList<VariableReference> Analyze(string source)
    {
        var references = new List<VariableReference>();

        foreach (var (line, lineOffset) in BasicDiagnostics.EnumerateLines(source))
            AnalyzeLine(line, lineOffset, references);

        return references;
    }

    #endregion

    #region Private Methods

    private static void AnalyzeLine(string line, int lineOffset, List<VariableReference> references)
    {
        if (!BasicDiagnostics.TryParseLineNumber(line, out _, out _, out _, out int codeStart))
            return; // no leading line number - not a program line, nothing to analyze

        string code = line[codeStart..];
        int codeOffset = lineOffset + codeStart;

        // REM makes the rest of the physical line a comment - not scanned for variables,
        // matching every other analyzer in this codebase.
        string activeCode = code[..BasicDiagnostics.FindTopLevelRemStart(code)];

        int stmtOffset = codeOffset;
        foreach (var stmt in CodePrettifier.SplitStatements(activeCode))
        {
            AnalyzeStatement(stmt, stmtOffset, references);
            stmtOffset += stmt.Length + 1; // +1 for the ':' separator consumed between statements
        }
    }

    private static void AnalyzeStatement(string stmt, int stmtOffset, List<VariableReference> references)
    {
        var writeOffsets = new HashSet<int>();
        string trimmed = stmt.TrimStart();
        CollectWriteTargets(trimmed, stmt.Length - trimmed.Length, writeOffsets);

        EnumerateOccurrences(stmt, (name, offset, length) =>
            references.Add(new VariableReference(
                name.ToUpperInvariant(), stmtOffset + offset, length, writeOffsets.Contains(offset))));
    }

    // ── Pass 1: enumerate every variable occurrence (default: read) ──────────────────────────

    // Same walk as MainWindow.TryGetHoverTooltip: skip strings, skip DATA-statement argument
    // values (literals, not variable references), match keywords via BasicTokens.TryMatchKeyword
    // and skip past them (including the deliberate no-word-boundary quirk - "OR" inside "SCORE"
    // ends the run early, same as every other scanner here, so this never disagrees with what
    // the hover tooltip or colorizers show), and report every other letter/digit run - plus an
    // optional trailing $ or % - as a variable occurrence.
    private static void EnumerateOccurrences(string stmt, Action<string, int, int> onOccurrence)
    {
        bool inString = false;
        int rawStart = -1;
        int i = 0;

        while (i < stmt.Length)
        {
            char c = stmt[i];

            if (c == '"')
            {
                FlushRawRun(stmt, ref rawStart, i, onOccurrence);
                inString = !inString;
                i++;
                continue;
            }
            if (inString) { i++; continue; }

            if (char.IsLetter(c))
            {
                if (BasicTokens.TryMatchKeyword(stmt, i, BasicTokens.WordKeywordsLongestFirst, out string keyword))
                {
                    FlushRawRun(stmt, ref rawStart, i, onOccurrence);

                    if (string.Equals(keyword, "DATA", StringComparison.OrdinalIgnoreCase))
                        return; // rest of the statement is literal DATA values, not references

                    i += keyword.Length;

                    if (string.Equals(keyword, "FN", StringComparison.OrdinalIgnoreCase))
                    {
                        // The identifier right after FN - in both "DEF FN name(param)=..." and a
                        // call like "Y=FN name(X)" - is the function's own name, never a
                        // variable, so it's skipped silently rather than reported as a read.
                        while (i < stmt.Length && stmt[i] == ' ') i++;
                        while (i < stmt.Length && char.IsLetterOrDigit(stmt[i])) i++;
                    }
                    continue;
                }

                if (rawStart < 0) rawStart = i;
                i++;
                continue;
            }

            if (char.IsDigit(c)) { i++; continue; } // digits only extend an already-open run

            FlushRawRun(stmt, ref rawStart, i, onOccurrence);
            i++;
        }

        FlushRawRun(stmt, ref rawStart, stmt.Length, onOccurrence);
    }

    private static void FlushRawRun(string stmt, ref int rawStart, int end, Action<string, int, int> onOccurrence)
    {
        if (rawStart < 0) return;

        if (end < stmt.Length && (stmt[end] == '$' || stmt[end] == '%')) end++;

        onOccurrence(stmt.Substring(rawStart, end - rawStart), rawStart, end - rawStart);
        rawStart = -1;
    }

    // ── Pass 2: determine which offsets in the statement are writes ──────────────────────────

    // fragmentOffset is the fragment's own offset relative to the statement start (so results
    // can be added to writeOffsets, which is keyed relative to the statement too).
    private static void CollectWriteTargets(string fragment, int fragmentOffset, HashSet<int> writeOffsets)
    {
        if (!TryCollectForTarget(fragment, fragmentOffset, writeOffsets))
            if (!TryCollectListTargets(fragment, fragmentOffset, writeOffsets))
                if (!TryCollectDefFnTarget(fragment, fragmentOffset, writeOffsets))
                    TryCollectAssignmentTarget(fragment, fragmentOffset, writeOffsets);

        // Independent of the above - "IF X=1 THEN Y=5" needs Y (not X, the condition) recognized
        // as a write. The IF/condition prefix never matches any of the patterns above (they all
        // require the fragment to start with FOR/INPUT/READ/GET/DEF/LET/an identifier, and "IF"
        // is none of those), so recursing into the THEN tail is always safe to attempt.
        if (TryFindTopLevelThen(fragment, out int afterThen))
        {
            string tail = fragment[afterThen..];
            string trimmedTail = tail.TrimStart();
            CollectWriteTargets(trimmedTail, fragmentOffset + afterThen + (tail.Length - trimmedTail.Length), writeOffsets);
        }
    }

    // FOR var = ... - scalar only (real BASIC has no array FOR variables).
    private static bool TryCollectForTarget(string fragment, int fragmentOffset, HashSet<int> writeOffsets)
    {
        var match = BasicDiagnostics._forRegex.Match(fragment);
        if (!match.Success) return false;

        writeOffsets.Add(fragmentOffset + match.Groups[1].Index);
        return true;
    }

    // INPUT/INPUT#/READ/GET/GET# var[,var...] - each target is a write; array targets allowed.
    private static bool TryCollectListTargets(string fragment, int fragmentOffset, HashSet<int> writeOffsets)
    {
        if (!BasicTokens.TryMatchKeyword(fragment, 0, BasicTokens.WordKeywordsLongestFirst, out string keyword))
            return false;

        string upper = keyword.ToUpperInvariant();
        bool isInput = upper is "INPUT" or "INPUT#";
        bool isGet   = upper is "GET" or "GET#";
        bool isRead  = upper == "READ";
        if (!isInput && !isGet && !isRead) return false;

        int i = keyword.Length;
        bool hasFileNumber = upper.EndsWith('#') || (i < fragment.Length && fragment[i] == '#');

        if (hasFileNumber)
        {
            if (i < fragment.Length && fragment[i] == '#') i++;
            SkipSpaces(fragment, ref i);
            while (i < fragment.Length && fragment[i] != ',') i++;
            if (i < fragment.Length) i++; // consume the ',' after the file number
        }
        else if (isInput)
        {
            int j = i;
            SkipSpaces(fragment, ref j);
            if (j < fragment.Length && fragment[j] == '"')
            {
                int k = j + 1;
                while (k < fragment.Length && fragment[k] != '"') k++;
                if (k < fragment.Length) k++; // closing quote
                int m = k;
                SkipSpaces(fragment, ref m);
                if (m < fragment.Length && fragment[m] == ';')
                    i = m + 1; // consume prompt string + ';'
                // else: not actually a prompt - leave i unchanged
            }
        }

        while (true)
        {
            SkipSpaces(fragment, ref i);
            if (!TryReadTarget(fragment, ref i, out int nameStart, out _)) break;
            writeOffsets.Add(fragmentOffset + nameStart);
            SkipSpaces(fragment, ref i);
            if (i < fragment.Length && fragment[i] == ',') { i++; continue; }
            break;
        }

        return true;
    }

    // DEF FN name(param) = ... - the parameter (scalar) is a write; the function's own name isn't
    // a variable at all.
    private static bool TryCollectDefFnTarget(string fragment, int fragmentOffset, HashSet<int> writeOffsets)
    {
        int i = 0;
        if (!BasicTokens.TryMatchKeyword(fragment, i, BasicTokens.WordKeywordsLongestFirst, out string defKeyword) ||
            !string.Equals(defKeyword, "DEF", StringComparison.OrdinalIgnoreCase))
            return false;
        i += defKeyword.Length;
        SkipSpaces(fragment, ref i);

        if (!BasicTokens.TryMatchKeyword(fragment, i, BasicTokens.WordKeywordsLongestFirst, out string fnKeyword) ||
            !string.Equals(fnKeyword, "FN", StringComparison.OrdinalIgnoreCase))
            return false;
        i += fnKeyword.Length;
        SkipSpaces(fragment, ref i);

        if (!TryReadIdentifier(fragment, ref i, out _, out _)) return false; // the function's own name
        SkipSpaces(fragment, ref i);

        if (i >= fragment.Length || fragment[i] != '(') return false;
        i++;
        SkipSpaces(fragment, ref i);

        if (TryReadIdentifier(fragment, ref i, out int paramStart, out _))
            writeOffsets.Add(fragmentOffset + paramStart);

        return true;
    }

    // Bare "X=..." or "LET X=..." - including array targets like "A(I)=5" or "A(I,J)=5".
    private static bool TryCollectAssignmentTarget(string fragment, int fragmentOffset, HashSet<int> writeOffsets)
    {
        int i = 0;
        if (BasicTokens.TryMatchKeyword(fragment, i, BasicTokens.WordKeywordsLongestFirst, out string letKeyword) &&
            string.Equals(letKeyword, "LET", StringComparison.OrdinalIgnoreCase))
        {
            i += letKeyword.Length;
            SkipSpaces(fragment, ref i);
        }

        if (!TryReadTarget(fragment, ref i, out int nameStart, out _)) return false;
        SkipSpaces(fragment, ref i);

        if (i >= fragment.Length || fragment[i] != '=') return false;

        writeOffsets.Add(fragmentOffset + nameStart);
        return true;
    }

    // Finds a top-level (not inside a string) THEN keyword, same scan style as
    // BasicDiagnostics.FindTopLevelRemStart.
    private static bool TryFindTopLevelThen(string fragment, out int afterThen)
    {
        afterThen = 0;
        bool inString = false;
        int i = 0;

        while (i < fragment.Length)
        {
            char c = fragment[i];
            if (c == '"') { inString = !inString; i++; continue; }
            if (inString) { i++; continue; }

            if (char.IsLetter(c))
            {
                if (BasicTokens.TryMatchKeyword(fragment, i, BasicTokens.WordKeywordsLongestFirst, out string keyword))
                {
                    if (string.Equals(keyword, "THEN", StringComparison.OrdinalIgnoreCase))
                    {
                        afterThen = i + keyword.Length;
                        return true;
                    }
                    i += keyword.Length;
                    continue;
                }
            }
            i++;
        }
        return false;
    }

    // Reads one identifier (+ optional trailing $/%) starting exactly at i, same raw-run rules
    // (and keyword-collision quirk) as the general occurrence scan. Does not consume a following
    // array subscript - see TryReadTarget for that.
    private static bool TryReadIdentifier(string text, ref int i, out int nameStart, out int nameLength)
    {
        nameStart = 0; nameLength = 0;
        if (i >= text.Length || !char.IsLetter(text[i])) return false;

        int start = i;
        int end = start;
        while (end < text.Length)
        {
            if (BasicTokens.TryMatchKeyword(text, end, BasicTokens.WordKeywordsLongestFirst, out _)) break;
            if (char.IsLetterOrDigit(text[end])) { end++; continue; }
            break;
        }
        if (end == start) return false; // the character at `i` was itself a keyword match

        if (end < text.Length && (text[end] == '$' || text[end] == '%')) end++;

        nameStart = start;
        nameLength = end - start;
        i = end;
        return true;
    }

    // TryReadIdentifier, then - if immediately followed by '(' - also consumes a balanced-paren
    // array subscript (string-literal-aware, e.g. "A(LEN(\"X\"))"). The reported name/length
    // cover only the array's own name, not the subscript.
    private static bool TryReadTarget(string text, ref int i, out int nameStart, out int nameLength)
    {
        if (!TryReadIdentifier(text, ref i, out nameStart, out nameLength)) return false;

        int j = i;
        SkipSpaces(text, ref j);
        if (j < text.Length && text[j] == '(' && TrySkipBalancedParens(text, ref j))
            i = j;

        return true;
    }

    private static bool TrySkipBalancedParens(string text, ref int i)
    {
        if (i >= text.Length || text[i] != '(') return false;

        bool inString = false;
        int depth = 0;
        int j = i;

        while (j < text.Length)
        {
            char c = text[j];
            if (c == '"') { inString = !inString; j++; continue; }
            if (!inString)
            {
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) { i = j + 1; return true; }
                }
            }
            j++;
        }
        return false; // unbalanced - never closed
    }

    private static void SkipSpaces(string text, ref int i)
    {
        while (i < text.Length && text[i] == ' ') i++;
    }

    #endregion
}
