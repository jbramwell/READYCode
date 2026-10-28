// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using ReadyCode.Assembler;
using ReadyCode.Debugger;
using ReadyCode.Diagnostics;
using ReadyCode.Editor;
using ReadyCode.Models;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

/// <summary>
/// The main application window: hosts the BASIC code editor, folder explorer, tab bar,
/// Quick Keys / PETSCII panels, find/replace bar, and all of the File/Edit/View/C64U commands.
/// </summary>
public partial class MainWindow : Window
{
    // Snapshot of a closed tab's state, kept in _closedTabHistory so Ctrl+Shift+T can restore
    // it exactly - including unsaved content, since the tab may never have matched disk.
    private sealed record ClosedTabSnapshot(
        string? FilePath, string Text, bool WasModified, int CaretOffset, double ScrollOffsetY);

    #region Private Fields

    // Custom clipboard format that stores raw UTF-16LE bytes so PETSCII control
    // characters in the C0 (U+0001–U+001F) and C1 (U+0080–U+009F) Unicode ranges
    // survive the Windows clipboard round-trip unharmed.  Windows CF_TEXT encoding
    // maps C1 code points through the system ANSI codepage, which mangles them.
    private const string _petsciiClipboardFormat = "ReadyCode.PetsciiText";

    private static readonly Regex _leadingLineNumberPattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);

    private readonly BasicKeywordColorizer _keywordColorizer = new();
    private readonly LineNumberColorizer _lineNumberColorizer = new();
    private readonly NumberLiteralColorizer _numberLiteralColorizer = new();
    private readonly StringLiteralColorizer _stringLiteralColorizer = new();
    private readonly DataLiteralColorizer _dataLiteralColorizer = new();
    private readonly RemCommentColorizer _remCommentColorizer = new();
    private readonly FindHighlightColorizer _findHighlightColorizer = new();
    private readonly AsmMnemonicColorizer _asmMnemonicColorizer = new();
    private readonly AsmNumberLiteralColorizer _asmNumberLiteralColorizer = new();
    private readonly AsmLabelColorizer _asmLabelColorizer = new();
    private readonly AsmCommentColorizer _asmCommentColorizer = new();
    private readonly AsmLineNumberMargin _asmLineNumberMargin = new();
    private readonly PetsciiGlyphGenerator _petsciiGlyphGenerator = new();
    private List<(int Offset, int Length)> _findMatches = new();
    private int _findMatchIndex = -1;
    private CurrentLineBorderRenderer _currentLineBorderRenderer = null!;
    private readonly ErrorSquiggleRenderer _errorSquiggleRenderer;
    private readonly BreakpointMargin _breakpointMargin = new();
    private readonly DebugCurrentLineRenderer _debugCurrentLineRenderer = new();

    // The active BASIC tab's line address table, rebuilt whenever RunDocumentAnalysis runs -
    // used to resolve breakpoint-gutter clicks (document line -> BASIC line number) without
    // rebuilding it on every single click.
    private BasicLineAddressTable? _activeTabLineAddressTable;

    // Debounces BasicDiagnostics.Analyze so a full re-analysis doesn't run on every keystroke.
    private readonly DispatcherTimer _diagnosticsTimer;

    // Debounces the find-bar's live match list so an edit doesn't re-search the whole document
    // on every keystroke; only fired while the find bar is open (see Editor_TextChanged).
    private readonly DispatcherTimer _findUpdateTimer;

    // Shown when a Find / project search regex is abandoned for taking too long (see SearchPattern).
    private const string _regexTooSlowMessage =
        "The regular expression took too long to evaluate, so the search was stopped. Try a simpler pattern.";

    // The project search currently running in the background, if any - cancelled when a new
    // search starts so only the latest one's results are shown (see RunProjectSearchAsync).
    private CancellationTokenSource? _projectSearchCts;

    private IReadOnlyList<EditorDiagnostic> _currentDiagnostics = Array.Empty<EditorDiagnostic>();

    // The most recent assembly result for the active Asm tab, refreshed by RunAsmSymbolIndex on
    // the same debounce tick as diagnostics - reused by the hover tooltip so it doesn't need to
    // re-assemble on every mouse move.
    private AssemblyResult? _lastAsmResult;

    // Code folding - bound to whichever document is currently on Editor.TextArea; reinstalled per
    // tab activation (see ActivateTab) since FoldingManager can't be rebound to a new document.
    private FoldingManager? _foldingManager;
    private readonly BasicFoldingStrategy _foldingStrategy = new();
    private readonly AsmFoldingStrategy _asmFoldingStrategy = new();

    // Tab management state
    private bool _tabSwitching;
    private bool _activatingTab;
    private bool _ctrlKChordPending;

    // Set once OnClosing has already run its unsaved-tab prompts and any active debug session's
    // async cleanup has finished - see OnClosing for why closing sometimes has to cancel itself,
    // await that cleanup, then request the close again.
    private bool _readyToClose;

    // Closed-tab history for Ctrl+Shift+T, most-recently-closed last. In-memory only (starts
    // empty each run) and capped at 20 entries, oldest evicted first.
    private const int _maxClosedTabHistory = 20;
    private readonly List<ClosedTabSnapshot> _closedTabHistory = new();

    // Chord shortcut state (Ctrl+K → Ctrl+C / Ctrl+K → Ctrl+U / Ctrl+K → Ctrl+F)
    private bool _chordCtrlKActive;

    // Keyword completion
    private CompletionWindow? _completionWindow;
    private readonly GhostTextRenderer _ghostRenderer;

    // Hover tooltips
    private ToolTip? _hoverToolTip;

    // Drag-and-drop state
    private Point _dragStartPoint;
    private FileTreeItem? _dragItem;
    private FileTreeItem? _currentDropTarget;
    private Point _c64uDragStartPoint;
    private C64UFileItem? _c64uDragItem;
    private C64UFileItem? _c64uCurrentDropTarget;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class, restoring window/panel
    /// state from settings and wiring up all commands and editor event handlers.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Restore window size and position from previous session
        var s = ViewModel.Settings;
        Width  = s.MainWindowWidth;
        Height = s.MainWindowHeight;
        if (s.MainWindowLeft.HasValue && s.MainWindowTop.HasValue)
        {
            double l = s.MainWindowLeft.Value;
            double t = s.MainWindowTop.Value;
            // Only restore if the window would be at least partially on a connected screen
            if (l + Width  > SystemParameters.VirtualScreenLeft &&
                l          < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                t + Height > SystemParameters.VirtualScreenTop &&
                t          < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                Left = l;
                Top  = t;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }
        
        if (s.IsMainWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        // Initialize commands
        FileNewCommand = new RelayCommand(_ => FileNew_Click(this, new RoutedEventArgs()));
        FileNewBasCommand = new RelayCommand(_ => FileNewBas_Click(this, new RoutedEventArgs()));
        FileNewAsmCommand = new RelayCommand(_ => FileNewAsm_Click(this, new RoutedEventArgs()));
        FileOpenCommand = new RelayCommand(_ => FileOpen_Click(this, new RoutedEventArgs()));
        FileSaveCommand = new RelayCommand(_ => FileSave_Click(this, new RoutedEventArgs()));
        FileSaveAsCommand = new RelayCommand(_ => FileSaveAs_Click(this, new RoutedEventArgs()));
        FileExportCommand = new RelayCommand(_ => FileExport_Click(this, new RoutedEventArgs()));
        FileImportCommand = new RelayCommand(_ => FileImport_Click(this, new RoutedEventArgs()));
        EditUndoCommand = new RelayCommand(_ => EditUndo_Click(this, new RoutedEventArgs()), _ => ViewModel.ActiveTab?.IsHexMode == true ? HexEditor.CanUndo : Editor.CanUndo);
        EditRedoCommand = new RelayCommand(_ => EditRedo_Click(this, new RoutedEventArgs()), _ => ViewModel.ActiveTab?.IsHexMode == true ? HexEditor.CanRedo : Editor.CanRedo);
        EditCutCommand = new RelayCommand(_ => EditCut_Click(this, new RoutedEventArgs()), _ => HasSelection());
        EditCopyCommand = new RelayCommand(_ => EditCopy_Click(this, new RoutedEventArgs()), _ => HasSelection());
        EditPasteCommand = new RelayCommand(_ => EditPaste_Click(this, new RoutedEventArgs()), _ => HasActiveTab());
        EditDeleteCommand = new RelayCommand(_ => EditDelete_Click(this, new RoutedEventArgs()), _ => HasSelection() || HasNonEmptyActiveTab());
        EditSelectAllCommand = new RelayCommand(_ => EditSelectAll_Click(this, new RoutedEventArgs()));
        EditCommentCommand   = new RelayCommand(_ => ExecuteCommentSelection(), _ => HasNonEmptyBasicActiveTab());
        EditUncommentCommand = new RelayCommand(_ => ExecuteUncommentSelection(), _ => HasNonEmptyBasicActiveTab());
        EditMakeUppercaseCommand = new RelayCommand(_ => ExecuteChangeSelectionCase(upper: true),  _ => HasNonEmptyActiveTab());
        EditMakeLowercaseCommand = new RelayCommand(_ => ExecuteChangeSelectionCase(upper: false), _ => HasNonEmptyActiveTab());
        EditToggleLowerCaseModeCommand = new RelayCommand(
            _ => ViewModel.IsLowerCaseModeActive = !ViewModel.IsLowerCaseModeActive,
            _ => ViewModel.IsShiftModeApplicable);
        EditMinifyCommand    = new RelayCommand(_ => ExecuteMinifyCode(), _ => HasNonEmptyBasicActiveTab());
        EditPrettifyCommand  = new RelayCommand(_ => ExecutePrettifyCode(), _ => HasNonEmptyBasicActiveTab());
        EditRenumberCommand  = new RelayCommand(_ => ExecuteRenumberCode(), _ => HasNonEmptyBasicActiveTab());
        EditFormatCommand    = new RelayCommand(_ => ExecuteFormatAsmCode(), _ => HasNonEmptyAsmActiveTab());
        EditGoToLineCommand  = new RelayCommand(_ => ExecuteGoToLine(), _ => HasNonEmptyActiveTab());
        GoToDefinitionCommand = new RelayCommand(_ => ExecuteGoToDefinition(), _ => HasNonEmptyActiveTab());
        PreferencesSettingsCommand = new RelayCommand(_ => SettingsPreferences_Click(this, new RoutedEventArgs()));
        FileOpenFolderCommand = new RelayCommand(_ => OpenFolderDialog());
        FileCloseFolderCommand = new RelayCommand(_ => CloseFolder(), _ => HasFolderOpen());
        InsertSpecialCharCommand = new RelayCommand(p => {
            if (p is string s && int.TryParse(s, out int code))
                InsertSpecialChar((char)code);
        });
        TabCloseCurrentCommand = new RelayCommand(_ => { if (ViewModel.ActiveTab != null) CloseTab(ViewModel.ActiveTab); }, _ => HasActiveTab());
        TabReopenClosedCommand = new RelayCommand(_ => ReopenClosedTab(), _ => HasClosedTabHistory());

        EditFindCommand    = new RelayCommand(_ => OpenFind(), _ => HasNonEmptyActiveTab());
        EditReplaceCommand = new RelayCommand(_ => OpenReplace(), _ => HasNonEmptyActiveTab());
        EditFindInFilesCommand    = new RelayCommand(_ => OpenProjectSearch(replaceMode: false), _ => ViewModel.Project.IsOpen);
        EditReplaceInFilesCommand = new RelayCommand(_ => OpenProjectSearch(replaceMode: true),  _ => ViewModel.Project.IsOpen);

        ViewPrimarySideBarCommand   = new RelayCommand(_ => TogglePrimarySideBar());
        ViewSecondarySideBarCommand = new RelayCommand(_ => ToggleSecondarySideBar());
        ViewExplorerCommand         = new RelayCommand(_ => FocusExplorer());
        ViewWordWrapCommand         = new RelayCommand(_ => { ViewModel.WordWrap = !ViewModel.WordWrap; });
        ViewCodeStatisticsCommand   = new RelayCommand(_ => ShowCodeStatistics(), _ => HasNonEmptyActiveTab());
        ViewVariablesCommand        = new RelayCommand(_ => { ViewModel.ShowVariableExplorer = !ViewModel.ShowVariableExplorer; });

        DebugToggleBreakpointCommand = new RelayCommand(_ => ToggleBreakpointAtCaret(),
            _ => ViewModel.ActiveTab is { Language: EditorLanguage.Basic });
        DebugToggleBreakpointEnabledCommand = new RelayCommand(_ => ToggleBreakpointEnabledAtCaret(),
            _ => ViewModel.ActiveTab is { Language: EditorLanguage.Basic });
        DebugDeleteAllBreakpointsCommand = new RelayCommand(async _ => await DeleteAllBreakpointsAsync(),
            _ => ViewModel.BreakpointStore.Breakpoints.Count > 0);
        DebugRunToCursorCommand = new RelayCommand(async _ => await RunToCursorAsync(),
            _ => ViewModel.IsDebugging && ViewModel.IsDebugStopped);

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ShowColumnGuide) or nameof(MainViewModel.WordWrap))
                ApplyEditorAppearance();
            if (e.PropertyName == nameof(MainViewModel.ShowVariableExplorer))
                ApplyVariableExplorerVisibility();
            if (e.PropertyName == nameof(MainViewModel.DebugCurrentDocumentLine))
                ApplyDebugCurrentLine();
            if (e.PropertyName is nameof(MainViewModel.IsDebugPanelActive) or nameof(MainViewModel.IsErrorsPanelActive))
                ApplyBottomPanelOpenState();
            if (e.PropertyName == nameof(MainViewModel.IsUpperCaseModeActive))
            {
                ApplyUpperCaseMode();

                // ViewModel.IsUpperCaseModeActive tracks the ACTIVE tab, not necessarily the
                // debugged one - only refresh the Variables tree's string displays when they're
                // actually the same tab (toggling the mode on some other, non-debugged tab, or
                // merely switching which tab is active, has nothing to do with the debug session).
                if (ReferenceEquals(ViewModel.ActiveTab, ViewModel.DebugTab))
                    RefreshDebugVariablesDisplayMode();
            }
        };

        // WPF has no change-notification event for Caps Lock - Activated catches a
        // toggle made while some other window had focus; Window_PreviewKeyDown catches one made
        // via the physical key while typing here.
        Activated += (_, _) => ViewModel.RefreshKeyboardLockStatus();

        _breakpointMargin.BreakpointToggleRequested += (_, documentLine) => ToggleBreakpointAtDocumentLine(documentLine);

        // Reacts to a breakpoint's IsEnabled changing by any means - in practice, right now,
        // only the Breakpoints grid's "On" checkbox - rather than relying on the grid's own
        // CellEditEnding event, which turned out not to reliably fire (or fire late enough for
        // the binding to have committed) for every row when multiple checkboxes were toggled in
        // quick succession, silently leaving the gutter dot and the live VICE session out of
        // sync with what the checkbox actually showed.
        ViewModel.BreakpointStore.Breakpoints.CollectionChanged += BreakpointStore_CollectionChanged;
        foreach (var breakpoint in ViewModel.BreakpointStore.Breakpoints)
            breakpoint.PropertyChanged += Breakpoint_PropertyChanged;

        // Keeps the Breakpoints grid sorted by file then line, regardless of add/remove order -
        // the default CollectionView WPF creates for any ItemsSource re-sorts automatically as
        // the underlying ObservableCollection changes, so this only needs to be set up once.
        var breakpointsView = CollectionViewSource.GetDefaultView(ViewModel.BreakpointStore.Breakpoints);
        breakpointsView.SortDescriptions.Add(new SortDescription(nameof(Breakpoint.FilePath), ListSortDirection.Ascending));
        breakpointsView.SortDescriptions.Add(new SortDescription(nameof(Breakpoint.LineNumber), ListSortDirection.Ascending));

        HexEditor.ByteEdited += (_, _) => { if (ViewModel.ActiveTab != null) ViewModel.ActiveTab.IsModified = true; };

        CompareControl.ViewStateChanged += (isUnified, ignoreWhitespace) =>
        {
            if (ViewModel.ActiveTab is { IsCompareMode: true } tab)
            {
                tab.CompareIsUnified = isUnified;
                tab.CompareIgnoreWhitespace = ignoreWhitespace;
            }
        };

        // Force pasted text to upper case, just like typed text
        DataObject.AddPastingHandler(Editor, Editor_Pasting);

        // AvalonEdit raises selection/caret changes via the TextArea, not the editor itself
        Editor.TextArea.SelectionChanged += Editor_SelectionChanged;
        Editor.TextArea.Caret.PositionChanged += Editor_CaretPositionChanged;

        ApplyLineTransformersForLanguage(EditorLanguage.Basic);
        _currentLineBorderRenderer = new CurrentLineBorderRenderer(Editor);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_currentLineBorderRenderer);
        _errorSquiggleRenderer = new ErrorSquiggleRenderer(Editor);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_errorSquiggleRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_debugCurrentLineRenderer);
        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _diagnosticsTimer.Tick += (_, _) => { _diagnosticsTimer.Stop(); RunDocumentAnalysis(); };
        _findUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _findUpdateTimer.Tick += (_, _) => { _findUpdateTimer.Stop(); UpdateFindMatches(); };

        FindBar.CloseRequested      += (_, _) =>
        {
            _findUpdateTimer.Stop();
            _findHighlightColorizer.Clear();
            RedrawMatches(_findMatches);
            Editor.Focus();
        };
        FindBar.SearchChanged       += (_, _) => UpdateFindMatches();
        FindBar.FindNextRequested   += (_, _) => FindNext();
        FindBar.FindPreviousRequested += (_, _) => FindPrev();
        FindBar.ReplaceRequested    += (_, _) => ExecuteReplace();
        FindBar.ReplaceAllRequested += (_, _) => ExecuteReplaceAll();

        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            // Bail out immediately when the caret is still on the same line (e.g. just moving a
            // column) - nothing here would change, so even reaching InvalidateLayer/Redraw is
            // wasted work.
            int newLine = Editor.Document.GetLineByOffset(Editor.CaretOffset).LineNumber;
            int oldLine = _lineNumberColorizer.ActiveDocumentLineNumber;
            if (newLine == oldLine) return;

            _lineNumberColorizer.ActiveDocumentLineNumber = newLine;
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

            if (oldLine >= 1 && oldLine <= Editor.Document.LineCount)
            {
                var previousLine = Editor.Document.GetLineByNumber(oldLine);
                Editor.TextArea.TextView.Redraw(previousLine.Offset, previousLine.Length);
            }
            var currentLine = Editor.Document.GetLineByNumber(newLine);
            Editor.TextArea.TextView.Redraw(currentLine.Offset, currentLine.Length);
        };
        // AvalonEdit's built-in control-character boxes (e.g. "DC1", "GS") would otherwise
        // render before our generator gets a chance to show the actual C64 ROM glyph
        Editor.Options.ShowBoxForControlCharacters = false;
        Editor.TextArea.TextView.ElementGenerators.Add(_petsciiGlyphGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(new LineSpacingElementGenerator { ExtraSpacing = 4 });
        _ghostRenderer = new GhostTextRenderer(Editor.TextArea);
        // AdornerLayer and PETSCII table both need the visual tree to be ready.
        Loaded += (_, _) =>
        {
            AdornerLayer.GetAdornerLayer(Editor.TextArea)?.Add(_ghostRenderer);
            BuildPetsciiTable();
            BuildBasicKeywordsList();
            BuildAsmKeywordsList();
            BuildMusicNotesTable();

            // TabBar's ScrollViewer only exists once its control template is applied - hook up
            // overflow detection here rather than in the constructor, and check the initial
            // state immediately since tabs restored from the last session are already present.
            var tabBarScrollViewer = FindVisualChild<ScrollViewer>(TabBar);
            if (tabBarScrollViewer != null)
            {
                tabBarScrollViewer.ScrollChanged += (_, _) => UpdateTabListButtonVisibility(tabBarScrollViewer);
                UpdateTabListButtonVisibility(tabBarScrollViewer);
            }
        };
        ApplyEditorAppearance();
        ViewModel.RefreshKeyboardLockStatus();

        // Set DataContext for binding
        DataContext = this;

        // No tabs at startup - EmptyStateImage/Editor's XAML-default visibility already reflects
        // that; the user has to create or open a file before an editor tab appears.

        UpdateScreenPositionStatus();
        UpdateLineCountStatus();
        RefreshRecentFiles();

        // Restore side panel states from previous session
        if (ViewModel.Settings.IsLeftPanelOpen)
        {
            LeftPanelCol.Width = new GridLength(ViewModel.Settings.LeftPanelWidth);
            LeftSplitterCol.Width = new GridLength(4);

            // ExplorerPanel is visible by default in XAML while C64UPanel defaults to Collapsed,
            // and setting IsChecked here doesn't fire the toggles' Click handlers - so every
            // panel's visibility must be set explicitly to match the restored tab.
            var restoreTarget = LeftPanelToggles.FirstOrDefault(t => t.SettingsKey == ViewModel.Settings.ActiveLeftPanel);
            if (restoreTarget.Toggle == null) restoreTarget = LeftPanelToggles.First();

            foreach (var (toggle, panel, _) in LeftPanelToggles)
            {
                bool isTarget = ReferenceEquals(toggle, restoreTarget.Toggle);
                toggle.IsChecked = isTarget;
                panel.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
            }

            // Deliberately does not auto-connect even if the C64U panel was open last session -
            // connecting only ever happens from an explicit "Connect" click.
        }
        if (ViewModel.Settings.IsRightPanelOpen)
        {
            RightPanelCol.Width = new GridLength(ViewModel.Settings.RightPanelWidth);
            RightSplitterCol.Width = new GridLength(4);

            // SpecialCharsPanel is visible by default in XAML while the others default to
            // Collapsed, and setting IsChecked here doesn't fire the toggles' Click handlers -
            // so every panel's visibility must be set explicitly to match the restored tab.
            var restoreTarget = RightPanelToggles.FirstOrDefault(t => t.SettingsKey == ViewModel.Settings.ActiveRightPanel);
            if (restoreTarget.Toggle == null) restoreTarget = RightPanelToggles.First();

            foreach (var (toggle, panel, _) in RightPanelToggles)
            {
                bool isTarget = ReferenceEquals(toggle, restoreTarget.Toggle);
                toggle.IsChecked = isTarget;
                panel.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        ApplyVariableExplorerVisibility();

        ActivateDebugPanelTab(ViewModel.Settings.ActiveDebugPanelTab);
        ActivateBottomPanelTab(ViewModel.Settings.ActiveBottomPanelTab);
        if (ViewModel.Settings.IsBottomPanelOpen)
            ApplyBottomPanelOpenState();

        if (ViewModel.Project.IsOpen && Directory.Exists(ViewModel.Project.RootPath))
        {
            ViewModel.LoadFolder(ViewModel.Project.RootPath);
        }

        if (ViewModel.Settings.RestoreOpenTabsOnStartup)
        {
            // Skip missing files quietly - OpenFileByPath's own error dialog would otherwise
            // pop up once per moved/deleted file on every launch.
            EditorTab? firstRestoredTab = null;
            foreach (string path in ViewModel.Settings.OpenTabPaths)
            {
                if (!File.Exists(path)) continue;

                OpenFileByPath(path, forceHex: ViewModel.Settings.OpenTabHexModePaths.Contains(path, StringComparer.OrdinalIgnoreCase));
                firstRestoredTab ??= ViewModel.OpenTabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
            }

            // OpenFileByPath activates whatever it just opened, so after the loop the LAST
            // restored tab ends up active rather than the first - reactivate the first one to
            // match the order the tabs were saved in.
            if (firstRestoredTab != null)
                ActivateTab(firstRestoredTab);
        }
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the view model backing the window's title and status bar.
    /// </summary>
    public MainViewModel ViewModel { get; } = new();

    // Command properties
    /// <summary>Gets the command that creates a new BASIC tab (tokenized .prg format).</summary>
    public ICommand FileNewCommand { get; }
    /// <summary>Gets the command that creates a new untokenized BASIC (.bas) tab.</summary>
    public ICommand FileNewBasCommand { get; }
    /// <summary>Gets the command that creates a new assembly tab.</summary>
    public ICommand FileNewAsmCommand { get; }
    /// <summary>Gets the command that opens a file.</summary>
    public ICommand FileOpenCommand { get; }
    /// <summary>Gets the command that saves the active tab.</summary>
    public ICommand FileSaveCommand { get; }
    /// <summary>Gets the command that saves the active tab to a new path.</summary>
    public ICommand FileSaveAsCommand { get; }
    /// <summary>Gets the command that exports the active tab's text to a .txt file.</summary>
    public ICommand FileExportCommand { get; }
    /// <summary>Gets the command that imports a .txt file into a new tab.</summary>
    public ICommand FileImportCommand { get; }
    /// <summary>Gets the command that undoes the last editor change.</summary>
    public ICommand EditUndoCommand { get; }
    /// <summary>Gets the command that redoes the last undone editor change.</summary>
    public ICommand EditRedoCommand { get; }
    /// <summary>Gets the command that cuts the current selection.</summary>
    public ICommand EditCutCommand { get; }
    /// <summary>Gets the command that copies the current selection.</summary>
    public ICommand EditCopyCommand { get; }
    /// <summary>Gets the command that pastes the clipboard contents.</summary>
    public ICommand EditPasteCommand { get; }
    /// <summary>Gets the command that deletes the current selection.</summary>
    public ICommand EditDeleteCommand { get; }
    /// <summary>Gets the command that selects all text in the active tab.</summary>
    public ICommand EditSelectAllCommand { get; }
    /// <summary>Gets the command that comments out the selected lines.</summary>
    public ICommand EditCommentCommand   { get; }
    /// <summary>Gets the command that uncomments the selected lines.</summary>
    public ICommand EditUncommentCommand { get; }
    /// <summary>Gets the command that converts the highlighted text to upper case.</summary>
    public ICommand EditMakeUppercaseCommand { get; }
    /// <summary>Gets the command that converts the highlighted text to lower case.</summary>
    public ICommand EditMakeLowercaseCommand { get; }
    /// <summary>Gets the command that toggles the active tab's C64 keyboard emulation Lower Case Mode.</summary>
    public ICommand EditToggleLowerCaseModeCommand { get; }
    /// <summary>Gets the command that opens the Minify dialog.</summary>
    public ICommand EditMinifyCommand    { get; }
    /// <summary>Gets the command that opens the Prettify dialog.</summary>
    public ICommand EditPrettifyCommand  { get; }
    /// <summary>Gets the command that opens the Renumber dialog.</summary>
    public ICommand EditRenumberCommand  { get; }
    /// <summary>Gets the command that reformats the active assembly tab per the Assembly Formatting settings.</summary>
    public ICommand EditFormatCommand    { get; }
    /// <summary>Gets the command that opens the Go To Line dialog.</summary>
    public ICommand EditGoToLineCommand  { get; }
    /// <summary>Gets the command that jumps to the BASIC line targeted by the GOTO/GOSUB line number under the caret.</summary>
    public ICommand GoToDefinitionCommand { get; }
    /// <summary>Gets the command that opens the Preferences dialog.</summary>
    public ICommand PreferencesSettingsCommand { get; }
    /// <summary>Gets the command that opens a folder in the folder explorer.</summary>
    public ICommand FileOpenFolderCommand { get; }
    /// <summary>Gets the command that closes the currently open folder.</summary>
    public ICommand FileCloseFolderCommand { get; }
    /// <summary>Gets the command that inserts a special character at the caret.</summary>
    public ICommand InsertSpecialCharCommand { get; }
    /// <summary>Gets the command that closes the active tab.</summary>
    public ICommand TabCloseCurrentCommand { get; }
    /// <summary>Gets the command that reopens the most recently closed tab.</summary>
    public ICommand TabReopenClosedCommand { get; }

    // Find / Replace (MainWindow-owned so they can access the editor and FindBar directly)
    /// <summary>Gets the command that opens the find bar.</summary>
    public ICommand EditFindCommand    { get; }
    /// <summary>Gets the command that opens the find bar in replace mode.</summary>
    public ICommand EditReplaceCommand { get; }
    /// <summary>Gets the command that opens the project-wide Search panel.</summary>
    public ICommand EditFindInFilesCommand { get; }
    /// <summary>Gets the command that opens the project-wide Search panel with the Replace field expanded.</summary>
    public ICommand EditReplaceInFilesCommand { get; }

    // View Menu
    /// <summary>Gets the command that toggles the left (folder explorer) panel.</summary>
    public ICommand ViewPrimarySideBarCommand   { get; }
    /// <summary>Gets the command that toggles the right (Quick Keys / PETSCII) panel.</summary>
    public ICommand ViewSecondarySideBarCommand { get; }
    /// <summary>Gets the command that focuses the folder explorer, opening it if needed.</summary>
    public ICommand ViewExplorerCommand         { get; }
    /// <summary>Gets the command that toggles word wrap in the editor.</summary>
    public ICommand ViewWordWrapCommand         { get; }
    /// <summary>Gets the command that shows the code statistics dialog.</summary>
    public ICommand ViewCodeStatisticsCommand   { get; }
    /// <summary>Gets the command that toggles the Variable Explorer section.</summary>
    public ICommand ViewVariablesCommand        { get; }

    // Debug (MainWindow-owned since it needs the caret's current line, unlike the session
    // lifecycle commands on MainViewModel)
    /// <summary>Gets the command that sets or clears a breakpoint on the caret's current BASIC line.</summary>
    public ICommand DebugToggleBreakpointCommand { get; }
    /// <summary>Gets the command that enables or disables (without removing) the breakpoint on the caret's current BASIC line.</summary>
    public ICommand DebugToggleBreakpointEnabledCommand { get; }
    /// <summary>Gets the command that removes every breakpoint, across all files.</summary>
    public ICommand DebugDeleteAllBreakpointsCommand { get; }
    /// <summary>Gets the command that resumes a halted debug session and runs until the caret's current line is reached.</summary>
    public ICommand DebugRunToCursorCommand { get; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Persists window bounds and panel state to settings when the window closes.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnClosed(EventArgs e)
    {
        var activeLeftPanel = LeftPanelToggles.FirstOrDefault(t => t.Toggle.IsChecked == true);
        ViewModel.Settings.IsLeftPanelOpen = activeLeftPanel.SettingsKey != null;
        if (activeLeftPanel.SettingsKey != null)
            ViewModel.Settings.ActiveLeftPanel = activeLeftPanel.SettingsKey;
        ViewModel.C64UFtp?.Dispose();
        var activePanel = RightPanelToggles.FirstOrDefault(t => t.Toggle.IsChecked == true);
        ViewModel.Settings.IsRightPanelOpen = activePanel.SettingsKey != null;
        if (activePanel.SettingsKey != null)
            ViewModel.Settings.ActiveRightPanel = activePanel.SettingsKey;
        if (LeftPanelCol.Width.Value > 0)
            ViewModel.Settings.LeftPanelWidth = LeftPanelCol.Width.Value;
        if (RightPanelCol.Width.Value > 0)
            ViewModel.Settings.RightPanelWidth = RightPanelCol.Width.Value;
        // FolderTreeRow.Height is Star-sized (not absolute) while the Variable Explorer is
        // hidden - .Value would just be the star weight then, not a real pixel height.
        if (FolderTreeRow.Height.IsAbsolute && FolderTreeRow.Height.Value > 0)
            ViewModel.Settings.ExplorerFolderTreeHeight = FolderTreeRow.Height.Value;

        ViewModel.Settings.IsMainWindowMaximized = WindowState == WindowState.Maximized;

        // Save window bounds — use RestoreBounds when maximised so the saved
        // rect reflects the unmaximised size rather than the full-screen size.
        var bounds = WindowState == WindowState.Normal
            ? new System.Windows.Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            ViewModel.Settings.MainWindowLeft   = bounds.Left;
            ViewModel.Settings.MainWindowTop    = bounds.Top;
            ViewModel.Settings.MainWindowWidth  = bounds.Width;
            ViewModel.Settings.MainWindowHeight = bounds.Height;
        }

        ViewModel.Settings.OpenTabPaths = ViewModel.Settings.RestoreOpenTabsOnStartup
            ? ViewModel.OpenTabs.Where(t => t.FilePath != null).Select(t => t.FilePath!).ToList()
            : new List<string>();
        ViewModel.Settings.OpenTabHexModePaths = ViewModel.Settings.RestoreOpenTabsOnStartup
            ? ViewModel.OpenTabs.Where(t => t.FilePath != null && t.IsHexMode).Select(t => t.FilePath!).ToList()
            : new List<string>();

        ViewModel.Settings.Save();
        base.OnClosed(e);
    }

    /// <summary>
    /// Prompts to save any modified tabs before the window closes, cancelling the close if the
    /// user dismisses a save prompt, then - once those are settled - cleans up an active debug
    /// session before letting the window actually close.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The second pass, after debug cleanup has finished and Close() was called again below -
        // skip straight to actually closing rather than re-running the prompts/cleanup a second
        // time (the unsaved-tab check in particular would otherwise re-prompt for a tab the user
        // already answered "No" to on the first pass).
        if (_readyToClose) { base.OnClosing(e); return; }

        foreach (var tab in ViewModel.OpenTabs.ToList())
        {
            if (!tab.IsModified) continue;
            ActivateTab(tab);
            var result = MessageBox.Show(
                $"Save changes to \"{tab.FileName}\"?",
                "READYCode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) { e.Cancel = true; base.OnClosing(e); return; }
            if (result == MessageBoxResult.Yes && !SaveTabWithDialog(tab)) { e.Cancel = true; base.OnClosing(e); return; }
        }

        // DebugSession.DisposeAsync does real work (deleting its checkpoint over the live
        // connection, closing the socket/HTTP client, joining its background read/poll loop task)
        // that can't run synchronously here - cancel this close, await the same cleanup Stop
        // Debugging uses, then request the close again once it's done, rather than leaving a live
        // session dangling (still polling/connected) after the window disappears.
        if (ViewModel.DebugSession != null)
        {
            e.Cancel = true;
            base.OnClosing(e);
            _ = CloseAfterDebugCleanupAsync();
            return;
        }

        base.OnClosing(e);
    }

    private async Task CloseAfterDebugCleanupAsync()
    {
        await ViewModel.DebugStopAsync();
        _readyToClose = true;
        Close();
    }
    #endregion
    #region Private Methods

    // Gates commands that just need an open tab, regardless of its content (Close, Paste).
    private bool HasActiveTab() => ViewModel.ActiveTab != null;

    // Gates commands that need an open tab with at least one character typed into it. Checks
    // ViewModel.ActiveTab rather than Editor.Text/Editor.Document, since those can still hold a
    // stale previous tab's content while Editor itself sits hidden with zero tabs open.
    private bool HasNonEmptyActiveTab() => !string.IsNullOrEmpty(ViewModel.ActiveTab?.Document.Text);

    // Gates commands that run BASIC-specific text transforms (prettify/minify/renumber, GOTO
    // navigation, REM comment toggling) and would corrupt or misparse assembly source.
    private bool HasNonEmptyBasicActiveTab() =>
        HasNonEmptyActiveTab() && ViewModel.ActiveTab?.Language != EditorLanguage.Asm;

    // Gates Format Code, the assembly counterpart of the BASIC-only commands above.
    private bool HasNonEmptyAsmActiveTab() =>
        HasNonEmptyActiveTab() && ViewModel.ActiveTab?.Language == EditorLanguage.Asm;

    // Gates Cut/Copy, which need an actual selection rather than just non-empty content - routes
    // to the hex grid's own selection when a hex tab is active, since Editor's SelectionLength
    // reflects whatever text tab it was last bound to, not the (invisible) hex tab.
    private bool HasSelection() =>
        ViewModel.ActiveTab?.IsHexMode == true ? HexEditor.HasSelection : ViewModel.ActiveTab != null && Editor.SelectionLength > 0;

    // Gates Close Folder, which only makes sense once a folder has been opened.
    private bool HasFolderOpen() => ViewModel.Project.IsOpen;

    // Gates Reopen Closed Tab, which only makes sense once a tab has actually been closed.
    private bool HasClosedTabHistory() => _closedTabHistory.Count > 0;
    #region Side Panels
    #region Inline Rename

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T elem && elem.Name == name) return elem;
            var result = FindVisualChild<T>(child, name);
            if (result != null) return result;
        }
        return null;
    }

    // Finds the first descendant of type T regardless of name - used to reach into a control's
    // default template (e.g. the ScrollViewer inside a ListBox) where there's nothing to name.
    private static T? FindVisualChild<T>(DependencyObject parent) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
    #endregion

    #endregion

    #region Settings

    private void SettingsPreferences_Click(object sender, RoutedEventArgs e) => OpenSettingsDialog();

    // Opens the Settings dialog to the given tree section (see the Tag values in
    // SettingsWindow.xaml), or the default "Application > General" section if omitted.
    private void OpenSettingsDialog(string? initialSection = null)
    {
        var dialog = new SettingsWindow(ViewModel.Settings, initialSection) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            dialog.ViewModel.ApplyTo(ViewModel.Settings);
            ViewModel.Settings.Save();
            ApplyEditorAppearance();
            ApplyCodeAnalysisSettings();
            UpdateScreenPositionStatus();
            ViewModel.RefreshMenuVisibility();
        }
    }
    #endregion
    #region Event Handlers

    // Full display names for PETSCII control codes that can appear inside a string literal,
    // matching the names already shown on the corresponding Quick Keys buttons (see
    // MainWindow.xaml) plus the handful of control codes with no dedicated button.
    private static readonly Dictionary<int, string> _petsciiControlCharNames = new()
    {
        [5]   = "White",
        [6]   = "Disable Shift+C=",
        [7]   = "Enable Shift+C=",
        [13]  = "Return",
        [14]  = "Lower Case",
        [17]  = "Cursor Down",
        [18]  = "Reverse On",
        [19]  = "Home",
        [20]  = "Insert/Delete",
        [28]  = "Red",
        [29]  = "Cursor Right",
        [30]  = "Green",
        [31]  = "Blue",
        [129] = "Orange",
        [133] = "Function 1",
        [134] = "Function 3",
        [135] = "Function 5",
        [136] = "Function 7",
        [137] = "Function 2",
        [138] = "Function 4",
        [139] = "Function 6",
        [140] = "Function 8",
        [141] = "Shift+Return",
        [142] = "Upper Case",
        [144] = "Black",
        [145] = "Cursor Up",
        [146] = "Reverse Off",
        [147] = "CLR",
        [148] = "Insert/Delete",
        [149] = "Brown",
        [150] = "Light Red",
        [151] = "Gray 1",
        [152] = "Gray 2",
        [153] = "Light Green",
        [154] = "Light Blue",
        [155] = "Gray 3",
        [156] = "Purple",
        [157] = "Cursor Left",
        [158] = "Yellow",
        [159] = "Cyan",
    };
    #endregion
    #region UI Updates

    private void ApplyEditorAppearance()
    {
        ApplyTheme(ViewModel.Settings.Theme);
        BuildPetsciiTable();
        BuildBasicKeywordsList();
        BuildAsmKeywordsList();
        BuildMusicNotesTable();

        Editor.Background = (Brush)FindResource("ThemeEditorBg");
        Editor.Foreground = (Brush)FindResource("ThemeEditorFg");
        Editor.FontSize   = ViewModel.Settings.EditorFontSize;
        Editor.WordWrap   = ViewModel.Settings.WordWrap;
        HexEditor.HexFontSize = ViewModel.Settings.EditorFontSize;
        CompareControl.EditorFontSize = ViewModel.Settings.EditorFontSize;
        _lineNumberColorizer.LineNumberBrush       = (Brush)FindResource("ThemeEditorLineNumberFg");
        _lineNumberColorizer.ActiveLineNumberBrush = (Brush)FindResource("ThemeEditorFg");
        _keywordColorizer.KeywordBrush          = (Brush)FindResource("ThemeEditorKeywordFg");
        _numberLiteralColorizer.NumberBrush     = (Brush)FindResource("ThemeEditorNumberLiteralFg");
        _stringLiteralColorizer.StringBrush     = (Brush)FindResource("ThemeEditorStringFg");
        _dataLiteralColorizer.StringBrush       = (Brush)FindResource("ThemeEditorStringFg");
        _remCommentColorizer.CommentBrush       = (Brush)FindResource("ThemeEditorCommentFg");
        _asmMnemonicColorizer.MnemonicBrush     = (Brush)FindResource("ThemeEditorKeywordFg");
        _asmNumberLiteralColorizer.NumberBrush  = (Brush)FindResource("ThemeEditorNumberLiteralFg");
        _asmLabelColorizer.LabelBrush           = (Brush)FindResource("ThemeEditorLineNumberFg");
        _asmCommentColorizer.CommentBrush       = (Brush)FindResource("ThemeEditorCommentFg");
        _asmLineNumberMargin.TextBrush   = (Brush)FindResource("ThemeEditorLineNumberFg");
        _asmLineNumberMargin.FontSize    = ViewModel.Settings.EditorFontSize;
        _asmLineNumberMargin.ZeroPadWidth = ViewModel.Settings.LineNumberPadding;
        _asmLineNumberMargin.InvalidateMeasure();
        _asmLineNumberMargin.InvalidateVisual();
        _findHighlightColorizer.MatchBrush          = (Brush)FindResource("ThemeFindMatchBg");
        _findHighlightColorizer.MatchFgBrush        = (Brush)FindResource("ThemeFindMatchFg");
        _findHighlightColorizer.CurrentMatchBrush   = (Brush)FindResource("ThemeFindCurrentBg");
        _findHighlightColorizer.CurrentMatchFgBrush = (Brush)FindResource("ThemeFindCurrentFg");
        _currentLineBorderRenderer.SetColor(((SolidColorBrush)FindResource("ThemeEditorCurrentLineBorder")).Color);
        _errorSquiggleRenderer.SetColor(((SolidColorBrush)FindResource("ThemeEditorErrorSquiggle")).Color);

        // Mark where the target machine would wrap, without actually wrapping the editor's text.
        // Keep ShowColumnRuler permanently on and toggle visibility via the position instead:
        // AvalonEdit treats a negative ColumnRulerPosition as "no ruler", and unlike ShowColumnRuler
        // (whose change handler is skipped when the value doesn't actually change, e.g. false -> false),
        // the position's change handler reliably redraws the ruler in its hidden/shown state
        Editor.Options.ShowColumnRuler = true;
        Editor.TextArea.TextView.ColumnRulerPen = new Pen((Brush)FindResource("ThemeEditorGuideLineFg"), 1);
        UpdateColumnRulerPosition();
    }

    // The column guide's target column is per-language (BASIC and assembly have their own
    // setting - see AppSettings.BasicColumnGuideColumn/AsmColumnGuideColumn), so unlike the rest
    // of ApplyEditorAppearance's one-time-refresh values, this also needs to be re-applied
    // whenever the active tab's language changes, not just on startup/theme change/Settings close.
    private int ActiveColumnGuideColumn() =>
        ViewModel.ActiveTab?.Language == EditorLanguage.Asm
            ? ViewModel.Settings.AsmColumnGuideColumn
            : ViewModel.Settings.BasicColumnGuideColumn;

    private void UpdateColumnRulerPosition()
    {
        Editor.Options.ColumnRulerPosition = ViewModel.Settings.ShowColumnGuide ? Math.Max(1, ActiveColumnGuideColumn()) : -1;
        Editor.TextArea.TextView.Redraw();
    }

    private static void ApplyTheme(string theme)
    {
        var themeName = theme switch { "Dark" => "Dark", "C64" => "C64", _ => "Light" };
        var uri = new Uri($"pack://application:,,,/Resources/Themes/{themeName}Theme.xaml");
        var merged = Application.Current.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("Theme.xaml") == true);
        if (existing != null) merged.Remove(existing);
        merged.Add(new ResourceDictionary { Source = uri });
    }

    private void UpdateLineCountStatus()
    {
        int lineCount = 1;
        string text = Editor.Text;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lineCount++;
        }

        ViewModel.LineCountText = $"Lines: {lineCount:N0}";
    }

    /// <summary>
    /// Show where the caret would land on the target machine's screen (e.g. a 40-column C64 display),
    /// simulating the hard character-wrap the real hardware performs at the configured column
    /// </summary>
    private void UpdateScreenPositionStatus()
    {
        int row = Editor.TextArea.Caret.Line;
        int col = Editor.TextArea.Caret.Column;

        ViewModel.ScreenPositionText = $"Col: {col}, Row: {row}";
    }
    #endregion

    #endregion
}

