// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Rendering;
using ReadyCode.Debugger;
using ReadyCode.Interop;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

// Breakpoints and the debug panel: the breakpoint gutter, live-session sync, the call stack, and viewing / editing variables while debugging.
public partial class MainWindow
{
    #region Breakpoints

    // Rebuilds the active tab's line address table and recomputes which document lines the
    // breakpoint gutter should mark, from BreakpointStore's BASIC-line-number-keyed entries. A
    // no-op margin (nothing shown) for anything that isn't an ordinary BASIC text tab.
    private void RefreshBreakpointMargin()
    {
        var tab = ViewModel.ActiveTab;
        if (tab is not { Language: EditorLanguage.Basic, IsHexMode: false, IsCompareMode: false, IsDisassemblyMode: false })
        {
            _activeTabLineAddressTable = null;
            _breakpointMargin.EnabledBreakpointLines = new HashSet<int>();
            _breakpointMargin.DisabledBreakpointLines = new HashSet<int>();
            _breakpointMargin.InvalidateVisual();
            return;
        }

        var lineTable = BasicLineAddressTable.Build(tab.Document.Text);
        _activeTabLineAddressTable = lineTable;

        string fileKey = tab.FilePath ?? tab.FileName;
        var enabledLines = new HashSet<int>();
        foreach (ushort basicLine in ViewModel.BreakpointStore.EnabledLinesFor(fileKey))
            if (lineTable.BasicLineToDocumentLine.TryGetValue(basicLine, out int documentLine))
                enabledLines.Add(documentLine);

        var disabledLines = new HashSet<int>();
        foreach (ushort basicLine in ViewModel.BreakpointStore.DisabledLinesFor(fileKey))
            if (lineTable.BasicLineToDocumentLine.TryGetValue(basicLine, out int documentLine))
                disabledLines.Add(documentLine);

        _breakpointMargin.EnabledBreakpointLines = enabledLines;
        _breakpointMargin.DisabledBreakpointLines = disabledLines;
        _breakpointMargin.InvalidateVisual();
    }

    // F9 / "Toggle Breakpoint": same effect as clicking the gutter, but at the caret's line.
    private void ToggleBreakpointAtCaret() => ToggleBreakpointAtDocumentLine(Editor.TextArea.Caret.Line);

    // Handles a breakpoint-gutter click: resolves the document line to a BASIC line number via
    // the active tab's line address table, toggles it in the store, refreshes the gutter, and -
    // if a debug session is currently attached to this same tab - updates VICE's live checkpoint
    // immediately rather than waiting for the next debug session to pick it up.
    private async void ToggleBreakpointAtDocumentLine(int documentLine)
    {
        var tab = ViewModel.ActiveTab;
        if (tab is not { Language: EditorLanguage.Basic }) return;

        var lineTable = _activeTabLineAddressTable ?? BasicLineAddressTable.Build(tab.Document.Text);
        if (!lineTable.DocumentLineToBasicLine.TryGetValue(documentLine, out ushort basicLine))
            return; // blank/no-code line - not a valid breakpoint location

        string fileKey = tab.FilePath ?? tab.FileName;
        var addedBreakpoint = ViewModel.BreakpointStore.Toggle(fileKey, basicLine);
        RefreshBreakpointMargin();
        ViewModel.PersistBreakpoints();

        if (ViewModel.DebugSession is { } session && ReferenceEquals(ViewModel.DebugTab, tab))
            await SyncBreakpointToLiveSessionAsync(session, basicLine, addedBreakpoint != null);
    }

    // Applies a single breakpoint add/remove to a live debug session (VICE or C64U - see
    // IDebugSession) and reports any failure - shared by every place a breakpoint change needs to
    // reach whichever session is currently attached: ToggleBreakpointAtDocumentLine,
    // Breakpoint_PropertyChanged, and DeleteAllBreakpointsAsync's per-line loop.
    private async Task SyncBreakpointToLiveSessionAsync(IDebugSession session, ushort basicLine, bool enabled)
    {
        try
        {
            if (enabled)
                await session.SetLineBreakpointAsync(basicLine);
            else
                await session.RemoveBreakpointAsync(basicLine);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Failed to update breakpoint on the live session: {ex.Message}", StatusType.Error);
        }
    }

    // Ctrl+F9 / "Enable/Disable Breakpoint": flips IsEnabled on the breakpoint already at the
    // caret's line, if one exists there - unlike Toggle Breakpoint (F9), this never adds or
    // removes one. Reuses Breakpoint_PropertyChanged (already wired to every breakpoint's
    // IsEnabled) for persisting, refreshing the gutter, and syncing a live debug session, so
    // nothing further is needed here beyond flipping the flag.
    private void ToggleBreakpointEnabledAtCaret()
    {
        var tab = ViewModel.ActiveTab;
        if (tab is not { Language: EditorLanguage.Basic }) return;

        var lineTable = _activeTabLineAddressTable ?? BasicLineAddressTable.Build(tab.Document.Text);
        if (!lineTable.DocumentLineToBasicLine.TryGetValue(Editor.TextArea.Caret.Line, out ushort basicLine))
            return;

        string fileKey = tab.FilePath ?? tab.FileName;
        var existing = ViewModel.BreakpointStore.Find(fileKey, basicLine);
        if (existing == null) return; // nothing set at this line to enable/disable

        existing.IsEnabled = !existing.IsEnabled;
    }

    // Ctrl+Shift+F9 / "Delete All Breakpoints": clears every breakpoint, across every file, not
    // just the active tab - matches Visual Studio's own "Delete All Breakpoints" scope. Captures
    // the active debug tab's enabled lines before clearing, since BreakpointStore.Clear() alone
    // only touches the in-memory model, not whatever a live session still has armed.
    private async Task DeleteAllBreakpointsAsync()
    {
        var session = ViewModel.DebugSession;
        var debugTab = ViewModel.DebugTab;
        List<ushort> liveLines = new();
        if (session != null && debugTab != null)
        {
            string debugTabFileKey = debugTab.FilePath ?? debugTab.FileName;
            liveLines = ViewModel.BreakpointStore.EnabledLinesFor(debugTabFileKey).ToList();
        }

        ViewModel.BreakpointStore.Clear();
        RefreshBreakpointMargin();
        ViewModel.PersistBreakpoints();

        if (session == null) return;
        foreach (ushort line in liveLines)
            await SyncBreakpointToLiveSessionAsync(session, line, enabled: false);
    }

    // Ctrl+F10 / "Run to Cursor": resolves the caret's line to a BASIC line number and hands off
    // to MainViewModel.RunToLineAsync, which owns the actual session-level logic (arming a
    // temporary breakpoint there if needed, then continuing).
    private async Task RunToCursorAsync()
    {
        var tab = ViewModel.ActiveTab;
        if (tab is not { Language: EditorLanguage.Basic }) return;

        var lineTable = _activeTabLineAddressTable ?? BasicLineAddressTable.Build(tab.Document.Text);
        if (!lineTable.DocumentLineToBasicLine.TryGetValue(Editor.TextArea.Caret.Line, out ushort basicLine))
        {
            ViewModel.SetStatus("Run to Cursor requires the caret to be on a line with code.", StatusType.Error);
            return;
        }

        await ViewModel.RunToLineAsync(basicLine);
    }

    // Pushes the halted line (if any) into the current-execution-line renderer, activating the
    // debug session's own tab first if the user was looking at a different one, and scrolling/
    // placing the caret there - mirrors MoveCaretToDocumentLine's scroll behavior without
    // stealing focus away from wherever the user was (e.g. the debug panel).
    private void ApplyDebugCurrentLine()
    {
        int? line = ViewModel.DebugCurrentDocumentLine;
        _debugCurrentLineRenderer.CurrentLine = line;

        if (line is { } documentLine)
        {
            if (!ReferenceEquals(ViewModel.ActiveTab, ViewModel.DebugTab) && ViewModel.DebugTab != null)
                ActivateTab(ViewModel.DebugTab);

            if (Editor.Document != null && documentLine <= Editor.Document.LineCount)
                Editor.ScrollToLine(documentLine);
        }

        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        Editor.TextArea.TextView.Redraw();
    }

    // Keeps each breakpoint's PropertyChanged subscription in sync as the store's contents
    // change (gutter clicks add/remove; ReplaceAll on project load swaps the whole set).
    private void BreakpointStore_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Breakpoint breakpoint in e.OldItems)
                breakpoint.PropertyChanged -= Breakpoint_PropertyChanged;

        if (e.NewItems != null)
            foreach (Breakpoint breakpoint in e.NewItems)
                breakpoint.PropertyChanged += Breakpoint_PropertyChanged;
    }

    // Reacts to a breakpoint's IsEnabled changing (the Breakpoints grid's "On" checkbox, or any
    // future path that flips it) - persists the change, refreshes the gutter dot, and - if a
    // debug session is attached to this same file - updates the live target too, since a debug
    // session (VICE or C64U - see IDebugSession) tracks its own independent set of active
    // breakpoint lines that a plain IsEnabled flip on the model doesn't touch by itself.
    private async void Breakpoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Breakpoint.IsEnabled)) return;
        if (sender is not Breakpoint breakpoint) return;

        ViewModel.PersistBreakpoints();
        RefreshBreakpointMargin();

        if (ViewModel.DebugSession is not { } session || ViewModel.DebugTab == null) return;
        string debugTabFileKey = ViewModel.DebugTab.FilePath ?? ViewModel.DebugTab.FileName;
        if (!string.Equals(breakpoint.FilePath, debugTabFileKey, StringComparison.OrdinalIgnoreCase)) return;

        await SyncBreakpointToLiveSessionAsync(session, breakpoint.LineNumber, breakpoint.IsEnabled);
    }

    #endregion

    #region Debug Panel

    // The debug panel's three inner tabs (Variables/Breakpoints/Call Stack), paired with the
    // content element each one shows - mirrors RightPanelToggles' shape for the same reasons.
    private IEnumerable<(ToggleButton Toggle, UIElement Content, string Key)> DebugPanelTabs => new (ToggleButton, UIElement, string)[]
    {
        (DebugVariablesTabToggle,   DebugVariablesTree,  "Variables"),
        (DebugBreakpointsTabToggle, DebugBreakpointsGrid, "Breakpoints"),
        (DebugCallStackTabToggle,   DebugCallStackList,   "CallStack"),
    };

    private void ActivateDebugPanelTab(string key)
    {
        var target = DebugPanelTabs.FirstOrDefault(t => t.Key == key);
        if (target.Toggle == null) target = DebugPanelTabs.First();

        foreach (var (toggle, content, _) in DebugPanelTabs)
        {
            bool isTarget = ReferenceEquals(toggle, target.Toggle);
            toggle.IsChecked = isTarget;
            content.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
        }

        ViewModel.Settings.ActiveDebugPanelTab = target.Key;
    }

    private void DebugPanelTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string key })
            ActivateDebugPanelTab(key);
    }

    // Jumps to a breakpoint's line in its file, if that file is already open in a tab - doesn't
    // open it from disk if it isn't, keeping this a pure navigation shortcut rather than a
    // second file-opening code path to keep in sync with the real one.
    private void DebugBreakpointsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DebugBreakpointsGrid.SelectedItem is not Breakpoint breakpoint) return;

        var tab = ViewModel.OpenTabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, breakpoint.FilePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.FileName, breakpoint.FilePath, StringComparison.OrdinalIgnoreCase));
        if (tab == null) return;

        ActivateTab(tab);
        var lineTable = BasicLineAddressTable.Build(tab.Document.Text);
        if (lineTable.BasicLineToDocumentLine.TryGetValue(breakpoint.LineNumber, out int documentLine))
            MoveCaretToDocumentLine(documentLine);
    }

    // Re-applies the debugged tab's current Lower Case Mode to every already-populated node in the
    // Variables tree (both top-level and any already-expanded array's children), so toggling the
    // mode for the tab being debugged (Edit > Lower Case Mode, or the status bar's Shift Badge)
    // updates a string variable's display immediately instead of waiting for the next
    // breakpoint/step to refresh. A node created by a later refresh picks up the current mode
    // directly instead (see MainViewModel.RefreshDebugVariablesAndCallStackAsync/LoadArrayChildrenAsync).
    private void RefreshDebugVariablesDisplayMode()
    {
        bool isUpperCaseModeActive = ViewModel.DebugTab?.IsUpperCaseModeActive ?? true;
        foreach (var node in ViewModel.DebugVariables)
        {
            node.IsUpperCaseModeActive = isUpperCaseModeActive;
            foreach (var child in node.Children)
                child.IsUpperCaseModeActive = isUpperCaseModeActive;
        }
    }

    // Double-clicking a leaf's Value cell starts inline editing (see BeginInlineEditDebugVariable)
    // - an array root's own row has no value of its own to edit (see DebugVariableNode.IsArray),
    // only its individual elements do, once expanded.
    private void DebugVariablesTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DebugVariablesTree.SelectedItem is not DebugVariableNode { IsArray: false } node) return;
        BeginInlineEditDebugVariable(node);
    }

    // Fetches this array's contents the first time it's expanded - or re-expanded after a refresh
    // reset ChildrenLoaded for the new stop - see MainViewModel.LoadArrayChildrenAsync. Wired via
    // DebugVariableTreeViewItemStyle's EventSetter, so this fires for every TreeViewItem in the
    // tree; only an array root (IsArray) ever actually has anything to load.
    private void DebugVariablesTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem { DataContext: DebugVariableNode { IsArray: true } node }) return;
        _ = ViewModel.LoadArrayChildrenAsync(node);
    }

    private void BeginInlineEditDebugVariable(DebugVariableNode node)
    {
        if (node.Variable == null) return; // e.g. a string still mid-resolution - nothing to edit yet

        node.IsEditing = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            var tvi = FindTreeViewItem(DebugVariablesTree, node);
            if (tvi == null) return;
            var box = FindVisualChild<TextBox>(tvi, "valueEditBox");
            if (box == null) return;

            // The box's Text binds Mode=OneTime to DebugVariableNode.ValueDisplayText - safe to
            // overwrite once here, before focusing, since that binding never re-pushes. For a
            // string, rebuild the box's text directly from the RAW PETSCII bytes via
            // PetsciiScreenCodeMap.ToDisplayText's mode-aware overload - the same per-byte display
            // rule live typing uses in DebugValueEditBox_PreviewTextInput below - so editing shows
            // the raw content unquoted, in whatever case/graphics the debugged tab's keyboard
            // emulation is currently in, matching what actually typing this string right now would
            // show (ValueDisplayText itself can't be used here - it's quoted, for the read-only
            // display).
            if (node.Variable is { Type: BasicVariableType.String, Value: ResolvedStringValue resolved })
            {
                bool isUpperCaseModeActive = ViewModel.DebugTab?.IsUpperCaseModeActive ?? true;
                box.Text = PetsciiScreenCodeMap.ToDisplayText(resolved.Text, isUpperCaseModeActive);
            }

            box.Focus();
            box.SelectAll();
        });
    }

    // Intercepts live typing into the Value edit box for a string variable, applying the exact
    // same real-hardware keyboard behavior the main code editor already does (see
    // MainWindow.ApplyC64Shift and PetsciiGlyphGenerator.ConstructElement, which this mirrors):
    // the raw PETSCII byte a key produces is the SAME uppercase-or-lowercase ASCII value
    // regardless of the debugged tab's Lower Case Mode - only Shift/Caps Lock decides that, same
    // as a real C64 keyboard matrix - and it's the CURRENT display mode that then decides whether
    // that byte shows as a plain letter (case-swapped in Lower Case Mode) or its PETSCII graphic
    // (Upper Active mode's Shift+letter positions). Only applies while editing a string - a
    // float/integer edit box's plain-numeric input is left untouched.
    private void DebugValueEditBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.DataContext is not DebugVariableNode { Variable.Type: BasicVariableType.String }) return;

        e.Handled = true;

        bool isUpperCaseModeActive = ViewModel.DebugTab?.IsUpperCaseModeActive ?? true;
        string insertText = ApplyDebugValueShift(e.Text, isUpperCaseModeActive);

        int start = box.SelectionStart;
        int length = box.SelectionLength;
        box.Text = box.Text.Remove(start, length).Insert(start, insertText);

        int caretOffset = start + insertText.Length;
        box.CaretIndex = caretOffset;
        box.Select(caretOffset, 0);
    }

    // Non-letters (digits, punctuation) pass through untouched, same as ApplyC64Shift. A letter's
    // raw byte is decided purely by Shift/Caps Lock, mode-independent - then
    // PetsciiScreenCodeMap.ToDisplayText's mode-aware overload decides how that one-char "string"
    // is actually shown, given the debugged tab's current mode (same call BeginInlineEditDebugVariable
    // makes for the whole pre-existing value above, and PetsciiGlyphGenerator.ConstructElement
    // makes per-byte for the main code editor).
    private static string ApplyDebugValueShift(string text, bool isUpperCaseModeActive)
    {
        if (text.Length != 1 || !char.IsAsciiLetter(text[0])) return text;

        bool shifted = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || KeyboardLockKeys.IsCapsLockOn;
        char rawByte = shifted ? char.ToLowerInvariant(text[0]) : char.ToUpperInvariant(text[0]);
        return PetsciiScreenCodeMap.ToDisplayText(rawByte.ToString(), isUpperCaseModeActive);
    }

    // Swaps every ASCII letter's case (A<->a) across a whole string - used only to reverse
    // BeginInlineEditDebugVariable/DebugValueEditBox_PreviewTextInput's Lower Case Mode display
    // swap before writing a committed edit back (CommitDebugValueEdit): in that mode, ANY letter
    // shown is already the opposite of its real PETSCII case (see ToDebugDisplayChar), and no
    // graphics glyphs ever appear in it to worry about, so a plain whole-string case flip is the
    // exact, self-inverse reversal - unlike Upper Active mode, where VariableWriteBack.EncodeString's
    // own PetsciiScreenCodeMap.FromDisplayText call already correctly reverses any PUA graphics
    // glyphs back to their real byte with no case flip needed.
    private static string SwapAsciiLetterCase(string text)
    {
        char[] chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (char.IsAsciiLetter(c))
                chars[i] = char.IsAsciiLetterUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
        }
        return new string(chars);
    }

    private void DebugValueEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)       { CommitDebugValueEdit((TextBox)sender); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelDebugValueEdit((TextBox)sender); e.Handled = true; }
    }

    private void DebugValueEditBox_LostFocus(object sender, RoutedEventArgs e)
        => CommitDebugValueEdit((TextBox)sender);

    private void CancelDebugValueEdit(TextBox box)
    {
        if (box.DataContext is DebugVariableNode node) node.IsEditing = false;
    }

    // Commits an inline value edit: encodes the typed text via VariableWriteBack (float/integer/
    // string, per the variable's type), writes it to the live machine, then re-reads the whole
    // variable table so the tree shows the true resulting value rather than trusting the typed
    // text verbatim - matters most for a string, which silently space-pads to its original
    // length. Guarded on IsEditing (mirroring CommitVariableRename's identical guard) so the
    // LostFocus that CancelDebugValueEdit's own IsEditing=false triggers doesn't re-commit
    // afterward.
    private async void CommitDebugValueEdit(TextBox box)
    {
        if (box.DataContext is not DebugVariableNode { IsEditing: true } node) return;
        node.IsEditing = false;

        if (node.Variable is not { } variable) return;
        if (ViewModel.DebugSession is not { } session) return;

        // Writing memory while the target is running (not halted) would race the live program's
        // own reads/writes of the same bytes - only safe while stopped, same as every other live
        // debugger interaction (stepping, etc.).
        if (!ViewModel.IsDebugStopped)
        {
            ViewModel.SetStatus($"Can't update {variable.Name} while running - pause first.", StatusType.Error);
            return;
        }

        string enteredText = box.Text;

        // Reverse BeginInlineEditDebugVariable's display-only case swap before encoding, so what
        // gets written back is real PETSCII case regardless of the debugged tab's current keyboard
        // emulation mode - the swap is its own inverse, so this is the exact same operation.
        if (variable.Type == BasicVariableType.String && ViewModel.DebugTab is { IsUpperCaseModeActive: false })
            enteredText = SwapAsciiLetterCase(enteredText);

        try
        {
            var (address, bytes) = VariableWriteBack.Encode(variable, enteredText);
            await session.WriteMemoryAsync(address, bytes);
            ViewModel.SetStatus($"{variable.Name} updated.", StatusType.Info);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Failed to update {variable.Name}: {ex.Message}", StatusType.Error);
        }

        await ViewModel.RefreshDebugVariablesAndCallStackAsync();
    }

    // Jumps to a GOSUB frame's return line in the debug session's own tab (a call stack is only
    // ever shown for the file currently being debugged).
    private void DebugCallStackList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DebugCallStackList.SelectedItem is not GosubFrame frame || frame.DocumentLine is not int documentLine) return;
        if (ViewModel.DebugTab == null) return;

        if (!ReferenceEquals(ViewModel.ActiveTab, ViewModel.DebugTab))
            ActivateTab(ViewModel.DebugTab);

        MoveCaretToDocumentLine(documentLine);
    }

    #endregion
}
