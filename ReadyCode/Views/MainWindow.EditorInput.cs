// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ReadyCode.Diagnostics;
using ReadyCode.Editor;
using ReadyCode.Formatting;
using ReadyCode.Interop;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Views;

// Typing in the editor: key handling, C64 shift / keyword abbreviations, line-number padding, paste, caret status, and ghost-text keyword completion.
public partial class MainWindow
{
    #region Editor Input

    private void Editor_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateScreenPositionStatus();
    }

    private void Editor_CaretPositionChanged(object? sender, EventArgs e)
    {
        UpdateScreenPositionStatus();
        UpdateGhostText();
    }

    private void CapsLockIndicator_Click(object sender, RoutedEventArgs e)
    {
        KeyboardLockKeys.ToggleCapsLock();
        ViewModel.RefreshKeyboardLockStatus();
    }

    // Setting ViewModel.IsUpperCaseModeActive here is the only work needed - it writes through
    // to ActiveTab.IsUpperCaseModeActive, whose PropertyChanged MainViewModel.
    // OnActiveTabPropertyChanged already turns into RefreshShiftModeStatus(), which updates
    // ViewModel.IsUpperCaseModeActive's own backing field, which the constructor's
    // PropertyChanged handler below turns into ApplyUpperCaseMode(). The Edit menu's "Upper Case
    // Mode" checkbox drives the exact same property via a plain TwoWay binding.
    private void ShiftModeIndicator_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsUpperCaseModeActive = !ViewModel.IsUpperCaseModeActive;
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        UpdateLineCountStatus();

        if (!_activatingTab && !ViewModel.IsModified)
            ViewModel.IsModified = true;

        // A "Disassemble file" tab starts with real memory addresses in the gutter, but unlike a
        // live-memory disassembly tab (IsDisassemblyMode) it stays editable, so once the user
        // changes anything the line/address pairing can no longer be trusted. Just clear the
        // snapshot here - the debounced RunDocumentAnalysis this same edit already armed will run
        // UpdateAsmGutterAddresses shortly, which takes back over now that the snapshot is gone,
        // showing addresses recomputed from the edited source instead (or plain line numbers, if
        // it no longer assembles cleanly).
        if (!_activatingTab && ViewModel.ActiveTab is { IsDisassemblyMode: false, DisassemblyLineAddresses: not null } tab)
            tab.DisassemblyLineAddresses = null;

        // Debounced so a full re-analysis doesn't run on every keystroke; also covers tab
        // switches, since assigning Editor.Document in ActivateTab raises this same event.
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Start();

        // Editing the document directly (rather than through Replace/Replace All) leaves the
        // cached _findMatches offsets stale, so Next/Prev would jump to whatever text now sits
        // at the old offset instead of the actual next match. Debounced the same way as
        // diagnostics above, so a full re-search doesn't run on every keystroke either.
        // The highlight itself doesn't need to wait for that recompute: FindHighlightColorizer
        // paints from AnchorSegments, which AvalonEdit shifts automatically as this same edit
        // is applied, so it keeps tracking its matched text (and repaints as part of this same
        // keystroke's normal redraw) instead of sitting at a stale numeric offset.
        if (FindBar.Visibility == Visibility.Visible)
        {
            _findUpdateTimer.Stop();
            _findUpdateTimer.Start();
        }
    }

    /// <summary>
    /// Pads each line's leading line number to the configured width, mirroring the
    /// padding Editor_PreviewKeyDown applies as the user types - imported .prg source
    /// otherwise comes back with its zero padding stripped (the format only stores numbers)
    /// </summary>
    private string PadLineNumbers(string sourceCode) => PadLineNumbers(sourceCode, ViewModel.Settings.LineNumberPadding);

    // The setting-free core of PadLineNumbers, safe to call off the UI thread (project search).
    private static string PadLineNumbers(string sourceCode, int padding)
    {
        if (padding <= 0) return sourceCode;

        string[] lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            Match match = _leadingLineNumberPattern.Match(lines[i]);
            if (!match.Success) continue;

            string digits = match.Groups[2].Value;
            if (digits.Length >= padding) continue;

            string padded = digits.PadLeft(padding, '0');
            lines[i] = lines[i].Remove(match.Groups[2].Index, digits.Length).Insert(match.Groups[2].Index, padded);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+End: AvalonEdit's own built-in handling already moves the caret to the document
        // end, but the scroll can undershoot on the first press for the same height-tree-staleness
        // reason NavigateToCurrentMatch's own deferred BringCaretToView() call works around - see
        // the comment there. Not marking this Handled; AvalonEdit's default processing still runs
        // normally, this just schedules the same corrective follow-up after its layout pass lands.
        //
        // Enter/Return: the new line the caret lands on is column 1, but if the previous line had
        // scrolled the view far to the right (word wrap off), nothing otherwise re-scrolls back
        // left - none of Enter's several handlers (AvalonEdit's own default newline insertion,
        // InsertAsmNewlineWithIndent, or the BASIC auto-number/zero-pad branches further below)
        // call BringCaretToView() themselves.
        if ((e.Key == Key.End && Keyboard.Modifiers == ModifierKeys.Control) || e.Key == Key.Enter || e.Key == Key.Return)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Editor.TextArea.Caret.BringCaretToView());

        // Assembly auto-indent: handled up front and returns, rather than falling through to the
        // BASIC-specific line-number logic below (_leadingLineNumberPattern wouldn't match an
        // assembly line anyway, but this keeps the two languages' Enter handling clearly separate).
        bool isEnterForAsmIndent = (e.Key == Key.Enter || e.Key == Key.Return) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (isEnterForAsmIndent && ViewModel.ActiveTab?.Language == EditorLanguage.Asm && ViewModel.Settings.AsmAutoIndent)
        {
            e.Handled = true;
            InsertAsmNewlineWithIndent();
            return;
        }

        // Ctrl+K chord prefix — next key determines the action
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ctrlKChordPending = true;
            e.Handled = true;
            return;
        }
        if (_ctrlKChordPending)
        {
            _ctrlKChordPending = false;
            if (e.Key == Key.U) // Ctrl+K U — close saved tabs
            {
                foreach (var t in ViewModel.OpenTabs.Where(t => !t.IsModified).ToList())
                    CloseTab(t);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.W) // Ctrl+K W — close all tabs
            {
                foreach (var t in ViewModel.OpenTabs.ToList())
                    if (!CloseTab(t)) break;
                e.Handled = true;
                return;
            }
        }

        // Ctrl+Space: open the full completion popup for the current prefix
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenCompletionPopup();
            e.Handled = true;
            return;
        }

        // TAB: accept ghost-text suggestion when one is visible; otherwise fall through
        // to the existing line-number padding logic below.
        if (e.Key == Key.Tab && _completionWindow == null)
        {
            if (!string.IsNullOrEmpty(_ghostRenderer.GhostText))
            {
                AcceptGhostCompletion();
                e.Handled = true;
                return;
            }
        }

        // Backspace while a completion popup is open: update filter after the character is removed
        if (e.Key == Key.Back && _completionWindow != null)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_completionWindow == null) return;
                var (_, w) = GetWordBeforeCaret();
                if (string.IsNullOrEmpty(w)) _completionWindow.Close();
                else _completionWindow.CompletionList.SelectItem(w);
            });
        }

        bool isEnter      = e.Key == Key.Enter || e.Key == Key.Return;
        bool isSpaceOrTab = e.Key == Key.Space || e.Key == Key.Tab;

        if (!isEnter && !isSpaceOrTab) return;

        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        string lineText = document.GetText(line);

        Match match = _leadingLineNumberPattern.Match(lineText);

        // Zero-pad: fires on Space/Tab (at end of line number) and Enter (anywhere on line)
        if (ViewModel.Settings.LineNumberPadding > 0 && match.Success)
        {
            bool shouldPad = isEnter;
            if (isSpaceOrTab)
            {
                int caretCol      = Editor.CaretOffset - line.Offset;
                int lineNumEndCol = match.Groups[2].Index + match.Groups[2].Length;
                shouldPad = (caretCol == lineNumEndCol);
            }

            if (shouldPad)
            {
                string digits = match.Groups[2].Value;
                if (digits.Length < ViewModel.Settings.LineNumberPadding)
                {
                    string padded      = digits.PadLeft(ViewModel.Settings.LineNumberPadding, '0');
                    int numberStart    = line.Offset + match.Groups[2].Index;
                    int numberEnd      = numberStart + digits.Length;
                    int delta          = padded.Length - digits.Length;
                    int oldCaretOffset = Editor.CaretOffset;

                    document.Replace(numberStart, digits.Length, padded);

                    if (oldCaretOffset >= numberEnd)
                        Editor.CaretOffset = oldCaretOffset + delta;
                    else if (oldCaretOffset > numberStart)
                        Editor.CaretOffset = numberStart + padded.Length;

                    // Refresh match/lineText after padding so auto-number below sees the updated line
                    lineText = document.GetText(line);
                    match    = _leadingLineNumberPattern.Match(lineText);
                }
            }
        }

        // Auto-number: fires on Enter only when the line has content beyond the line number.
        // Shift+Enter suppresses auto-numbering so the user can insert a plain newline.
        bool isShiftEnter = isEnter && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        
        if (isEnter && !isShiftEnter && ViewModel.Settings.AutoNumberLines && match.Success)
        {
            // A line that is only a line number (+ optional whitespace) should not trigger auto-numbering
            string afterNumber = lineText.Substring(match.Groups[2].Index + match.Groups[2].Length);
            if (!string.IsNullOrWhiteSpace(afterNumber))
            {
                if (int.TryParse(match.Groups[2].Value, out int currentNumber))
                {
                    int padding = ViewModel.Settings.LineNumberPadding;

                    // If the caret sits at or before the line's own leading number (e.g. pressed
                    // Enter at column 1), the intent is "insert a new line ABOVE this one," not
                    // "continue the sequence after it" - split the gap between the line above (or
                    // 0, if this is the very first line) and this line's own number, instead of
                    // incrementing forward from it and colliding with this line's content.
                    int caretCol = Editor.CaretOffset - line.Offset;
                    if (caretCol <= match.Groups[2].Index)
                    {
                        int previousNumber = 0;
                        bool hasPreviousNumber = false;
                        DocumentLine? prevDocLine = line.PreviousLine;
                        if (prevDocLine != null)
                        {
                            Match prevMatch = _leadingLineNumberPattern.Match(document.GetText(prevDocLine));
                            if (prevMatch.Success && int.TryParse(prevMatch.Groups[2].Value, out int prevNumber))
                            {
                                previousNumber = prevNumber;
                                hasPreviousNumber = true;
                            }
                        }

                        // 0 is itself a valid BASIC line number, so when there's no real previous
                        // line to stay above, the only floor is the current line's own number -
                        // don't also require the midpoint to be strictly greater than the
                        // "previousNumber = 0" default, or line 1 would have nowhere to go.
                        int aboveMidpoint = (previousNumber + currentNumber) / 2;
                        bool noRoom = aboveMidpoint >= currentNumber ||
                            (hasPreviousNumber && aboveMidpoint <= previousNumber);
                        if (noRoom) return;

                        string aboveLabel = padding > 0
                            ? aboveMidpoint.ToString().PadLeft(padding, '0')
                            : aboveMidpoint.ToString();

                        // Insert the new numbered (empty) line before this line's own start, then
                        // land the caret right after the new line number - ready to type on the
                        // freshly inserted line, same as the "continue forward" branch below lands
                        // the caret right after the number it inserts.
                        e.Handled = true;
                        document.Insert(line.Offset, aboveLabel + " " + Environment.NewLine);
                        Editor.CaretOffset = line.Offset + aboveLabel.Length + 1;
                        return;
                    }

                    int nextNumber = currentNumber + ViewModel.Settings.AutoNumberIncrement;

                    // If the naive increment would land on or past an already-existing line
                    // number below, split the gap instead: use the midpoint between the
                    // current and next line numbers. If there's no room for a number in
                    // between, don't auto-number at all - fall through to a plain newline,
                    // same as Shift+Enter.
                    DocumentLine? nextDocLine = line.NextLine;
                    if (nextDocLine != null)
                    {
                        Match nextMatch = _leadingLineNumberPattern.Match(document.GetText(nextDocLine));

                        if (nextMatch.Success &&
                            int.TryParse(nextMatch.Groups[2].Value, out int nextExistingNumber) &&
                            nextNumber >= nextExistingNumber)
                        {
                            int midpoint = (currentNumber + nextExistingNumber) / 2;
                            if (midpoint <= currentNumber) return;
                            nextNumber = midpoint;
                        }
                    }

                    string nextLabel = padding > 0
                        ? nextNumber.ToString().PadLeft(padding, '0')
                        : nextNumber.ToString();

                    // Let the newline insert happen, then prepend the next line number
                    e.Handled = true;
                    int insertOffset = Editor.CaretOffset;
                    document.Insert(insertOffset, Environment.NewLine + nextLabel + " ");
                    Editor.CaretOffset = insertOffset + Environment.NewLine.Length + nextLabel.Length + 1;
                }
            }
        }
    }

    // Handles Enter in an assembly tab when Auto-indent is on. Normalizes the line being left in
    // two independent ways before moving on:
    //  1. If it's a bare mnemonic line typed without the configured indent and/or in the wrong
    //     case (e.g. "lda #25" at column 1), re-indents it to AsmMnemonicIndentColumn and
    //     upper-cases just the mnemonic token, leaving the operand/comment exactly as typed.
    //  2. If it has an inline ";" comment with real code before it (a whole-line comment is left
    //     alone - shifting it to the comment column would just waste space), the comment is
    //     realigned to AsmCommentAlignColumn.
    // Then indents the new line to the mnemonic column too, if the (possibly just-normalized)
    // current line was a mnemonic line. A label-only line, directive, comment, or blank line
    // triggers neither the indent nor the next-line auto-indent - only comment alignment applies
    // to those, when they have a trailing comment.
    private void InsertAsmNewlineWithIndent()
    {
        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        string lineText = document.GetText(line);
        int caretInLine = Editor.CaretOffset - line.Offset;
        bool caretAtEnd = caretInLine == lineText.Length;

        string workingLine = lineText;

        string trimmedStart = workingLine.TrimStart();
        int oldIndentLength = workingLine.Length - trimmedStart.Length;
        string trimmed = trimmedStart.TrimEnd();

        bool isMnemonicLine = AsmCodeFormatter.TryParseAsmMnemonicLine(trimmed, out string mnemonic, out string rest);
        string indent = isMnemonicLine
            ? new string(' ', Math.Max(0, ViewModel.Settings.AsmMnemonicIndentColumn - 1))
            : "";

        if (isMnemonicLine)
        {
            string normalized = indent + mnemonic.ToUpperInvariant() + rest;
            if (normalized != workingLine)
            {
                caretInLine = Math.Clamp(caretInLine + (indent.Length - oldIndentLength), 0, normalized.Length);
                workingLine = normalized;
            }
        }

        // Same convention as ".byte"/string literals aren't excluded here either - matches
        // AsmCommentColorizer's own (deliberately simple) first-";" rule, so a comment realigned
        // here is always exactly what's shown colored as a comment.
        int semicolonIndex = workingLine.IndexOf(';');
        if (semicolonIndex > 0)
        {
            string codePart = workingLine[..semicolonIndex];
            if (!string.IsNullOrWhiteSpace(codePart))
            {
                string commentPart = workingLine[semicolonIndex..];
                string trimmedCode = codePart.TrimEnd();
                int targetLength = Math.Max(0, ViewModel.Settings.AsmCommentAlignColumn - 1);
                string alignedCode = trimmedCode.Length < targetLength ? trimmedCode.PadRight(targetLength) : trimmedCode + "  ";
                string realigned = alignedCode + commentPart;

                if (realigned != workingLine)
                {
                    if (caretAtEnd)
                        caretInLine = realigned.Length;
                    else if (caretInLine >= semicolonIndex)
                        caretInLine = Math.Clamp(alignedCode.Length + (caretInLine - semicolonIndex), 0, realigned.Length);
                    else
                        caretInLine = Math.Clamp(caretInLine, 0, realigned.Length);

                    workingLine = realigned;
                }
            }
        }

        if (workingLine != lineText)
            document.Replace(line.Offset, line.Length, workingLine);

        int insertOffset = line.Offset + caretInLine;
        document.Insert(insertOffset, Environment.NewLine + indent);
        Editor.CaretOffset = insertOffset + Environment.NewLine.Length + indent.Length;
    }

    private void Editor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // C64 BASIC is upper case by default - force typed text to match, unless Shift or Caps
        // Lock makes it "shifted" (see ApplyC64Shift). Assembly source case is significant
        // (labels, comments) - leave it to AvalonEdit's normal input handling.
        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm) return;

        e.Handled = true;

        string insertText = TryGetKeywordAbbreviationGlyph(e.Text) ?? ApplyC64Shift(e.Text);
        int start = Editor.SelectionStart;
        int length = Editor.SelectionLength;

        Editor.Document.Replace(start, length, insertText);

        int caretOffset = start + insertText.Length;
        Editor.CaretOffset = caretOffset;
        Editor.Select(caretOffset, 0);

        // e.Handled = true above suppresses AvalonEdit's own TextInput handling entirely, which
        // is what normally scrolls the caret into view after inserting typed text - without this,
        // typing past the right edge with word wrap off never auto-scrolls horizontally.
        Editor.TextArea.Caret.BringCaretToView();

        // Keep the completion popup in sync — the TextArea.TextEntered event is suppressed
        // because we set e.Handled = true, so we update the filter manually here.
        if (_completionWindow != null)
        {
            var (_, word) = GetWordBeforeCaret();
            if (string.IsNullOrEmpty(word))
                _completionWindow.Close();
            else
                _completionWindow.CompletionList.SelectItem(word);
        }
    }

    // Recognizes a C64 BASIC keyword shift-abbreviation as it's typed: an unshifted letter prefix
    // already sitting in the document (forced upper case by the caller) immediately followed by
    // one shifted letter - exactly the CRUNCH routine's keyboard shortcut for that keyword (see
    // BasicKeywordAbbreviations). Shift is detected from the raw case of `text`, since typing an
    // unshifted letter always yields lower case and a shifted one upper case, regardless of the
    // caller's own upper-casing of the character actually inserted. Returns the PETSCII graphic
    // character for the shifted letter (its byte value equals the letter's lower case ASCII code -
    // see PetsciiScreenCodeMap) so the abbreviation is inserted and rendered with its correct C64
    // glyph instead of expanding to the keyword's full spelling, or null if `text` doesn't
    // complete a recognized abbreviation at the caret.
    private string? TryGetKeywordAbbreviationGlyph(string text)
    {
        if (Editor.SelectionLength > 0) return null;
        if (text.Length != 1 || !char.IsAsciiLetterUpper(text[0])) return null;

        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        int caretCol = Editor.CaretOffset - line.Offset;
        string lineText = document.GetText(line);

        // Never inside a string literal - a shifted keystroke there is raw PETSCII content being
        // typed directly, not a keyword abbreviation (the tokenizer never keyword-scans strings).
        bool inString = false;
        for (int i = 0; i < caretCol; i++)
            if (lineText[i] == '"') inString = !inString;
        if (inString) return null;

        char shiftedLower = char.ToLowerInvariant(text[0]);
        int prefixAvailable = Math.Min(caretCol, BasicKeywordAbbreviations.MaxLength - 1);
        for (int prefixLen = prefixAvailable; prefixLen >= 0; prefixLen--)
        {
            string candidate = lineText.Substring(caretCol - prefixLen, prefixLen) + shiftedLower;
            if (BasicKeywordAbbreviations.ToKeyword.ContainsKey(candidate))
                return shiftedLower.ToString();
        }

        return null;
    }

    // Real C64 keyboard behavior for a letter key: unshifted (no Shift, Shift Lock/Caps Lock off)
    // produces the normal uppercase-looking glyph (this app's default); shifted (Shift held, or
    // Caps Lock on - a real C64's Shift Lock physically latches Shift down, unlike a PC's Caps
    // Lock, so either one alone is enough, and there's no cancel-out when both are active at
    // once) produces the C64 graphic character occupying that key's shifted position. Internally
    // that graphic glyph is just the letter's lower case ASCII byte - PetsciiGlyphGenerator
    // already renders it as the correct C64 ROM glyph, so no separate PETSCII mapping is needed
    // here. Non-letters (digits, punctuation) are unaffected either way.
    private static string ApplyC64Shift(string text)
    {
        if (text.Length != 1 || !char.IsAsciiLetter(text[0])) return text.ToUpperInvariant();

        bool shifted = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || KeyboardLockKeys.IsCapsLockOn;
        return shifted ? char.ToLowerInvariant(text[0]).ToString() : char.ToUpperInvariant(text[0]).ToString();
    }

    private void Editor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm) return; // preserve pasted case for assembly

        string text = (string)e.DataObject.GetData(DataFormats.Text);
        e.DataObject = new DataObject(text.ToUpperInvariant());
    }

    #endregion

    #region Ghost Text / Completion

    /// <summary>
    /// Scans backward from the caret to find the start and text of the identifier being typed.
    /// Returns an empty string when the caret is not inside an identifier.
    /// </summary>
    private (int offset, string word) GetWordBeforeCaret()
    {
        int caretOffset = Editor.CaretOffset;
        var doc = Editor.Document;
        int pos = caretOffset - 1;

        while (pos >= 0)
        {
            char c = doc.GetCharAt(pos);
            if (char.IsLetterOrDigit(c) || c == '$' || c == '#')
                pos--;
            else
                break;
        }

        int wordStart = pos + 1;
        if (wordStart >= caretOffset) return (caretOffset, string.Empty);

        string word = doc.GetText(wordStart, caretOffset - wordStart).ToUpperInvariant();
        return (wordStart, word);
    }

    /// <summary>
    /// Updates (or clears) the inline ghost-text suggestion based on what is before the caret.
    /// Called on every caret position change so the suggestion stays in sync while typing.
    /// </summary>
    private void UpdateGhostText()
    {
        // Completion doesn't make sense for a read-only tab (e.g. a disassembly listing) - there's
        // nothing for the user to be typing towards.
        if (Editor.IsReadOnly)
        {
            ClearGhostText();
            return;
        }

        // Don't update while a popup completion window is open.
        if (_completionWindow != null)
        {
            ClearGhostText();
            return;
        }

        // REM makes the rest of the physical line a comment - no keyword/variable completion
        // makes sense once the caret is past the REM keyword itself, same "everything after REM
        // isn't code" rule every diagnostic/analysis pass in this codebase already follows.
        if (ViewModel.ActiveTab?.Language == EditorLanguage.Basic)
        {
            var caretLine = Editor.Document.GetLineByOffset(Editor.CaretOffset);
            string caretLineText = Editor.Document.GetText(caretLine);
            if (BasicDiagnostics.TryParseLineNumber(caretLineText, out _, out _, out _, out int codeStart))
            {
                string code = caretLineText[codeStart..];
                int remStart = BasicDiagnostics.FindTopLevelRemStart(code);
                if (remStart < code.Length &&
                    BasicTokens.TryMatchKeyword(code, remStart, BasicTokens.WordKeywordsLongestFirst, out string remKeyword))
                {
                    int caretCol = Editor.CaretOffset - caretLine.Offset - codeStart;
                    if (caretCol >= remStart + remKeyword.Length)
                    {
                        ClearGhostText();
                        return;
                    }
                }
            }
        }

        var (_, word) = GetWordBeforeCaret();

        if (string.IsNullOrEmpty(word) || word.All(char.IsDigit))
        {
            ClearGhostText();
            return;
        }

        // Only suggest when the caret is at the true end of the identifier being typed - if a
        // word character immediately follows (e.g. clicking between "PR" and "INT" in "PRINT"),
        // a completion here would show suggested text overlapping what's already there.
        int caretOffset = Editor.CaretOffset;
        if (caretOffset < Editor.Document.TextLength)
        {
            char next = Editor.Document.GetCharAt(caretOffset);
            if (char.IsLetterOrDigit(next) || next == '$' || next == '#')
            {
                ClearGhostText();
                return;
            }
        }

        var matches = GetCompletionMatches(word);
        if (matches.Count == 0)
        {
            ClearGhostText();
            return;
        }

        // matches is already sorted alphabetically; index 0 is the primary suggestion.
        var best = matches[0];
        string snippet = best.Snippet; // cursor marker already stripped

        // Ghost text is the part of the snippet that the user hasn't typed yet.
        string ghost = snippet.Length > word.Length ? snippet[word.Length..] : string.Empty;

        _ghostRenderer.GhostText = ghost;
        _ghostRenderer.InvalidateVisual();
    }

    private void ClearGhostText()
    {
        if (string.IsNullOrEmpty(_ghostRenderer.GhostText)) return;
        _ghostRenderer.GhostText = string.Empty;
        _ghostRenderer.InvalidateVisual();
    }

    /// <summary>
    /// Accepts the currently displayed ghost-text suggestion.
    /// </summary>
    private void AcceptGhostCompletion()
    {
        var (wordStart, word) = GetWordBeforeCaret();
        if (string.IsNullOrEmpty(word)) return;

        var matches = GetCompletionMatches(word);
        if (matches.Count == 0) return;

        ClearGhostText();
        var segment = new EditorSegment(wordStart, Editor.CaretOffset - wordStart);
        matches[0].Complete(Editor.TextArea, segment, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the CompletionWindow popup with all keywords matching the current prefix.
    /// Used by Ctrl+Space; also handles the edge case where no ghost text is available.
    /// </summary>
    private void OpenCompletionPopup()
    {
        if (Editor.IsReadOnly) return;

        _completionWindow?.Close();
        ClearGhostText();

        var (wordStart, word) = GetWordBeforeCaret();

        List<KeywordCompletionData> matches = string.IsNullOrEmpty(word) || word.All(char.IsDigit)
            ? GetAllCompletionItems()
            : GetCompletionMatches(word);

        if (matches.Count == 0) return;

        _completionWindow = new CompletionWindow(Editor.TextArea) { StartOffset = wordStart };
        foreach (var item in matches)
            _completionWindow.CompletionList.CompletionData.Add(item);
        if (!string.IsNullOrEmpty(word))
            _completionWindow.CompletionList.SelectItem(word);
        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
    }

    // Returns completion entries matching prefix from whichever provider matches the active
    // tab's language, so ghost text/Ctrl+Space never mixes BASIC keywords and assembly mnemonics.
    private List<KeywordCompletionData> GetCompletionMatches(string prefix) =>
        ViewModel.ActiveTab?.Language == EditorLanguage.Asm
            ? AsmCompletionProvider.GetMatches(prefix)
            : BasicCompletionProvider.GetMatches(prefix);

    // Returns every completion entry for the active tab's language, alphabetically - used for
    // the Ctrl+Space "no prefix typed yet" fallback.
    private List<KeywordCompletionData> GetAllCompletionItems() =>
        ViewModel.ActiveTab?.Language == EditorLanguage.Asm
            ? [.. AsmCompletionProvider.AllItems.OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase)]
            : [.. BasicCompletionProvider.AllItems.OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase)];

    #endregion
}
