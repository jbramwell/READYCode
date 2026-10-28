// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Folding;
using ReadyCode.Editor;
using ReadyCode.Models;

namespace ReadyCode.Views;

// The editor tab bar: activating, switching, closing and reopening tabs, and the tab context menu.
public partial class MainWindow
{
    #region Tab Management

    private void ActivateTab(EditorTab? tab)
    {
        // Persist outgoing tab's caret, scroll position, and collapsed-fold state. A hex tab was
        // never bound onto Editor in the first place (see below), so its position must be read
        // from HexEditor instead - reading Editor's here would just capture whatever unrelated
        // tab it was last bound to.
        if (ViewModel.ActiveTab != null && !ReferenceEquals(ViewModel.ActiveTab, tab))
        {
            if (ViewModel.ActiveTab.IsHexMode)
            {
                ViewModel.ActiveTab.CaretOffset = HexEditor.SelectedOffset;
                ViewModel.ActiveTab.ScrollOffsetY = HexEditor.VerticalScrollOffset;
            }
            else
            {
                ViewModel.ActiveTab.CaretOffset = Editor.CaretOffset;
                ViewModel.ActiveTab.ScrollOffsetY = Editor.VerticalOffset;
                SaveFoldingState(ViewModel.ActiveTab);
            }
        }

        // FoldingManager is bound to whichever document was on the TextArea at Install time and
        // must be uninstalled before the shared Editor control is rebound to a different one -
        // there's no "reinstall"/rebind API, so a fresh manager is created per activation below.
        // Already null whenever a hex tab is/was active (never installed for one), so this no-ops
        // correctly without needing its own IsHexMode check.
        if (_foldingManager != null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

        _activatingTab = true;
        ViewModel.ActiveTab = tab;

        // Visibility is set here, before LoadBytes/Focus below, rather than after the mode
        // branch as it used to be - HexEditor.Focus() (and the layout LoadBytes's freshly
        // assigned ItemsSource needs) can't do much useful work while HexEditor is still
        // Visibility.Collapsed, since a collapsed subtree doesn't participate in layout or fire
        // Loaded events for its elements at all.
        bool isHexMode = tab?.IsHexMode == true;
        bool isCompareMode = tab?.IsCompareMode == true;
        EditorContainer.Visibility = tab != null && !isHexMode && !isCompareMode ? Visibility.Visible : Visibility.Collapsed;
        HexEditor.Visibility = isHexMode ? Visibility.Visible : Visibility.Collapsed;
        CompareControl.Visibility = isCompareMode ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateImage.Visibility = tab != null ? Visibility.Collapsed : Visibility.Visible;

        bool isDisassembly = tab?.IsDisassemblyMode == true;
        DisassemblyToolbar.Visibility = isDisassembly ? Visibility.Visible : Visibility.Collapsed;
        Editor.IsReadOnly = isDisassembly;
        _asmLineNumberMargin.LineAddresses = tab?.DisassemblyLineAddresses;
        _asmLineNumberMargin.InvalidateMeasure();
        _asmLineNumberMargin.InvalidateVisual();

        if (tab != null && tab.IsHexMode)
        {
            HexEditor.LoadBytes(tab.RawBytes!, tab.CaretOffset, tab.ScrollOffsetY, tab.UndoStack);
            HexEditor.Focus();
            HideLanguagePanelsForHexMode();

            // A hex tab has no BASIC variables or ASM symbols of its own - clear out whatever
            // the previously active tab left behind rather than leaving stale entries on screen.
            // Also stop the debounce timer: RunVariableIndex/RunAsmSymbolIndex already guard
            // against a hex-mode ActiveTab, but a tick already in flight from the previous tab's
            // activation would otherwise still fire ~300ms from now and redo the same work.
            _diagnosticsTimer.Stop();
            ViewModel.Variables.Clear();
            ViewModel.ConstantSymbols.Symbols.Clear();
            ViewModel.LabelSymbols.Symbols.Clear();
            _lastAsmResult = null;
        }
        else if (tab != null && tab.IsCompareMode)
        {
            CompareControl.LoadResult(tab.CompareResult!, tab.CompareIsUnified, tab.CompareIgnoreWhitespace);
            HideLanguagePanelsForHexMode();

            // A compare tab has no BASIC variables or ASM symbols of its own - same reasoning as
            // the hex-mode branch above.
            _diagnosticsTimer.Stop();
            ViewModel.Variables.Clear();
            ViewModel.ConstantSymbols.Symbols.Clear();
            ViewModel.LabelSymbols.Symbols.Clear();
            _lastAsmResult = null;
        }
        else if (tab != null)
        {
            // Assigning Editor.Document raises AvalonEdit's TextChanged event (a new document
            // counts as the visible text changing), so the guard must still be up here -
            // otherwise Editor_TextChanged marks the freshly activated tab as modified.
            Editor.Document = tab.Document;
            ApplyLineTransformersForLanguage(tab.Language);
            Editor.CaretOffset = Math.Min(tab.CaretOffset, tab.Document.TextLength);
            Editor.ScrollToVerticalOffset(tab.ScrollOffsetY);
            Editor.Focus();

            // The Find bar doesn't close when switching tabs, but _findMatches holds offsets
            // computed against whichever document was active when the search last ran - stale
            // offsets applied to this tab's (unrelated) document land on arbitrary text instead
            // of an actual match. Re-running the search against the newly bound document keeps
            // them in sync, same as FindBar.SearchChanged already does while typing.
            if (FindBar.Visibility == Visibility.Visible)
                UpdateFindMatches();

            if (IsCodeFoldingEnabled(tab.Language))
            {
                _foldingManager = FoldingManager.Install(Editor.TextArea);
                RunFolding();
                foreach (var fs in _foldingManager.AllFoldings)
                    fs.IsFolded = tab.CollapsedFoldStartOffsets.Contains(fs.StartOffset);
            }

            // Assigning Editor.Document above already armed the debounce timer via
            // Editor_TextChanged - run the full analysis for the newly active tab right now
            // instead of waiting ~300ms for it (so squiggles/symbols/gutter addresses are correct
            // immediately), then cancel that now-redundant pending tick so the exact same work
            // doesn't just repeat itself a moment later.
            RunDocumentAnalysis();
            _diagnosticsTimer.Stop();
        }
        else
        {
            ViewModel.Variables.Clear();
            ViewModel.ConstantSymbols.Symbols.Clear();
            ViewModel.LabelSymbols.Symbols.Clear();
            _lastAsmResult = null;
        }

        _activatingTab = false;

        _tabSwitching = true;
        TabBar.SelectedItem = tab;
        _tabSwitching = false;

        // Selecting an item doesn't reliably auto-scroll it into view with a plain horizontal
        // StackPanel as the tab bar's items panel, so scroll explicitly - covers activation from
        // the overflow menu, keyboard shortcuts, etc., not just clicking a visible tab.
        if (tab != null)
            TabBar.ScrollIntoView(tab);
    }

    // Hides the language-specific right-panel toggles (BASIC Keywords / ASM Mnemonics) and closes
    // whichever is open - a hex-mode tab is neither BASIC nor Asm, so neither panel applies.
    // Mirrors ApplyLineTransformersForLanguage's own panel-toggle logic for the same two panels.
    private void HideLanguagePanelsForHexMode()
    {
        BasicKeywordsToggle.Visibility = Visibility.Collapsed;
        AsmKeywordsToggle.Visibility = Visibility.Collapsed;
        if (BasicKeywordsToggle.IsChecked == true) { BasicKeywordsToggle.IsChecked = false; CloseRightPanel(BasicKeywordsPanel); }
        if (AsmKeywordsToggle.IsChecked == true) { AsmKeywordsToggle.IsChecked = false; CloseRightPanel(AsmKeywordsPanel); }
        ViewModel.IsRightPanelOpen = RightPanelToggles.Any(t => t.Toggle.IsChecked == true);
    }

    // Swaps which set of syntax colorizers is attached to the editor, and which font it displays,
    // for the given language, so switching tabs never leaves BASIC transformers running over
    // assembly text (or vice versa) - e.g. RemCommentColorizer's un-bounded "REM" scan would
    // wrongly comment out the rest of a line containing a label like "TREMOR:".
    // FindHighlightColorizer is language-agnostic and stays active either way. Also shows a
    // sequential editor-line-number gutter for Asm only - BASIC already shows its own line
    // numbers as ordinary source text, so a duplicate gutter would look redundant there.
    private void ApplyLineTransformersForLanguage(EditorLanguage language)
    {
        var transformers = Editor.TextArea.TextView.LineTransformers;
        transformers.Clear();

        if (language == EditorLanguage.Asm)
        {
            transformers.Add(_asmMnemonicColorizer);
            transformers.Add(_asmNumberLiteralColorizer);
            transformers.Add(_asmLabelColorizer);
            transformers.Add(_asmCommentColorizer);

            if (!Editor.TextArea.LeftMargins.Contains(_asmLineNumberMargin))
                Editor.TextArea.LeftMargins.Insert(0, _asmLineNumberMargin);

            Editor.TextArea.LeftMargins.Remove(_breakpointMargin);
        }
        else
        {
            transformers.Add(_lineNumberColorizer);
            transformers.Add(_keywordColorizer);
            transformers.Add(_numberLiteralColorizer);
            transformers.Add(_stringLiteralColorizer);
            transformers.Add(_dataLiteralColorizer);
            transformers.Add(_remCommentColorizer);

            if (language == EditorLanguage.Basic && !Editor.TextArea.LeftMargins.Contains(_breakpointMargin))
                Editor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
            else if (language != EditorLanguage.Basic)
                Editor.TextArea.LeftMargins.Remove(_breakpointMargin);

            Editor.TextArea.LeftMargins.Remove(_asmLineNumberMargin);
        }

        transformers.Add(_findHighlightColorizer);

        bool isAsm = language == EditorLanguage.Asm;

        // BASIC (.bas and .prg alike) always uses the PETSCII font with glyph substitution on,
        // since either can contain real PETSCII control/graphics characters; assembly always
        // uses Consolas with substitution off, since assembly/plain source must never be
        // reinterpreted as PETSCII bytes.
        Editor.FontFamily = isAsm ? EditorFonts.Consolas : EditorFonts.Petscii;
        _petsciiGlyphGenerator.IsAsmMode = isAsm;
        Editor.TextArea.TextView.Redraw();
        VariablesPanel.Visibility = isAsm ? Visibility.Collapsed : Visibility.Visible;
        SymbolsPanel.Visibility = isAsm ? Visibility.Visible : Visibility.Collapsed;

        // Minify/Prettify/Renumber are BASIC-only text transforms that would corrupt assembly
        // source (same reasoning as HasNonEmptyBasicActiveTab) - hidden entirely for Asm rather
        // than just disabled, with Format Code (the Asm counterpart) shown in their place.
        MinifyMenuItem.Visibility = isAsm ? Visibility.Collapsed : Visibility.Visible;
        PrettifyMenuItem.Visibility = isAsm ? Visibility.Collapsed : Visibility.Visible;
        RenumberMenuItem.Visibility = isAsm ? Visibility.Collapsed : Visibility.Visible;
        FormatMenuItem.Visibility = isAsm ? Visibility.Visible : Visibility.Collapsed;

        // The BASIC Keywords / ASM Mnemonics activity-bar buttons only make sense for their
        // matching language - hide whichever doesn't apply, closing its panel first if it was
        // the one currently open (setting IsChecked here doesn't raise Click, so the panel
        // must be closed explicitly rather than relying on AsmKeywordsToggle_Click etc).
        BasicKeywordsToggle.Visibility = isAsm ? Visibility.Collapsed : Visibility.Visible;
        AsmKeywordsToggle.Visibility = isAsm ? Visibility.Visible : Visibility.Collapsed;
        if (isAsm && BasicKeywordsToggle.IsChecked == true)
        {
            BasicKeywordsToggle.IsChecked = false;
            CloseRightPanel(BasicKeywordsPanel);
            ViewModel.IsRightPanelOpen = RightPanelToggles.Any(t => t.Toggle.IsChecked == true);
        }
        else if (!isAsm && AsmKeywordsToggle.IsChecked == true)
        {
            AsmKeywordsToggle.IsChecked = false;
            CloseRightPanel(AsmKeywordsPanel);
            ViewModel.IsRightPanelOpen = RightPanelToggles.Any(t => t.Toggle.IsChecked == true);
        }

        // BASIC and assembly have their own column guide column - re-apply now that
        // ViewModel.ActiveTab's language has changed, rather than leaving the previous tab's
        // ruler position on screen until some unrelated trigger (Settings close, etc.) refreshes it.
        UpdateColumnRulerPosition();
    }

    // Applies the active tab's Upper Active/Inactive mode to the shared glyph generator and
    // redraws - a pure re-render, since the mode only changes which glyph a byte displays as,
    // never the byte itself (see PetsciiGlyphGenerator.IsUpperCaseModeActive). Reached via
    // ViewModel.IsUpperCaseModeActive's PropertyChanged, which fires whenever the active tab
    // changes or the active tab's own mode is toggled - see ShiftModeIndicator_Click.
    private void ApplyUpperCaseMode()
    {
        _petsciiGlyphGenerator.IsUpperCaseModeActive = ViewModel.IsUpperCaseModeActive;
        Editor.TextArea.TextView.Redraw();
    }

    // Cycles the active tab forward (right) or backward (left) through ViewModel.OpenTabs,
    // wrapping around at either end. A no-op with 0 or 1 tabs open.
    private void SwitchToAdjacentTab(bool forward)
    {
        var tabs = ViewModel.OpenTabs;
        if (tabs.Count < 2 || ViewModel.ActiveTab == null) return;

        int currentIndex = tabs.IndexOf(ViewModel.ActiveTab);
        if (currentIndex < 0) return;

        int nextIndex = (currentIndex + (forward ? 1 : -1) + tabs.Count) % tabs.Count;
        ActivateTab(tabs[nextIndex]);
    }

    private void TabBar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tabSwitching) return;
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is EditorTab tab)
            ActivateTab(tab);
    }

    // Shows the "Show Open Tabs" overflow button only once tabs actually extend past the visible
    // tab bar - called on every ScrollChanged (window resize, tabs added/closed/renamed all
    // change the ScrollViewer's ExtentWidth/ViewportWidth) and once immediately after hookup.
    private void UpdateTabListButtonVisibility(ScrollViewer tabBarScrollViewer)
    {
        TabListButton.Visibility = tabBarScrollViewer.ExtentWidth > tabBarScrollViewer.ViewportWidth
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // Shows every open tab in a dropdown so tabs scrolled out of view at either end of the tab
    // bar are still reachable in one click, rather than needing to scroll the tab strip itself.
    private void TabListButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var tab in ViewModel.OpenTabs)
        {
            var menuItem = new MenuItem
            {
                Header = tab.IsModified ? $"{tab.FileName} •" : tab.FileName,
                IsCheckable = true,
                IsChecked = ReferenceEquals(tab, ViewModel.ActiveTab),
            };
            menuItem.Click += (_, _) => ActivateTab(tab);
            menu.Items.Add(menuItem);
        }
        menu.PlacementTarget = (Button)sender;
        menu.IsOpen = true;
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        var tab = (sender as Button)?.DataContext as EditorTab;
        if (tab != null) CloseTab(tab);
        e.Handled = true;
    }

    // Returns false if the user cancelled; callers that close multiple tabs should stop on false.
    private bool CloseTab(EditorTab tab)
    {
        if (tab.IsModified)
        {
            // Activate the tab so the user can see what they're being asked about
            ActivateTab(tab);
            var result = MessageBox.Show(
                $"Save changes to \"{tab.FileName}\"?",
                "READYCode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes && !SaveTabWithDialog(tab)) return false;
        }

        bool isActiveTab = ReferenceEquals(ViewModel.ActiveTab, tab);
        _closedTabHistory.Add(new ClosedTabSnapshot(
            tab.FilePath,
            tab.Document.Text,
            tab.IsModified,
            isActiveTab ? Editor.CaretOffset : tab.CaretOffset,
            isActiveTab ? Editor.VerticalOffset : tab.ScrollOffsetY));
        if (_closedTabHistory.Count > _maxClosedTabHistory)
            _closedTabHistory.RemoveAt(0);

        int idx = ViewModel.OpenTabs.IndexOf(tab);
        ViewModel.OpenTabs.Remove(tab);

        // ActivateTab(tab) below already refreshes the Errors panel for the *new* active tab
        // (via RunDocumentAnalysis -> RunDiagnostics), but ActivateTab(null) - the last tab
        // closing - doesn't run diagnostics at all, which would otherwise leave this closed
        // tab's rows stranded in the list. Covers both branches unconditionally rather than
        // duplicating the null-check.
        RefreshErrorsPanel();

        if (ViewModel.OpenTabs.Count == 0)
        {
            ActivateTab(null);
        }
        else
        {
            ActivateTab(ViewModel.OpenTabs[Math.Min(idx, ViewModel.OpenTabs.Count - 1)]);
        }
        return true;
    }

    // Restores the most recently closed tab (Ctrl+Shift+T), including any unsaved content it
    // had at close time. No-op if nothing has been closed yet this session.
    private void ReopenClosedTab()
    {
        if (_closedTabHistory.Count == 0) return;

        ClosedTabSnapshot snapshot = _closedTabHistory[^1];
        _closedTabHistory.RemoveAt(_closedTabHistory.Count - 1);

        var tab = new EditorTab { FilePath = snapshot.FilePath };
        if (snapshot.FilePath != null)
        {
            tab.Language = LanguageClassifier.Classify(snapshot.FilePath);
            tab.Kind = FileClassifier.Classify(snapshot.FilePath, isFolder: false);
        }
        tab.Document.Text = snapshot.Text;
        tab.CaretOffset = Math.Min(snapshot.CaretOffset, tab.Document.TextLength);
        tab.ScrollOffsetY = snapshot.ScrollOffsetY;
        ViewModel.OpenTabs.Add(tab);
        ActivateTab(tab);
        tab.IsModified = snapshot.WasModified; // reset any spurious change event from document setup
    }

    #endregion

    #region Tab Context Menu

    private void TabContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var tab = (menu.PlacementTarget as ListBoxItem)?.DataContext as EditorTab;
        if (tab == null) return;

        int idx = ViewModel.OpenTabs.IndexOf(tab);
        ((MenuItem)menu.Items[1]).IsEnabled = ViewModel.OpenTabs.Count > 1;
        ((MenuItem)menu.Items[2]).IsEnabled = idx < ViewModel.OpenTabs.Count - 1;
        ((MenuItem)menu.Items[3]).IsEnabled = ViewModel.OpenTabs.Any(t => !t.IsModified);
    }

    private EditorTab? GetContextMenuTab(object sender)
    {
        var menu = (sender as MenuItem)?.Parent as ContextMenu;
        return (menu?.PlacementTarget as ListBoxItem)?.DataContext as EditorTab;
    }

    private void TabContextClose_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab != null) CloseTab(tab);
    }

    private void TabContextCloseOthers_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        foreach (var t in ViewModel.OpenTabs.Where(t => !ReferenceEquals(t, tab)).ToList())
            if (!CloseTab(t)) break;
        if (ViewModel.OpenTabs.Contains(tab)) ActivateTab(tab);
    }

    private void TabContextCloseToRight_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        int idx = ViewModel.OpenTabs.IndexOf(tab);
        foreach (var t in ViewModel.OpenTabs.Skip(idx + 1).ToList())
            if (!CloseTab(t)) break;
        if (ViewModel.OpenTabs.Contains(tab)) ActivateTab(tab);
    }

    private void TabContextCloseSaved_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in ViewModel.OpenTabs.Where(t => !t.IsModified).ToList())
            CloseTab(t);
    }

    private void TabContextCloseAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in ViewModel.OpenTabs.ToList())
            if (!CloseTab(t)) break;
    }

    private void TabContextCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab != null) Clipboard.SetText(tab.FullPath);
    }

    #endregion
}
