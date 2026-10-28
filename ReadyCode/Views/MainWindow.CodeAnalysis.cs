// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Folding;
using ReadyCode.Assembler;
using ReadyCode.Diagnostics;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

// Background code analysis and the panels it feeds: diagnostics and the Errors panel, code folding, and the Variables / Symbols panels (including rename).
public partial class MainWindow
{
    #region Diagnostics / Errors Panel

    // Runs every debounced analysis pass for the active document in one go: diagnostics, folding,
    // and (language-specific) variable/symbol indexing. For an assembly tab, Asm6502Assembler's
    // full two-pass assemble is genuinely expensive on a large file, so it's run exactly once here
    // and threaded into both RunDiagnostics and RunAsmSymbolIndex, rather than each independently
    // re-assembling the same unchanged text (previously the dominant cost behind a slow load/tab
    // switch on a several-hundred-line assembly file). Called by the debounce timer as the user
    // types, and directly by ActivateTab right after switching tabs - see the comment there for
    // why that call site also stops the timer immediately afterward.
    private void RunDocumentAnalysis()
    {
        var document = Editor.Document;
        AssemblyResult? asmResult = document != null && ViewModel.ActiveTab?.Language == EditorLanguage.Asm
            ? new Asm6502Assembler().Assemble(
                document.Text, ViewModel.Settings.AsmOutputMode == "Standalone", (ushort)ViewModel.Settings.AsmDefaultOriginAddress)
            : null;

        RunDiagnostics(asmResult);
        RunFolding();
        RunVariableIndex();
        RunAsmSymbolIndex(asmResult);
        RefreshBreakpointMargin();
    }

    // Jumps to an Errors-tab row's location, activating its (possibly inactive) tab first - same
    // "jump to a place in a possibly-inactive tab" shape as DebugCallStackList_MouseDoubleClick
    // above, just off a row's own Tab/Line instead of the debug tab/call-stack frame. Also selects
    // the diagnostic's exact offset/length span - the same text the Errors squiggle underlines -
    // rather than only placing the caret at the start of the line.
    private void ErrorsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ErrorsGrid.SelectedItem is not ErrorListRow row) return;

        if (!ReferenceEquals(ViewModel.ActiveTab, row.Tab))
            ActivateTab(row.Tab);

        MoveCaretToDocumentLine(row.Line);
        Editor.Select(row.Offset, row.Length);

        // MoveCaretToDocumentLine already scrolled the line into view vertically, but it puts the
        // caret at column 1 first - on a long line, the selection Select() just made can still
        // sit off-screen horizontally (or even vertically, if it wraps past the line MoveCaret
        // scrolled to). BringCaretToView re-scrolls both axes to the caret's actual position now
        // (the selection's end), same as every other jump-to-selection spot in this file.
        Editor.TextArea.Caret.BringCaretToView();

        // Select() moves the caret, which fires Editor_CaretPositionChanged -> UpdateGhostText -
        // if the selected span happens to be a complete keyword/function name, that can show
        // stray ghost text at the caret (see NavigateToCurrentMatch's identical precaution).
        ClearGhostText();
    }

    // Re-analyzes the active document and refreshes the squiggle underlines: for BASIC, undefined
    // GOTO/GOSUB/THEN targets, unmatched FOR/NEXT, unterminated strings, and duplicate line
    // numbers; for assembly, whatever Asm6502Assembler reports as errors.
    private void RunDiagnostics(AssemblyResult? asmResult = null)
    {
        bool isAsm = ViewModel.ActiveTab?.Language == EditorLanguage.Asm;
        _currentDiagnostics = Editor.Document != null && ViewModel.Settings.EnableLinting && !Editor.IsReadOnly
            ? (isAsm ? AsmDiagnostics.Analyze(Editor.Document.Text, asmResult) : BasicDiagnostics.Analyze(Editor.Document.Text))
            : Array.Empty<EditorDiagnostic>();
        _errorSquiggleRenderer.SetDiagnostics(_currentDiagnostics);
        Editor.TextArea.TextView.Redraw();

        // Stash onto the tab so it survives being backgrounded (see EditorTab.Diagnostics), and
        // refresh the cross-tab Errors panel to match - every debounced re-analysis and every
        // tab activation both flow through here, so this is the one place that needs to know.
        if (ViewModel.ActiveTab != null) ViewModel.ActiveTab.Diagnostics = _currentDiagnostics;
        RefreshErrorsPanel();
    }

    // Rebuilds the Errors panel's row list from every open tab's cached Diagnostics (not just
    // the active tab) - the reason EditorTab.Diagnostics exists at all. Gated on EnableLinting
    // directly here (rather than relying on every open tab's Diagnostics having already been
    // cleared) so toggling linting off immediately empties the panel even for background tabs
    // that haven't been reactivated since.
    private void RefreshErrorsPanel()
    {
        ViewModel.ErrorListRows.Clear();

        if (ViewModel.Settings.EnableLinting)
        {
            foreach (var tab in ViewModel.OpenTabs)
            {
                foreach (var diag in tab.Diagnostics)
                {
                    var documentLine = tab.Document.GetLineByOffset(Math.Min(diag.Offset, tab.Document.TextLength));

                    // BasicDiagnostics only ever raises a diagnostic on a line that already
                    // parsed a leading line number (see AnalyzeLine's early-out), so this
                    // succeeds for every BASIC row; null for an assembly tab, which has no
                    // BASIC line numbering at all.
                    int? basicLineNumber = null;
                    if (tab.Language == EditorLanguage.Basic &&
                        BasicDiagnostics.TryParseLineNumber(tab.Document.GetText(documentLine), out int n, out _, out _, out _))
                        basicLineNumber = n;

                    ViewModel.ErrorListRows.Add(new ErrorListRow
                    {
                        Message = diag.Message,
                        FileName = tab.FileName,
                        FilePath = tab.FilePath,
                        Line = documentLine.LineNumber,
                        BasicLineNumber = basicLineNumber,
                        Tab = tab,
                        Offset = diag.Offset,
                        Length = diag.Length,
                    });
                }
            }
        }

        bool lintingOff = !ViewModel.Settings.EnableLinting;
        ErrorsEmptyStateText.Text = lintingOff
            ? "Linting is disabled - enable it in Settings to see errors here."
            : "No issues found.";
        ErrorsEmptyStateText.Visibility = ViewModel.ErrorListRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Auto-activates the Errors panel (opening it if needed) after a tokenize/save/deploy
    // action, if the affected tab has any diagnostic - deliberately NOT wired into the
    // debounce-timer live-typing path (RunDocumentAnalysis), so squiggles/Errors-tab contents
    // stay continuously current while typing without ever yanking focus away from the editor
    // mid-edit. Applied to every successful Save (not just the PrgConverter-tokenized branch)
    // and every C64U/VICE Load/Run, since an assembly error blocking a deploy deserves the same
    // visibility as a BASIC tokenizer gap - see §6.3/§10.3.
    private void ActivateErrorsPanelIfDiagnostics(EditorTab tab)
    {
        // A background tab's Diagnostics can't be stale (its Document can't change while it
        // isn't the one loaded into the editor - see EditorTab.Diagnostics) - only the active
        // tab needs a fresh pass, to cover the case of saving within the 300ms debounce window.
        if (ReferenceEquals(tab, ViewModel.ActiveTab))
            RunDiagnostics();
        if (tab.Diagnostics.Count > 0)
            ViewModel.IsErrorsPanelActive = true;
    }

    private void ErrorsSeverityToggle_Click(object sender, RoutedEventArgs e) => ApplyErrorsFilter();

    private void ErrorsSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyErrorsFilter();

    // Applies the Errors tab's severity-toggle and search-box state as a live filter over the
    // same default CollectionView ErrorsGrid's ItemsSource binding already uses (rather than
    // introducing a second, separately-filtered list) - the first use of ICollectionView.Filter
    // in this app; nothing else here has needed a client-side collection filter before.
    // WPF raises SelectionChanged synchronously the moment InitializeComponent() assigns
    // ErrorsScopeCombo's XAML-declared SelectedIndex="0" - before InitializeComponent finishes
    // parsing the rest of the tree, so later-declared fields this handler touches
    // (ErrorsSeverityErrorToggle, ErrorsSearchBox) are still null, crashing with a
    // NullReferenceException before the window ever opens. IsLoaded is false for that
    // startup-driven firing (it only becomes true once the whole window has loaded), so this
    // guard skips it - harmlessly, since the CollectionView's Filter being unset already shows
    // everything, exactly matching what applying the default Open-Files/Errors-shown/no-search
    // state would produce anyway.
    private void ErrorsScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyErrorsFilter();
    }

    private void ApplyErrorsFilter()
    {
        bool showErrors = ErrorsSeverityErrorToggle.IsChecked == true;
        string search = ErrorsSearchBox.Text;
        bool activeTabOnly = ErrorsScopeCombo.SelectedItem is ComboBoxItem { Content: "Active Tab" };

        // ActiveTab is read fresh inside the predicate (not captured here) so switching tabs
        // while this scope is selected keeps filtering correctly - RefreshErrorsPanel's
        // Clear()+re-Add() on every tab activation re-runs this Filter for every row against
        // whatever tab is active by then, with no extra wiring needed for that case.
        CollectionViewSource.GetDefaultView(ViewModel.ErrorListRows).Filter = obj =>
        {
            if (obj is not ErrorListRow row) return false;
            if (!showErrors) return false; // no other severity exists today - see §10.2
            if (activeTabOnly && !ReferenceEquals(row.Tab, ViewModel.ActiveTab)) return false;
            return string.IsNullOrWhiteSpace(search)
                || row.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.FileName.Contains(search, StringComparison.OrdinalIgnoreCase);
        };
    }

    #endregion

    #region Code Folding

    // Recomputes fold regions on the current (not reinstalled) manager. Deliberately doesn't
    // touch IsFolded - FoldingManager.UpdateFoldings already preserves it for sections that still
    // match after a same-tab edit, which is what should govern live-typing recomputation. Only
    // ActivateTab's fresh-manager path needs to explicitly restore collapsed state.
    private void RunFolding()
    {
        if (_foldingManager == null || Editor.Document == null) return;
        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm)
            _asmFoldingStrategy.UpdateFoldings(_foldingManager, Editor.Document);
        else
            _foldingStrategy.UpdateFoldings(_foldingManager, Editor.Document);
    }

    // Snapshots which folds are currently collapsed onto the tab being switched away from, so
    // ActivateTab can restore it next time this tab becomes active.
    private void SaveFoldingState(EditorTab tab)
    {
        tab.CollapsedFoldStartOffsets.Clear();
        if (_foldingManager == null) return;
        foreach (var fs in _foldingManager.AllFoldings)
            if (fs.IsFolded) tab.CollapsedFoldStartOffsets.Add(fs.StartOffset);
    }

    // Reacts to the Code Analysis settings (Linting/Code folding) having just been changed in the
    // Settings dialog: installs or uninstalls the FoldingManager to match, mirroring ActivateTab's
    // own install/uninstall lifecycle, and re-runs diagnostics so squiggles appear/clear
    // immediately. Linting has no persistent state to unwind - RunDiagnostics already clears
    // _currentDiagnostics when the setting is off.
    // BASIC and assembly have their own code folding toggle (folding REM/FOR-NEXT blocks vs.
    // runs of ";" comments) - null (no active tab) falls back to the BASIC setting, harmless
    // since both call sites already guard on there being a document to fold.
    private bool IsCodeFoldingEnabled(EditorLanguage? language) =>
        language == EditorLanguage.Asm ? ViewModel.Settings.AsmEnableCodeFolding : ViewModel.Settings.EnableCodeFolding;

    private void ApplyCodeAnalysisSettings()
    {
        EditorTab? activeTab = ViewModel.ActiveTab;
        bool foldingEnabled = IsCodeFoldingEnabled(activeTab?.Language);

        if (foldingEnabled && _foldingManager == null && Editor.Document != null)
        {
            _foldingManager = FoldingManager.Install(Editor.TextArea);
            RunFolding();
            if (activeTab != null)
                foreach (var fs in _foldingManager.AllFoldings)
                    fs.IsFolded = activeTab.CollapsedFoldStartOffsets.Contains(fs.StartOffset);
        }
        else if (!foldingEnabled && _foldingManager != null)
        {
            if (activeTab != null) SaveFoldingState(activeTab);
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

        RunDiagnostics();
    }

    #endregion

    #region Variables / Symbols Panels

    // Re-scans the active document for every variable's read/write occurrences, diffing the
    // result into ViewModel.Variables rather than replacing it wholesale - reusing an existing
    // VariableInfo instance for a name that's still present preserves its IsExpanded state (WPF's
    // TreeViewItem container ties expansion to the bound object's identity), so the tree doesn't
    // collapse everything the user just expanded on every debounce tick while typing.
    // BASIC-specific - see RunAsmSymbolIndex for assembly's equivalent (labels/constants).
    private void RunVariableIndex()
    {
        var variables = ViewModel.Variables;
        var document = Editor.Document;
        if (document == null || ViewModel.ActiveTab?.IsHexMode == true || ViewModel.ActiveTab?.Language == EditorLanguage.Asm)
        {
            variables.Clear();
            return;
        }

        // Variables, DEF FN functions, and a DEF FN's own parameter are all separate namespaces in
        // real BASIC (a variable, a function, and an unrelated function's same-named parameter can
        // all legally share a name), so each tracked entry is keyed by (Name, IsFunction,
        // LocalToFunction) rather than Name alone - otherwise same-named entries would collide in
        // the tree below. See VariableReference.LocalToFunction for why a DEF FN parameter counts
        // as its own scope rather than joining the global variable of the same name.
        var seenKeys = new HashSet<(string Name, bool IsFunction, string? LocalToFunction)>();

        var byName = VariableCrossReference.Analyze(document.Text)
            .GroupBy(r => (r.Name, r.LocalToFunction));

        foreach (var group in byName)
        {
            seenKeys.Add((group.Key.Name, false, group.Key.LocalToFunction));

            var existing = GetOrCreateVariableEntry(variables, group.Key.Name, isFunction: false, group.Key.LocalToFunction);
            existing.Occurrences.Clear();
            foreach (var reference in group.OrderBy(r => r.Offset))
            {
                var line = document.GetLineByOffset(reference.Offset);
                TryGetBasicLineNumber(document, line.LineNumber, out int basicLineNumber);
                existing.Occurrences.Add(new VariableOccurrenceInfo(line.LineNumber, basicLineNumber, reference.IsWrite));
            }
        }

        var byFunctionName = VariableCrossReference.AnalyzeFunctions(document.Text)
            .GroupBy(r => r.Name, StringComparer.Ordinal);

        foreach (var group in byFunctionName)
        {
            seenKeys.Add((group.Key, true, null));

            var existing = GetOrCreateVariableEntry(variables, group.Key, isFunction: true, localToFunction: null);
            existing.Occurrences.Clear();
            foreach (var reference in group.OrderBy(r => r.Offset))
            {
                var line = document.GetLineByOffset(reference.Offset);
                TryGetBasicLineNumber(document, line.LineNumber, out int basicLineNumber);
                existing.Occurrences.Add(new VariableOccurrenceInfo(
                    line.LineNumber, basicLineNumber, reference.IsDefinition, isFunction: true));
            }
        }

        for (int i = variables.Count - 1; i >= 0; i--)
            if (!seenKeys.Contains((variables[i].Name, variables[i].IsFunction, variables[i].LocalToFunction))) variables.RemoveAt(i);
    }

    // Finds the existing VariableInfo for (name, isFunction, localToFunction) in the (Name-sorted)
    // Variable Explorer tree, or inserts a new one in sorted position - shared by both the
    // variable and function indexing passes in RunVariableIndex above.
    private static VariableInfo GetOrCreateVariableEntry(
        ObservableCollection<VariableInfo> variables, string name, bool isFunction, string? localToFunction)
    {
        var existing = variables.FirstOrDefault(v =>
            v.Name == name && v.IsFunction == isFunction && v.LocalToFunction == localToFunction);
        if (existing != null) return existing;

        existing = new VariableInfo(name, isFunction, localToFunction);
        int insertAt = 0;
        while (insertAt < variables.Count && string.CompareOrdinal(variables[insertAt].Name, name) < 0)
            insertAt++;
        variables.Insert(insertAt, existing);
        return existing;
    }

    // Re-scans the active document for every label/constant's definition/reference occurrences,
    // diffing the result into ViewModel.ConstantSymbols/LabelSymbols exactly like RunVariableIndex
    // diffs into ViewModel.Variables (same IsExpanded-preservation reasoning). Also caches the
    // underlying AssemblyResult in _lastAsmResult so the hover tooltip can show resolved
    // label/constant values without re-assembling on every mouse move. Assembly-specific - see
    // RunVariableIndex for BASIC's equivalent.
    private void RunAsmSymbolIndex(AssemblyResult? asmResult = null)
    {
        var constants = ViewModel.ConstantSymbols.Symbols;
        var labels = ViewModel.LabelSymbols.Symbols;
        var document = Editor.Document;
        if (document == null || ViewModel.ActiveTab?.IsHexMode == true || ViewModel.ActiveTab?.Language != EditorLanguage.Asm)
        {
            constants.Clear();
            labels.Clear();
            _lastAsmResult = null;
            return;
        }

        asmResult ??= new Asm6502Assembler().Assemble(
            document.Text, ViewModel.Settings.AsmOutputMode == "Standalone", (ushort)ViewModel.Settings.AsmDefaultOriginAddress);
        _lastAsmResult = asmResult;

        UpdateAsmGutterAddresses(asmResult);

        // Reuses the parse Assemble() already did (see RunDocumentAnalysis) instead of
        // re-parsing the same source a second time just to index it.
        var byName = AsmSymbolIndex.Analyze(asmResult.ParsedLines)
            .GroupBy(o => o.Name, StringComparer.Ordinal);

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byName)
        {
            seenNames.Add(group.Key);

            bool isConstant = group.Any(o => o.Kind == AsmSymbolKind.ConstantDefinition);
            var targetGroup = isConstant ? constants : labels;
            var otherGroup = isConstant ? labels : constants;

            // A symbol reclassified since the last reparse (e.g. a constant redefined further
            // down as a label) moves to its new group instead of leaving a stale duplicate
            // behind in the old one - reusing the existing AsmSymbolInfo instance rather than
            // creating a new one also preserves its IsExpanded state across the move.
            var existing = targetGroup.FirstOrDefault(s => s.Name == group.Key);
            if (existing == null)
            {
                var stray = otherGroup.FirstOrDefault(s => s.Name == group.Key);
                if (stray != null) otherGroup.Remove(stray);

                existing = stray ?? new AsmSymbolInfo(group.Key);
                int insertAt = 0;
                while (insertAt < targetGroup.Count && string.CompareOrdinal(targetGroup[insertAt].Name, group.Key) < 0)
                    insertAt++;
                targetGroup.Insert(insertAt, existing);
            }

            existing.TypeBadge = isConstant ? "CONST" : "LABEL";
            existing.ValueText = isConstant
                ? (asmResult.Constants.TryGetValue(group.Key, out int constValue) ? FormatAsmSymbolValue(constValue) : null)
                : (asmResult.Labels.TryGetValue(group.Key, out ushort labelAddress) ? FormatAsmSymbolValue(labelAddress) : null);

            existing.Occurrences.Clear();
            foreach (var occurrence in group.OrderBy(o => o.LineNumber))
                existing.Occurrences.Add(new AsmSymbolOccurrenceInfo(occurrence.LineNumber, occurrence.Kind));
        }

        RemoveStaleSymbols(constants, seenNames);
        RemoveStaleSymbols(labels, seenNames);
    }

    // Shows real memory addresses in the gutter instead of sequential line numbers for a regular
    // (non-disassembly) assembly tab, but only once its source has an explicit ".org" - without
    // one, "address" would just mean wherever the auto-generated BASIC loader stub (or the
    // standalone-output default origin) happens to land the code, an implementation detail rather
    // than something the user chose. Falls back to plain line numbers while the source doesn't
    // assemble cleanly, since there's no valid address data then. A tab that already carries its
    // own DisassemblyLineAddresses snapshot - a live IsDisassemblyMode tab, or a freshly opened
    // "Disassemble file" tab - always wins instead; this leaves those tabs alone entirely, letting
    // this recomputed-by-reassembly view take back over automatically once that snapshot is
    // cleared (see Editor_TextChanged, for a "Disassemble file" tab that's since been edited).
    private void UpdateAsmGutterAddresses(AssemblyResult asmResult)
    {
        var tab = ViewModel.ActiveTab;
        if (tab == null || tab.IsDisassemblyMode || tab.DisassemblyLineAddresses != null) return;

        Dictionary<int, ushort>? lineAddresses = null;
        if (asmResult.Success && asmResult.HasExplicitOrigin)
        {
            lineAddresses = new Dictionary<int, ushort>();
            foreach (var entry in asmResult.ListingEntries)
                lineAddresses[entry.LineNumber] = entry.Address;
        }

        _asmLineNumberMargin.LineAddresses = lineAddresses;
        _asmLineNumberMargin.InvalidateMeasure();
        _asmLineNumberMargin.InvalidateVisual();
    }

    private static void RemoveStaleSymbols(ObservableCollection<AsmSymbolInfo> symbols, HashSet<string> seenNames)
    {
        for (int i = symbols.Count - 1; i >= 0; i--)
            if (!seenNames.Contains(symbols[i].Name)) symbols.RemoveAt(i);
    }

    private static string FormatAsmSymbolValue(int value) => $"${value:X4}";

    // Double-clicking an occurrence (line number) row jumps to that line - mirrors
    // VariablesTree_MouseDoubleClick below.
    private void SymbolsTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SymbolsTree.SelectedItem is AsmSymbolOccurrenceInfo occurrence)
            MoveCaretToDocumentLine(occurrence.DocumentLineNumber);
    }

    // Double-clicking an occurrence (line number) row jumps to that line - a single click just
    // selects the row, matching how the folder tree only opens a file on double-click too.
    private void VariablesTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VariablesTree.SelectedItem is VariableOccurrenceInfo occurrence)
            MoveCaretToDocumentLine(occurrence.DocumentLineNumber);
    }

    private void VariablesTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && VariablesTree.SelectedItem is VariableInfo variable)
        {
            BeginInlineRenameVariable(variable);
            e.Handled = true;
        }
    }

    private void VariableOccurrenceContextGoToLine_Click(object sender, RoutedEventArgs e)
    {
        if (VariablesTree.SelectedItem is VariableOccurrenceInfo occurrence)
            MoveCaretToDocumentLine(occurrence.DocumentLineNumber);
    }

    private void VariableContextRename_Click(object sender, RoutedEventArgs e)
    {
        if (VariablesTree.SelectedItem is VariableInfo variable)
            BeginInlineRenameVariable(variable);
    }

    private void BeginInlineRenameVariable(VariableInfo variable)
    {
        variable.IsRenaming = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            var tvi = FindTreeViewItem(VariablesTree, variable);
            if (tvi == null) return;
            var box = FindVisualChild<TextBox>(tvi, "RenameBox");
            if (box == null) return;
            box.Focus();
            box.SelectAll();
        });
    }

    private void VariableRenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)      { CommitVariableRename((TextBox)sender); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelVariableRename((TextBox)sender); e.Handled = true; }
    }

    private void VariableRenameBox_LostFocus(object sender, RoutedEventArgs e)
        => CommitVariableRename((TextBox)sender);

    private void CancelVariableRename(TextBox box)
    {
        if (box.DataContext is VariableInfo variable) variable.IsRenaming = false;
    }

    private void CommitVariableRename(TextBox box)
    {
        if (box.DataContext is not VariableInfo variable || !variable.IsRenaming) return;
        variable.IsRenaming = false;

        string newName = box.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(newName) || newName == variable.Name) return;

        if (variable.IsFunction)
        {
            if (!IsValidFunctionName(newName))
            {
                ViewModel.SetStatus($"\"{newName}\" isn't a valid BASIC function name.", StatusType.Warning);
                return;
            }
            RenameFunction(variable.Name, newName);
            return;
        }

        if (!IsValidVariableName(newName))
        {
            ViewModel.SetStatus($"\"{newName}\" isn't a valid BASIC variable name.", StatusType.Warning);
            return;
        }

        RenameVariable(variable.Name, newName, variable.LocalToFunction);
    }

    // A variable name is a letter, then letters/digits, with an optional trailing $ or % - same
    // rule the analyzer and hover tooltip already use. Also rejects a name that IS a real BASIC
    // keyword outright (e.g. "FOR") - the tokenizer would crunch it as that keyword rather than
    // treat it as an identifier, unlike a name that merely contains one as a substring (e.g. "SCORE").
    private static bool IsValidVariableName(string name)
    {
        int end = name.Length;
        if (end > 0 && (name[end - 1] == '$' || name[end - 1] == '%')) end--;
        if (end == 0 || !char.IsLetter(name[0])) return false;

        for (int i = 1; i < end; i++)
            if (!char.IsLetterOrDigit(name[i])) return false;

        return !BasicTokens.TryMatchKeyword(name, 0, BasicTokens.WordKeywordsLongestFirst, out string keyword)
            || keyword.Length != end;
    }

    // Same shape as IsValidVariableName, but without the trailing $/% allowance - real BASIC's
    // DEF FN only supports numeric functions, and VariableCrossReference.AnalyzeFunctions never
    // captures a $ or % as part of a function name (see EnumerateFnOccurrences), so neither should
    // a name this dialog accepts.
    private static bool IsValidFunctionName(string name)
    {
        if (name.Length == 0 || !char.IsLetter(name[0])) return false;

        for (int i = 1; i < name.Length; i++)
            if (!char.IsLetterOrDigit(name[i])) return false;

        return !BasicTokens.TryMatchKeyword(name, 0, BasicTokens.WordKeywordsLongestFirst, out string keyword)
            || keyword.Length != name.Length;
    }

    // Renames every occurrence of oldName (as currently grouped in the Variable Explorer) to
    // newName throughout the active document, as one grouped undo step, then refreshes the
    // Variable Explorer immediately (rather than waiting for the debounce timer) to reflect it.
    // localToFunction scopes the rename to one DEF FN's parameter (see
    // VariableReference.LocalToFunction) rather than the global variable of the same name -
    // passing null (the default) renames the global as before.
    private void RenameVariable(string oldName, string newName, string? localToFunction = null)
    {
        var document = Editor.Document;
        if (document == null) return;

        var occurrences = VariableCrossReference.Analyze(document.Text)
            .Where(r => r.Name == oldName && r.LocalToFunction == localToFunction)
            .OrderByDescending(r => r.Offset) // back-to-front so earlier offsets stay valid
            .ToList();
        if (occurrences.Count == 0) return;

        document.BeginUpdate();
        try
        {
            foreach (var occurrence in occurrences)
                document.Replace(occurrence.Offset, occurrence.Length, newName);
        }
        finally
        {
            document.EndUpdate();
        }

        RunVariableIndex();
        string scopeSuffix = localToFunction == null ? "" : $", local to FN {localToFunction}";
        ViewModel.SetStatus($"Renamed {oldName} to {newName} ({occurrences.Count} occurrence{(occurrences.Count == 1 ? "" : "s")}{scopeSuffix}).");
    }

    // RenameVariable's counterpart for DEF FN functions - renames every occurrence (the
    // definition and every call site) of oldName to newName. Keyed off
    // VariableCrossReference.AnalyzeFunctions rather than Analyze, so this only ever touches FN
    // occurrences - functions and variables are separate namespaces in real BASIC (see
    // RunVariableIndex), so a same-named variable elsewhere in the document is left untouched.
    private void RenameFunction(string oldName, string newName)
    {
        var document = Editor.Document;
        if (document == null) return;

        var occurrences = VariableCrossReference.AnalyzeFunctions(document.Text)
            .Where(r => r.Name == oldName)
            .OrderByDescending(r => r.Offset) // back-to-front so earlier offsets stay valid
            .ToList();
        if (occurrences.Count == 0) return;

        document.BeginUpdate();
        try
        {
            foreach (var occurrence in occurrences)
                document.Replace(occurrence.Offset, occurrence.Length, newName);
        }
        finally
        {
            document.EndUpdate();
        }

        RunVariableIndex();
        ViewModel.SetStatus($"Renamed FN {oldName} to FN {newName} ({occurrences.Count} occurrence{(occurrences.Count == 1 ? "" : "s")}).");
    }

    // Shows/hides the Variable Explorer section, reclaiming its space for Explorer/C64U above it
    // when hidden (FolderTreeRow becomes "*" instead of its persisted fixed height) and restoring
    // the persisted split when shown again. Clamped defensively (not just relying on the
    // RowDefinition's own MinHeight) so a bad persisted value can't leave the Variable Explorer
    // effectively inaccessible after a restart.
    private void ApplyVariableExplorerVisibility()
    {
        bool show = ViewModel.Settings.ShowVariableExplorer;

        VariablesPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ExplorerRowSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        // MinHeight still reserves space for a Collapsed row's content, leaving a blank gap -
        // must be cleared too, not just Height.
        VariablesRow.MinHeight = show ? 60 : 0;
        VariablesRow.Height = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        FolderTreeRow.Height = show
            ? new GridLength(Math.Clamp(ViewModel.Settings.ExplorerFolderTreeHeight, 60, 600))
            : new GridLength(1, GridUnitType.Star);
    }

    #endregion
}
