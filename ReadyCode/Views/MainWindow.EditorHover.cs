// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using ReadyCode.Assembler;
using ReadyCode.Diagnostics;
using ReadyCode.Editor;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Views;

// Editor hover tooltips: diagnostics, BASIC keywords and PETSCII control characters, and assembly symbols.
public partial class MainWindow
{
    #region Hover Tooltips

    // Shows a hover tooltip over either a user variable ("(variable) {type} {name}") or a BASIC
    // keyword ("{Keyword} - {Description}", reusing the same descriptions as the BASIC Keywords
    // reference panel). No tooltip for text inside a string literal or REM comment, or for an
    // unquoted value in a DATA statement's argument list (those are literals, not references).
    private void Editor_MouseHover(object sender, MouseEventArgs e)
    {
        var position = GetHoverPosition(e.GetPosition(Editor));
        if (position == null) { CloseHoverToolTip(); return; }

        var line = Editor.Document.GetLineByNumber(position.Value.Line);
        string lineText = Editor.Document.GetText(line);
        int col = position.Value.Column - 1; // AvalonEdit columns are 1-based

        bool isAsm = ViewModel.ActiveTab?.Language == EditorLanguage.Asm;

        string tooltipText;
        if (TryGetDiagnosticAt(line.Offset + col, out var diagnostic))
        {
            tooltipText = diagnostic.Message;
        }
        else if (isAsm ? !TryGetAsmHoverTooltip(lineText, col, _lastAsmResult, out tooltipText)
                       : !TryGetHoverTooltip(lineText, col, out tooltipText))
        {
            CloseHoverToolTip();
            return;
        }

        _hoverToolTip = new ToolTip
        {
            Content = tooltipText,
            PlacementTarget = Editor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
            IsOpen = true
        };
        e.Handled = true;
    }

    // TextEditor.GetPositionFromPoint reports the NEAREST character boundary to the point - WPF's
    // own GetCharacterHitFromDistance rounds to whichever half of a glyph the point falls in, so
    // boundary B's catchment zone spans from the MIDDLE of the character before it to the middle
    // of the character after it. That's exactly the right behavior for caret placement (a click
    // should snap to the nearest boundary), but wrong for "which character glyph is the mouse
    // actually over" - what word-hover detection needs, which wants floor semantics: always the
    // character whose own box contains the point, regardless of which half. Confirmed by report:
    // the tooltip triggered ~half a character early approaching a word from the left, and ~half a
    // character late approaching from the right - exactly the signature of this boundary-vs-floor
    // mismatch. Re-querying at a point shifted left by half a character's width converts one into
    // the other (for this app's always-monospace code editors): floor(X) == nearest(X -
    // halfCharWidth). Measuring via two GetVisualPosition calls' difference rather than any
    // hardcoded font metric keeps this correct for both the BASIC (Pet Me 64) and assembly fonts.
    private TextViewPosition? GetHoverPosition(Point mousePoint)
    {
        var initial = Editor.GetPositionFromPoint(mousePoint);
        if (initial == null) return null;

        try
        {
            var textView = Editor.TextArea.TextView;
            var here = textView.GetVisualPosition(initial.Value, VisualYPosition.LineTop);
            var next = textView.GetVisualPosition(
                new TextViewPosition(initial.Value.Line, initial.Value.Column + 1), VisualYPosition.LineTop);
            double charWidth = next.X - here.X;
            if (charWidth <= 0) return initial;

            return Editor.GetPositionFromPoint(new Point(mousePoint.X - charWidth / 2, mousePoint.Y)) ?? initial;
        }
        catch
        {
            // Best-effort correction only - any failure just falls back to the unadjusted
            // (nearest-boundary) position rather than breaking hover entirely.
            return initial;
        }
    }

    // A squiggle's message takes priority over the keyword/variable tooltip when the mouse is
    // over both (e.g. hovering an undefined GOTO target, which is itself a number, not a keyword).
    private bool TryGetDiagnosticAt(int offset, out EditorDiagnostic diagnostic)
    {
        foreach (var d in _currentDiagnostics)
        {
            if (offset >= d.Offset && offset < d.Offset + d.Length) { diagnostic = d; return true; }
        }
        diagnostic = default;
        return false;
    }

    private void Editor_MouseHoverStopped(object sender, MouseEventArgs e)
    {
        CloseHoverToolTip();
    }

    // Right-clicking doesn't move the caret by default in AvalonEdit, so context-menu actions
    // like "Go to Line Number" would silently act on whatever row the caret was last on instead
    // of the row actually clicked. Move it here, before the context menu opens - unless the
    // click landed inside an existing selection, in which case leave the selection intact so
    // Copy/Cut still act on it.
    private void Editor_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position == null) return;

        int offset = Editor.Document.GetOffset(position.Value.Line, position.Value.Column);

        if (Editor.SelectionLength > 0 &&
            offset >= Editor.SelectionStart && offset <= Editor.SelectionStart + Editor.SelectionLength)
            return;

        Editor.CaretOffset = offset;
        Editor.SelectionLength = 0;
    }

    private void CloseHoverToolTip()
    {
        if (_hoverToolTip == null) return;
        _hoverToolTip.IsOpen = false;
        _hoverToolTip = null;
    }

    // Builds the hover tooltip text for column `col` on the given assembly line: a mnemonic match
    // ("{Mnemonic} - {Description}") takes priority, falling back to a label/constant reference
    // (see TryGetAsmSymbolHoverTooltip) if the word under the cursor isn't a mnemonic. Returns
    // false for comment text, or when nothing recognizable is under the cursor.
    private static bool TryGetAsmHoverTooltip(string lineText, int col, AssemblyResult? asmResult, out string tooltipText)
    {
        tooltipText = "";
        if (col < 0 || col > lineText.Length) return false;

        int commentStart = lineText.IndexOf(';');
        if (commentStart >= 0 && col >= commentStart) return false;

        for (int i = 0; i < lineText.Length; i++)
        {
            if (!char.IsLetter(lineText[i])) continue;

            bool leftBoundary = i == 0 || !IsAsmWordChar(lineText[i - 1]);
            if (!leftBoundary || i + 3 > lineText.Length) continue;

            string candidate = lineText.Substring(i, 3);
            bool rightBoundary = i + 3 == lineText.Length || !IsAsmWordChar(lineText[i + 3]);
            if (rightBoundary && col >= i && col < i + 3 &&
                AsmTokens.Mnemonics.TryGetValue(candidate, out var info))
            {
                tooltipText = $"{candidate.ToUpperInvariant()} - {info.Description}";
                return true;
            }
        }

        return TryGetAsmSymbolHoverTooltip(lineText, col, asmResult, out tooltipText);
    }

    // Builds the hover tooltip for a label/constant reference at column `col`, showing its
    // resolved value (e.g. "(label) NAME = $080E"), mirroring TryGetHoverTooltip's BASIC
    // variable-hover format ("(variable) {type} {name}"). Returns false if `col` isn't on an
    // identifier, or the identifier isn't a currently-resolved label/constant (e.g. it's
    // undefined, or the document currently has assembly errors so nothing resolved).
    private static bool TryGetAsmSymbolHoverTooltip(string lineText, int col, AssemblyResult? asmResult, out string tooltipText)
    {
        tooltipText = "";
        if (asmResult == null) return false;
        if (!TryGetAsmIdentifierAt(lineText, col, out string name)) return false;

        if (asmResult.Labels.TryGetValue(name, out ushort address))
        {
            tooltipText = $"(label) {name} = ${address:X4}";
            return true;
        }

        if (asmResult.Constants.TryGetValue(name, out int constValue))
        {
            tooltipText = $"(constant) {name} = {FormatAsmSymbolValue(constValue)}";
            return true;
        }

        return false;
    }

    private static bool IsAsmWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // Builds the hover tooltip text for the token at column `col` on the given line - either
    // "(variable) {type} {name}" for a user variable, or "{Keyword} - {Description}" (reusing
    // the BASIC Keywords reference panel's descriptions) for a BASIC keyword. Returns false for
    // string/comment content, DATA-statement literal values, or when `col` isn't on a token.
    //
    // Scans the line left to right exactly like the syntax colorizers do: at every letter,
    // greedily try the longest matching keyword first; runs of letters/digits that never match
    // a keyword (plus an optional trailing $ or %) are variable names. This intentionally
    // mirrors the colorizers' keyword-collision quirk (e.g. the "OR" inside "SCORE" still reads
    // as a keyword) so the tooltip never disagrees with what's on screen.
    private static bool TryGetHoverTooltip(string lineText, int col, out string tooltipText)
    {
        tooltipText = "";
        if (col < 0 || col > lineText.Length) return false;

        // Local scope for DEF FN parameters (see VariableReference.LocalToFunction), keyed by each
        // occurrence's raw-run start offset - reuses the same source analysis the Variable
        // Explorer is built from, so the tooltip always agrees with it rather than re-deriving
        // scope independently. A DEF FN's parameter and every body reference to it are always on
        // this one line, so analyzing lineText alone (rather than the whole document) is enough.
        var localScopeByOffset = new Dictionary<int, string?>();
        foreach (var reference in VariableCrossReference.Analyze(lineText))
            localScopeByOffset[reference.Offset] = reference.LocalToFunction;

        bool inString = false;
        bool inDataArgs = false;
        int rawStart = -1;
        int i = 0;

        while (i < lineText.Length)
        {
            char c = lineText[i];

            if (c == '"')
            {
                if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, localScopeByOffset, out tooltipText))
                    return true;
                rawStart = -1;
                inString = !inString;
                i++;
                continue;
            }

            if (inString)
            {
                if (i == col)
                    return TryGetControlCharTooltip(c, out tooltipText);
                i++;
                continue;
            }

            if (c == ':')
            {
                if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, localScopeByOffset, out tooltipText))
                    return true;
                rawStart = -1;
                inDataArgs = false;
                i++;
                continue;
            }

            // "?" is PRINT's literal shorthand (see BasicTokenizer.TryMatchKeywordOrAbbreviation).
            if (c == '?')
            {
                if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, localScopeByOffset, out tooltipText))
                    return true;
                rawStart = -1;

                if (col == i)
                    return TryGetKeywordTooltip("PRINT", out tooltipText);

                i++;
                continue;
            }

            if (char.IsLetter(c))
            {
                if (BasicKeywordAbbreviations.TryMatchKeywordOrAbbreviation(
                    lineText, i, BasicTokens.WordKeywordsLongestFirst, out string keyword, out int matchedLength))
                {
                    if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, localScopeByOffset, out tooltipText))
                        return true;
                    rawStart = -1;

                    if (col >= i && col < i + matchedLength)
                        return TryGetKeywordTooltip(keyword, out tooltipText);

                    if (string.Equals(keyword, "REM", StringComparison.OrdinalIgnoreCase))
                        return false; // everything from here to end of line is a comment
                    if (string.Equals(keyword, "DATA", StringComparison.OrdinalIgnoreCase))
                        inDataArgs = true;

                    i += matchedLength;
                    continue;
                }

                if (rawStart < 0) rawStart = i;
                i++;
                continue;
            }

            if (char.IsDigit(c))
            {
                // Digits only extend an already-started identifier; a standalone digit run
                // (a line number or numeric literal) isn't a variable.
                i++;
                continue;
            }

            if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, localScopeByOffset, out tooltipText))
                return true;
            rawStart = -1;
            i++;
        }

        return rawStart >= 0 &&
               TryClassifyRawRun(lineText, rawStart, lineText.Length, col, inDataArgs, localScopeByOffset, out tooltipText);
    }

    // Checks whether the raw (non-keyword) run [start, end) - extended by one trailing $ or %
    // if present - contains `col`. DATA-statement values are excluded (they're literals, not
    // variable references); everything else in range is reported as a variable.
    private static bool TryClassifyRawRun(string lineText, int start, int end, int col, bool inDataArgs,
        IReadOnlyDictionary<int, string?> localScopeByOffset, out string tooltipText)
    {
        tooltipText = "";

        if (end < lineText.Length && (lineText[end] == '$' || lineText[end] == '%'))
            end++;

        if (col < start || col >= end || inDataArgs) return false;

        string name = lineText.Substring(start, end - start);
        string typeLabel = name[^1] switch { '%' => "Integer", '$' => "String", _ => "Float" };

        // Pi ($FF) is the only PETSCII byte above 0x7F that char.IsLetter treats as an
        // identifier character, so it can reach here as a "variable" name - which is fair,
        // since Commodore BASIC has no real constants and Pi really is just a byte value like
        // any other. But $FF is stored as raw Unicode U+00FF ('ÿ'), which is what the editor's
        // custom PETSCII font renders as Pi - a plain-font tooltip needs the real Pi glyph
        // substituted in instead, or it shows the misleading 'ÿ'.
        string displayName = name.Replace((char)0xFF, 'π');

        string scopeSuffix = localScopeByOffset.TryGetValue(start, out string? localToFunction) && localToFunction != null
            ? $" (local to FN {localToFunction})"
            : "";

        tooltipText = $"(variable) {typeLabel} {displayName}{scopeSuffix}";
        return true;
    }

    // Looks up a matched keyword's description from the same table that feeds the BASIC
    // Keywords reference panel and autocomplete, and formats it as "{Keyword} - {Description}".
    private static bool TryGetKeywordTooltip(string keyword, out string tooltipText)
    {
        var item = BasicCompletionProvider.AllItems.FirstOrDefault(
            it => string.Equals(it.Text, keyword, StringComparison.OrdinalIgnoreCase));

        if (item == null)
        {
            tooltipText = "";
            return false;
        }

        tooltipText = $"{item.Text}\r\n{item.Description}";
        return true;
    }

    // Looks up a string-literal character's PETSCII control-code name and formats it as
    // "{Name} - CHR$({code})". Returns false for ordinary printable characters.
    private static bool TryGetControlCharTooltip(char c, out string tooltipText)
    {
        if (!_petsciiControlCharNames.TryGetValue(c, out string? name))
        {
            tooltipText = "";
            return false;
        }

        tooltipText = $"{name} - CHR$({(int)c})";
        return true;
    }

    #endregion
}
