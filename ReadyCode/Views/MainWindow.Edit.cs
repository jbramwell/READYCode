// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Windows;
using System.Windows.Input;
using ReadyCode.Diagnostics;
using ReadyCode.Formatting;
using ReadyCode.Minify;
using ReadyCode.Models;
using ReadyCode.Prettify;
using ReadyCode.Tokenizer;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

// Edit menu commands and keyboard shortcuts: undo/redo, clipboard, BASIC/assembly code transforms, commenting, and go-to navigation.
public partial class MainWindow
{
    #region Edit Commands

    private void EditUndo_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.IsHexMode == true)
        {
            HexEditor.Undo();
            return;
        }
        if (Editor.CanUndo)
        {
            Editor.Undo();
        }
    }

    private void EditRedo_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.IsHexMode == true)
        {
            HexEditor.Redo();
            return;
        }
        if (Editor.CanRedo)
        {
            Editor.Redo();
        }
    }

    private void EditCut_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.IsHexMode == true)
        {
            HexEditor.Cut();
            return;
        }
        Editor.Cut();
    }

    private void EditCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.IsHexMode == true)
        {
            HexEditor.Copy();
            return;
        }
        Editor.Copy();
    }

    private void EditPaste_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.IsHexMode == true)
        {
            HexEditor.Paste();
            return;
        }
        Editor.Paste();
    }

    private void EditDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab?.IsHexMode == true)
        {
            HexEditor.Delete();
            return;
        }
        if (Editor.SelectionLength > 0)
        {
            Editor.SelectedText = string.Empty;
        }
    }

    private void EditSelectAll_Click(object sender, RoutedEventArgs e)
    {
        Editor.SelectAll();
    }

    // ── Chord shortcut: Ctrl+K → Ctrl+C / Ctrl+K → Ctrl+U / Ctrl+K → Ctrl+F ──

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Catches a Caps Lock press made via the physical key while typing here - see the
        // Activated handler in the constructor for the toggled-while-unfocused case.
        ViewModel.RefreshKeyboardLockStatus();

        // Ignore bare modifier presses — they don't break or complete a chord
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or
                     Key.LeftShift or Key.RightShift or
                     Key.LeftAlt or Key.RightAlt or Key.System)
            return;

        // Ctrl+Tab / Ctrl+Shift+Tab: cycle the active tab right/left, wrapping at either end.
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control)
        { SwitchToAdjacentTab(forward: true);  e.Handled = true; return; }
        if (e.Key == Key.Tab && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        { SwitchToAdjacentTab(forward: false); e.Handled = true; return; }

        // Find bar shortcuts (F3 / Shift+F3 / Escape)
        if (FindBar.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            { FindBar.Close(); e.Handled = true; return; }
            if (e.Key == Key.F3 && Keyboard.Modifiers == ModifierKeys.Shift)
            { FindPrev(); e.Handled = true; return; }
            if (e.Key == Key.F3 && Keyboard.Modifiers == ModifierKeys.None)
            { FindNext(); e.Handled = true; return; }
        }
        else if (e.Key == Key.F3 && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Re-open with last search and advance to next match
            OpenFind();
            FindNext();
            e.Handled = true;
            return;
        }

        // Intercept Ctrl+C/X/V when the editor is focused so PETSCII control
        // characters (C0/C1 Unicode range) survive the Windows clipboard round-trip.
        // Skipped while a Ctrl+K chord is pending so Ctrl+K, Ctrl+C can complete as
        // "Comment Selection" instead of being swallowed here as a plain copy.
        if (Editor.IsKeyboardFocusWithin && !_chordCtrlKActive)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            { ExecuteEditorCopy();  e.Handled = true; return; }
            if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
            { ExecuteEditorCut();   e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            { ExecuteEditorPaste(); e.Handled = true; return; }
        }

        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _chordCtrlKActive = true;
            e.Handled = true;
            return;
        }

        if (_chordCtrlKActive)
        {
            _chordCtrlKActive = false;
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.C) { ExecuteCommentSelection();   e.Handled = true; return; }
                if (e.Key == Key.U) { ExecuteUncommentSelection(); e.Handled = true; return; }
                if (e.Key == Key.O) { OpenFolderDialog();          e.Handled = true; return; }
                if (e.Key == Key.F) { ExecuteFormatAsmCode();      e.Handled = true; return; }
            }
            else if (Keyboard.Modifiers == ModifierKeys.None)
            {
                if (e.Key == Key.F) { CloseFolder(); e.Handled = true; return; }
            }
        }
    }

    // ── Clipboard ────────────────────────────────────────────────────────────

    private void ExecuteEditorCopy()
    {
        string text;
        if (Editor.SelectionLength > 0)
            text = Editor.SelectedText;
        else
        {
            // No selection: copy the whole current line (mirrors AvalonEdit default).
            var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
            text = Editor.Document.GetText(line.Offset, line.TotalLength);
        }
        SetClipboardWithPetscii(text);
    }

    private void ExecuteEditorCut()
    {
        if (Editor.SelectionLength == 0) return;
        SetClipboardWithPetscii(Editor.SelectedText);
        Editor.Document.Replace(Editor.SelectionStart, Editor.SelectionLength, "");
    }

    private void SetClipboardWithPetscii(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var data = new DataObject();
        // Primary: raw UTF-16LE bytes — zero information loss for any Unicode code point.
        data.SetData(_petsciiClipboardFormat, Encoding.Unicode.GetBytes(text));
        // Secondary: standard Unicode text for cross-app paste (C1 chars may be lost there).
        data.SetData(DataFormats.UnicodeText, text);
        Clipboard.SetDataObject(data, true);
    }

    private void ExecuteEditorPaste()
    {
        IDataObject? data = Clipboard.GetDataObject();
        if (data == null) return;

        string text;
        if (data.GetDataPresent(_petsciiClipboardFormat))
        {
            var bytes = (byte[])data.GetData(_petsciiClipboardFormat);
            text = Encoding.Unicode.GetString(bytes);
        }
        else if (data.GetDataPresent(DataFormats.UnicodeText))
            text = (string)data.GetData(DataFormats.UnicodeText);
        else
            return;

        int start  = Editor.SelectionStart;
        int length = Editor.SelectionLength;
        Editor.Document.Replace(start, length, text);
        Editor.SelectionLength = 0;
        // Clamp in case AvalonEdit normalised \r\n → \n, making the stored
        // document shorter than text.Length would suggest.
        Editor.SelectionStart = Math.Min(start + text.Length, Editor.Document.TextLength);
    }

    #endregion

    #region Code Transforms

    // ── Minify ───────────────────────────────────────────────────────────────

    private void ExecuteMinifyCode()
    {
        var doc = Editor.Document;
        if (doc == null || string.IsNullOrWhiteSpace(doc.Text)) return;

        var dialog = new MinifyWindow(ViewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var vm = dialog.ViewModel;
        string minified = CodeMinifier.Minify(
            doc.Text,
            removeWhitespace:       vm.RemoveWhitespace,
            replace0WithPeriod:     vm.Replace0WithPeriod,
            useScientificNotation:  vm.UseScientificNotation,
            removeComments:         vm.RemoveComments,
            simplifyNextStatements: vm.SimplifyNextStatements,
            renumberLines:          vm.RenumberLines);

        vm.ApplyTo(ViewModel.Settings);
        ViewModel.Settings.Save();

        if (minified == doc.Text)
        {
            ViewModel.SetStatus("No changes — code is already minified.");
            return;
        }

        int bytesBefore = new PrgConverter().ConvertToPrg(doc.Text).Length - 2;
        int bytesAfter  = new PrgConverter().ConvertToPrg(minified).Length - 2;
        int bytesSaved  = bytesBefore - bytesAfter;

        doc.BeginUpdate();
        try { doc.Text = minified; }
        finally { doc.EndUpdate(); }

        ViewModel.SetStatus($"Code minified: {bytesBefore:N0} → {bytesAfter:N0} bytes ({bytesSaved:N0} saved).");
    }

    // ── Prettify ─────────────────────────────────────────────────────────────

    private void ExecutePrettifyCode()
    {
        var doc = Editor.Document;
        if (doc == null || string.IsNullOrWhiteSpace(doc.Text)) return;

        var dialog = new PrettifyWindow(ViewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var vm = dialog.ViewModel;
        string prettified = CodePrettifier.Prettify(
            doc.Text,
            addWhitespace:         vm.AddWhitespace,
            replacePeriodWithZero: vm.ReplacePeriodWithZero,
            useStandardNotation:   vm.UseStandardNotation,
            addNextVariables:      vm.AddNextVariables,
            renumberLines:         vm.RenumberLines,
            lineNumberIncrement:   vm.LineNumberIncrement,
            lineNumberPadding:     vm.LineNumberPadding);

        vm.ApplyTo(ViewModel.Settings);
        ViewModel.Settings.Save();

        if (prettified == doc.Text)
        {
            ViewModel.SetStatus("No changes — code is already prettified.");
            return;
        }

        doc.BeginUpdate();
        try { doc.Text = prettified; }
        finally { doc.EndUpdate(); }

        ViewModel.SetStatus("Code prettified.");
    }

    // ── Renumber ─────────────────────────────────────────────────────────────

    private void ExecuteRenumberCode()
    {
        var doc = Editor.Document;
        if (doc == null || string.IsNullOrWhiteSpace(doc.Text)) return;

        bool hasSelection = Editor.SelectionLength > 0;
        var (selStartLine, selEndLine) = GetSelectedLineRange();

        // Collected up front - reused both to seed the dialog's default starting number (the
        // lowest BASIC line number actually selected) and, if the user keeps "Selected lines
        // only" checked, as the renumber's actual scope.
        var selectedLineNumbers = new HashSet<int>();
        if (hasSelection)
            for (int i = selStartLine; i <= selEndLine; i++)
                if (TryGetBasicLineNumber(doc, i, out int n))
                    selectedLineNumbers.Add(n);

        int defaultStart = selectedLineNumbers.Count > 0 ? selectedLineNumbers.Min() : 10;

        var dialog = new RenumberDialog(
            defaultStart: defaultStart,
            defaultIncrement: ViewModel.Settings.AutoNumberIncrement,
            hasSelection: hasSelection)
        { Owner = this };
        if (dialog.ShowDialog() != true) return;

        int padding = ViewModel.Settings.LineNumberPadding;

        // CodePrettifier.RenumberLines still rewrites GOTO/GOSUB/THEN/RESTORE/RUN references
        // across the WHOLE document, so a reference outside the selection pointing at a line
        // inside it (or vice versa) is kept correct either way.
        HashSet<int>? onlyLineNumbers = null;
        if (dialog.RenumberSelectedOnly)
        {
            onlyLineNumbers = selectedLineNumbers;
            if (onlyLineNumbers.Count == 0)
            {
                ViewModel.SetStatus("No BASIC lines in the current selection to renumber.", StatusType.Warning);
                return;
            }
        }

        string renumbered = CodePrettifier.RenumberLines(
            doc.Text, dialog.StartLineNumber, dialog.Increment, padding, onlyLineNumbers);

        if (renumbered == doc.Text)
        {
            ViewModel.SetStatus("No changes — line numbers are already sequential.");
            return;
        }

        // Renumbering can't fix a reference to a line number that never existed, and renumbering
        // only a selection can land a new number on top of an untouched line outside it - both
        // are left as-is, so warn rather than silently applying a renumber with either problem.
        var problems = BasicDiagnostics.Analyze(renumbered)
            .Where(d => d.Message.EndsWith("does not exist.") || d.Message.StartsWith("Duplicate line number"))
            .ToList();
        if (problems.Count > 0)
        {
            int danglingCount  = problems.Count(d => d.Message.EndsWith("does not exist."));
            int duplicateCount = problems.Count(d => d.Message.StartsWith("Duplicate line number"));

            var messageParts = new List<string>();
            if (danglingCount > 0)
                messageParts.Add($"{danglingCount} GOTO/GOSUB/THEN reference(s) would point to line numbers that don't exist.");
            if (duplicateCount > 0)
                messageParts.Add($"{duplicateCount} line number(s) would end up duplicated.");
            messageParts.Add("Apply the renumber anyway?");

            var result = MessageBox.Show(
                string.Join("\n\n", messageParts),
                "Renumber Code", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        doc.BeginUpdate();
        try { doc.Text = renumbered; }
        finally { doc.EndUpdate(); }

        ViewModel.SetStatus("Code renumbered.");
    }

    // ── Format (assembly) ───────────────────────────────────────────────────

    // The assembly counterpart of Minify/Prettify/Renumber above - reformats the whole document
    // in one shot per the Assembly Formatting settings (mnemonic indent column, comment alignment
    // column), rather than only as-you-type via Enter (see InsertAsmNewlineWithIndent).
    private void ExecuteFormatAsmCode()
    {
        var doc = Editor.Document;
        if (doc == null || ViewModel.ActiveTab?.Language != EditorLanguage.Asm || string.IsNullOrWhiteSpace(doc.Text)) return;

        string formatted = AsmCodeFormatter.Format(doc.Text, ViewModel.Settings.AsmMnemonicIndentColumn, ViewModel.Settings.AsmCommentAlignColumn);

        if (formatted == doc.Text)
        {
            ViewModel.SetStatus("No changes — code is already formatted.");
            return;
        }

        doc.BeginUpdate();
        try { doc.Text = formatted; }
        finally { doc.EndUpdate(); }

        ViewModel.SetStatus("Code formatted.");
    }

    // ── Editor context menu ───────────────────────────────────────────────────

    private void EditorContextMinify_Click(object sender, RoutedEventArgs e)
    {
        if (EditMinifyCommand.CanExecute(null))
            EditMinifyCommand.Execute(null);
    }

    private void EditorContextPrettify_Click(object sender, RoutedEventArgs e)
    {
        if (EditPrettifyCommand.CanExecute(null))
            EditPrettifyCommand.Execute(null);
    }

    private void EditorContextRenumber_Click(object sender, RoutedEventArgs e)
    {
        if (EditRenumberCommand.CanExecute(null))
            EditRenumberCommand.Execute(null);
    }

    // ── Comment / Uncomment ──────────────────────────────────────────────────

    private (int start, int end) GetSelectedLineRange()
    {
        var doc = Editor.Document;
        if (Editor.SelectionLength == 0)
        {
            int n = doc.GetLineByOffset(Editor.CaretOffset).LineNumber;
            return (n, n);
        }

        int selStart = Editor.SelectionStart;
        int selEnd   = selStart + Editor.SelectionLength;
        int startLine = doc.GetLineByOffset(selStart).LineNumber;
        var endDocLine = doc.GetLineByOffset(selEnd);
        // If selection ends exactly at a line's start, exclude that line
        int endLine = (endDocLine.Offset == selEnd && endDocLine.LineNumber > startLine)
            ? endDocLine.LineNumber - 1
            : endDocLine.LineNumber;
        return (startLine, endLine);
    }

    // Returns (index of first non-whitespace after optional BASIC line number,
    //          whether a space was already present before that position)
    private static (int cmdIndex, bool hadSpace) ParseBasicLinePrefix(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;      // leading whitespace
        int numStart = i;
        while (i < text.Length && char.IsDigit(text[i])) i++; // line number digits
        bool hasDigits = i > numStart;
        int afterDigits = i;
        while (i < text.Length && text[i] == ' ') i++;      // space(s) after line number
        bool hadSpace = i > afterDigits;
        return (i, !hasDigits || hadSpace);                  // hadSpace=true means no extra space needed
    }

    private void ExecuteCommentSelection()
    {
        var doc = Editor.Document;
        if (doc == null) return;

        var (startLine, endLine) = GetSelectedLineRange();

        doc.BeginUpdate();
        try
        {
            for (int lineNum = startLine; lineNum <= endLine; lineNum++)
            {
                var docLine = doc.GetLineByNumber(lineNum);
                string text = doc.GetText(docLine.Offset, docLine.Length);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var (cmd, hadSpace) = ParseBasicLinePrefix(text);
                if (cmd >= text.Length) continue;

                // Skip lines already commented
                string rest = text[cmd..];
                if (rest.StartsWith("REM ", StringComparison.OrdinalIgnoreCase) ||
                    rest.Equals("REM", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Insert "REM " (prepend a space if the line number had none after it)
                string insertion = hadSpace ? "REM " : " REM ";
                doc.Insert(docLine.Offset + cmd, insertion);
            }
        }
        finally { doc.EndUpdate(); }
    }

    private void ExecuteUncommentSelection()
    {
        var doc = Editor.Document;
        if (doc == null) return;

        var (startLine, endLine) = GetSelectedLineRange();

        doc.BeginUpdate();
        try
        {
            for (int lineNum = startLine; lineNum <= endLine; lineNum++)
            {
                var docLine = doc.GetLineByNumber(lineNum);
                string text = doc.GetText(docLine.Offset, docLine.Length);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var (cmd, _) = ParseBasicLinePrefix(text);
                if (cmd >= text.Length) continue;

                string rest = text[cmd..];
                if (rest.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                    doc.Remove(docLine.Offset + cmd, 4);
                else if (rest.Equals("REM", StringComparison.OrdinalIgnoreCase))
                    doc.Remove(docLine.Offset + cmd, 3);
            }
        }
        finally { doc.EndUpdate(); }
    }

    // Converts the highlighted text to upper or lower case, leaving it selected afterward.
    // Does nothing if no text is highlighted.
    private void ExecuteChangeSelectionCase(bool upper)
    {
        if (Editor.SelectionLength == 0) return;

        int start = Editor.SelectionStart;
        string newText = upper ? Editor.SelectedText.ToUpperInvariant() : Editor.SelectedText.ToLowerInvariant();

        Editor.Document.Replace(start, Editor.SelectionLength, newText);
        Editor.Select(start, newText.Length);
    }

    #endregion

    #region Navigation

    private void ExecuteGoToLine()
    {
        var document = Editor.Document;
        if (document == null || document.LineCount == 0) return;

        int minBasicLine = int.MaxValue, maxBasicLine = int.MinValue;
        for (int i = 1; i <= document.LineCount; i++)
        {
            if (TryGetBasicLineNumber(document, i, out int n))
            {
                if (n < minBasicLine) minBasicLine = n;
                if (n > maxBasicLine) maxBasicLine = n;
            }
        }

        bool hasBasicLines = minBasicLine != int.MaxValue;
        int effectiveMin = hasBasicLines ? minBasicLine : 0;
        int effectiveMax = hasBasicLines ? maxBasicLine : 0;

        var dialog = new GoToLineDialog(effectiveMin, effectiveMax, document.LineCount, hasBasicLines) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.EnteredLineNumber is not int target) return;

        if (dialog.IsFileLineMode)
        {
            MoveCaretToDocumentLine(Math.Clamp(target, 1, document.LineCount));
            return;
        }

        if (!JumpToBasicLine(target))
            ViewModel.SetStatus($"BASIC line {target} not found.", StatusType.Warning);
    }

    // Moves the caret to the start of AvalonEdit document line `lineNumber` (1-based), scrolling
    // it into view.
    private void MoveCaretToDocumentLine(int lineNumber)
    {
        Editor.TextArea.Caret.Line = lineNumber;
        Editor.TextArea.Caret.Column = 1;
        Editor.ScrollToLine(lineNumber);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    // Moves the caret to the start of the document line whose leading BASIC line number equals
    // `target`, scrolling it into view. Returns false if no such line exists.
    private bool JumpToBasicLine(int target)
    {
        var document = Editor.Document;
        for (int i = 1; i <= document.LineCount; i++)
        {
            if (TryGetBasicLineNumber(document, i, out int n) && n == target)
            {
                MoveCaretToDocumentLine(i);
                return true;
            }
        }
        return false;
    }

    // F12 / "Go to Definition": dispatches to the BASIC or assembly implementation depending on
    // the active tab's language, mirroring Editor_MouseHover's isAsm branching style.
    private void ExecuteGoToDefinition()
    {
        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm)
            ExecuteGoToAsmDefinition();
        else
            ExecuteGoToGotoTarget();
    }

    // If the caret sits on a GOTO/GOSUB line-number target (standard or computed, e.g.
    // "ON X GOTO 100,200,300"), jumps to that BASIC line.
    private void ExecuteGoToGotoTarget()
    {
        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        string lineText = document.GetText(line);
        int col = Editor.CaretOffset - line.Offset;

        if (!TryGetGotoTarget(lineText, col, out int target))
        {
            ViewModel.SetStatus("Not on a GOTO/GOSUB line number.", StatusType.Warning);
            return;
        }

        if (!JumpToBasicLine(target))
            ViewModel.SetStatus($"BASIC line {target} not found.", StatusType.Warning);
    }

    // If the caret sits on a label or constant reference, jumps to the document line where that
    // symbol is defined ("NAME:" or "NAME = value").
    private void ExecuteGoToAsmDefinition()
    {
        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        string lineText = document.GetText(line);
        int col = Editor.CaretOffset - line.Offset;

        if (!TryGetAsmIdentifierAt(lineText, col, out string name))
        {
            ViewModel.SetStatus("Not on a label or constant reference.", StatusType.Warning);
            return;
        }

        var definition = AsmSymbolIndex.Analyze(document.Text)
            .FirstOrDefault(o => o.Name == name &&
                (o.Kind == AsmSymbolKind.LabelDefinition || o.Kind == AsmSymbolKind.ConstantDefinition));

        if (definition.Name == null)
        {
            ViewModel.SetStatus($"No definition found for \"{name}\".", StatusType.Warning);
            return;
        }

        MoveCaretToDocumentLine(definition.LineNumber);
    }

    // Finds the identifier (label/constant name) spanning column `col` on the given assembly
    // line, using the same word-boundary rule as TryGetAsmHoverTooltip's mnemonic scan.
    private static bool TryGetAsmIdentifierAt(string lineText, int col, out string identifier)
    {
        identifier = "";
        if (col < 0 || col > lineText.Length) return false;

        int commentStart = lineText.IndexOf(';');
        if (commentStart >= 0 && col >= commentStart) return false;

        int start = col;
        while (start > 0 && IsAsmWordChar(lineText[start - 1])) start--;
        int end = col;
        while (end < lineText.Length && IsAsmWordChar(lineText[end])) end++;

        if (start == end || !(char.IsLetter(lineText[start]) || lineText[start] == '_')) return false;

        identifier = lineText[start..end];
        return true;
    }

    // Finds a GOTO/GOSUB/THEN keyword on the line (skipping string literals, stopping at REM),
    // then checks whether column `col` falls within one of the digit-only line numbers in the
    // comma-separated target list that follows (a plain GOTO/GOSUB has exactly one; a computed
    // "ON expr GOTO/GOSUB n1,n2,..." can have several; "THEN 420" is shorthand for
    // "THEN GOTO 420", an implied GOTO). Reuses the same keyword-boundary scan as
    // the hover tooltip feature so this agrees with it on packed, space-free code.
    private static bool TryGetGotoTarget(string lineText, int col, out int targetLineNumber)
    {
        targetLineNumber = 0;
        if (col < 0 || col > lineText.Length) return false;

        bool inString = false;
        int i = 0;

        while (i < lineText.Length)
        {
            char c = lineText[i];

            if (c == '"') { inString = !inString; i++; continue; }
            if (inString) { i++; continue; }

            if (char.IsLetter(c))
            {
                if (!BasicKeywordAbbreviations.TryMatchKeywordOrAbbreviation(
                    lineText, i, BasicTokens.WordKeywordsLongestFirst, out string keyword, out int matchedLength))
                { i++; continue; }

                if (string.Equals(keyword, "REM", StringComparison.OrdinalIgnoreCase))
                    return false; // rest of the line is a comment

                // "THEN 420" is CBM BASIC shorthand for "THEN GOTO 420" - an implied GOTO.
                bool isTarget =
                    string.Equals(keyword, "GOTO",  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(keyword, "THEN",  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(keyword, "GOSUB", StringComparison.OrdinalIgnoreCase);

                i += matchedLength;
                if (!isTarget) continue;

                while (true)
                {
                    while (i < lineText.Length && lineText[i] == ' ') i++;
                    int numStart = i;
                    while (i < lineText.Length && char.IsDigit(lineText[i])) i++;
                    if (i == numStart) break; // not followed by a number - no target list here

                    if (col >= numStart && col < i)
                        return int.TryParse(lineText.AsSpan(numStart, i - numStart), out targetLineNumber);

                    while (i < lineText.Length && lineText[i] == ' ') i++;
                    if (i < lineText.Length && lineText[i] == ',') { i++; continue; }
                    break;
                }
                continue;
            }

            i++;
        }

        return false;
    }

    private bool TryGetBasicLineNumber(ICSharpCode.AvalonEdit.Document.TextDocument document, int lineIndex, out int basicLineNumber)
    {
        basicLineNumber = 0;
        var line = document.GetLineByNumber(lineIndex);
        string text = document.GetText(line.Offset, line.Length).TrimStart();
        int j = 0;
        while (j < text.Length && char.IsDigit(text[j])) j++;
        return j > 0 && int.TryParse(text[0..j], out basicLineNumber);
    }

    private void EditorContextGoToLineNumber_Click(object sender, RoutedEventArgs e)
    {
        if (GoToDefinitionCommand.CanExecute(null))
            GoToDefinitionCommand.Execute(null);
    }

    #endregion
}
