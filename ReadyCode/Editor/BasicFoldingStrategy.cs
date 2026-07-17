// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ReadyCode.Diagnostics;
using ReadyCode.Prettify;

namespace ReadyCode.Editor;

/// <summary>
/// Computes collapsible fold regions for C64 BASIC source: FOR...NEXT blocks and runs of
/// consecutive REM comment lines.
/// </summary>
public class BasicFoldingStrategy
{
    #region Public Methods

    /// <summary>
    /// Recomputes fold regions for <paramref name="document"/> and applies them to <paramref name="manager"/>.
    /// </summary>
    /// <param name="manager">The folding manager to update.</param>
    /// <param name="document">The document to compute fold regions for.</param>
    public void UpdateFoldings(FoldingManager manager, TextDocument document) =>
        manager.UpdateFoldings(CreateNewFoldings(document), -1);

    /// <summary>
    /// Computes every fold region in <paramref name="document"/>, sorted by start offset (required
    /// by <see cref="FoldingManager.UpdateFoldings"/>).
    /// </summary>
    /// <param name="document">The document to compute fold regions for.</param>
    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document)
    {
        var lines = new List<LineInfo>(document.LineCount);

        foreach (var line in document.Lines)
        {
            string text = document.GetText(line);
            if (!BasicDiagnostics.TryParseLineNumber(text, out int number, out _, out _, out int codeStart))
            {
                lines.Add(new LineInfo(line, null, string.Empty, string.Empty));
                continue;
            }

            string code = text[codeStart..];
            string activeCode = code[..BasicDiagnostics.FindTopLevelRemStart(code)];
            lines.Add(new LineInfo(line, number, code, activeCode));
        }

        var foldings = new List<NewFolding>();
        AddForNextFoldings(lines, foldings);
        AddRemBlockFoldings(lines, foldings);

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    #endregion

    #region Private Methods

    // Per-line data shared across the fold passes below: the leading BASIC line number (or null
    // for a non-program line), the raw code after it, and that code with anything from a
    // top-level REM onward stripped off (REM comments aren't scanned for FOR/NEXT keywords).
    private readonly record struct LineInfo(DocumentLine Line, int? Number, string Code, string ActiveCode);

    // Tracks open FOR loops (by line index) across the whole document; a NEXT closes the
    // innermost one(s), regardless of variable name - unlike BasicDiagnostics.AnalyzeForNext,
    // folding only cares about structural nesting, not correctness, so no variable-name matching
    // is needed here. "NEXT X,Y" closes as many loops as it lists variables (same as
    // CodePrettifier.ProcessStatements' varCount handling) - popping just once per NEXT statement
    // here would strand an entry on the stack, which then gets wrongly paired with some unrelated
    // NEXT much later in the file, producing a bogus fold spanning everything in between.
    private static void AddForNextFoldings(List<LineInfo> lines, List<NewFolding> foldings)
    {
        var forLineIndexes = new Stack<int>();

        // Multiple FOR loops opened on the same source line, closed by a compound "NEXT ...,..."
        // landing on the same later line, would otherwise produce several folds with the exact
        // same start/end offsets - overlapping FoldingSections that can't be independently
        // toggled (unfolding the visible one leaves the identical one underneath still folded,
        // so the text never reappears). One fold per unique span only.
        var emittedSpans = new HashSet<(int Start, int End)>();

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Number == null) continue;

            foreach (var stmt in CodePrettifier.SplitStatements(lines[i].ActiveCode))
            {
                string trimmed = stmt.TrimStart();

                if (BasicDiagnostics._forRegex.IsMatch(trimmed))
                {
                    forLineIndexes.Push(i);
                    continue;
                }

                int closeCount;
                if (BasicDiagnostics._bareNextRegex.IsMatch(trimmed)) closeCount = 1;
                else if (BasicDiagnostics._nextVarsRegex.IsMatch(trimmed)) closeCount = trimmed[4..].Split(',').Length;
                else continue;

                for (int v = 0; v < closeCount && forLineIndexes.Count > 0; v++)
                {
                    int forIndex = forLineIndexes.Pop();
                    if (i > forIndex && emittedSpans.Add((forIndex, i)))
                        foldings.Add(new NewFolding(lines[forIndex].Line.EndOffset, lines[i].Line.EndOffset));
                }
            }
        }
    }

    // Folds runs of 2+ consecutive full-line REM statements into one block - a single standalone
    // REM line has nothing to hide, and a trailing inline comment (e.g. "X=1:REM note") isn't a
    // full-line REM, so it never starts or extends a run.
    private static void AddRemBlockFoldings(List<LineInfo> lines, List<NewFolding> foldings)
    {
        int runStart = -1;

        for (int i = 0; i <= lines.Count; i++)
        {
            bool isFullRemLine = i < lines.Count && lines[i].Number != null && IsFullRemLine(lines[i]);
            if (isFullRemLine)
            {
                if (runStart < 0) runStart = i;
                continue;
            }

            if (runStart >= 0 && i - runStart >= 2)
                foldings.Add(new NewFolding(lines[runStart].Line.EndOffset, lines[i - 1].Line.EndOffset));
            runStart = -1;
        }
    }

    // A line's code is entirely one REM statement (not a trailing inline comment) exactly when
    // REM-truncation stripped everything (ActiveCode empty) from genuinely non-empty code - a
    // bare "100" line with no code at all is also "empty after truncation" but never REM at all,
    // hence the Code.Length check.
    private static bool IsFullRemLine(LineInfo info) => info.Code.Length > 0 && info.ActiveCode.Length == 0;

    #endregion
}
