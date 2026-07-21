// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;
using ReadyCode.Assembler;
using ReadyCode.C64U;
using ReadyCode.Diagnostics;
using ReadyCode.Editor;
using ReadyCode.Minify;
using ReadyCode.Models;
using ReadyCode.Prettify;
using ReadyCode.Search;
using ReadyCode.Sid;
using ReadyCode.Tokenizer;
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

    // The editor's default BASIC font (also set as Editor.FontFamily's XAML default) and the
    // font assembly tabs switch to instead - Pet Me 64 renders PETSCII glyphs BASIC relies on,
    // but has no useful bearing on plain 6502 assembly text.
    private static readonly FontFamily _basicEditorFont = new(new Uri("pack://application:,,,/ReadyCode;component/Assets/Fonts/"), "./#Pet Me 64");
    private static readonly FontFamily _asmEditorFont = new("Consolas");

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
    private List<(int Offset, int Length)> _findMatches = new();
    private int _findMatchIndex = -1;
    private CurrentLineBorderRenderer _currentLineBorderRenderer = null!;
    private readonly ErrorSquiggleRenderer _errorSquiggleRenderer;

    // Debounces BasicDiagnostics.Analyze so a full re-analysis doesn't run on every keystroke.
    private readonly DispatcherTimer _diagnosticsTimer;
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

    // Closed-tab history for Ctrl+Shift+T, most-recently-closed last. In-memory only (starts
    // empty each run) and capped at 20 entries, oldest evicted first.
    private const int _maxClosedTabHistory = 20;
    private readonly List<ClosedTabSnapshot> _closedTabHistory = new();

    // Chord shortcut state (Ctrl+K → Ctrl+C / Ctrl+K → Ctrl+U)
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
        FileNewAsmCommand = new RelayCommand(_ => FileNewAsm_Click(this, new RoutedEventArgs()));
        FileOpenCommand = new RelayCommand(_ => FileOpen_Click(this, new RoutedEventArgs()));
        FileSaveCommand = new RelayCommand(_ => FileSave_Click(this, new RoutedEventArgs()));
        FileSaveAsCommand = new RelayCommand(_ => FileSaveAs_Click(this, new RoutedEventArgs()));
        FileExportCommand = new RelayCommand(_ => FileExport_Click(this, new RoutedEventArgs()));
        FileImportCommand = new RelayCommand(_ => FileImport_Click(this, new RoutedEventArgs()));
        EditUndoCommand = new RelayCommand(_ => EditUndo_Click(this, new RoutedEventArgs()), _ => Editor.CanUndo);
        EditRedoCommand = new RelayCommand(_ => EditRedo_Click(this, new RoutedEventArgs()), _ => Editor.CanRedo);
        EditCutCommand = new RelayCommand(_ => EditCut_Click(this, new RoutedEventArgs()), _ => HasSelection());
        EditCopyCommand = new RelayCommand(_ => EditCopy_Click(this, new RoutedEventArgs()), _ => HasSelection());
        EditPasteCommand = new RelayCommand(_ => EditPaste_Click(this, new RoutedEventArgs()), _ => HasActiveTab());
        EditDeleteCommand = new RelayCommand(_ => EditDelete_Click(this, new RoutedEventArgs()), _ => HasNonEmptyActiveTab());
        EditSelectAllCommand = new RelayCommand(_ => EditSelectAll_Click(this, new RoutedEventArgs()));
        EditCommentCommand   = new RelayCommand(_ => ExecuteCommentSelection(), _ => HasNonEmptyBasicActiveTab());
        EditUncommentCommand = new RelayCommand(_ => ExecuteUncommentSelection(), _ => HasNonEmptyBasicActiveTab());
        EditMakeUppercaseCommand = new RelayCommand(_ => ExecuteChangeSelectionCase(upper: true),  _ => HasNonEmptyActiveTab());
        EditMakeLowercaseCommand = new RelayCommand(_ => ExecuteChangeSelectionCase(upper: false), _ => HasNonEmptyActiveTab());
        EditMinifyCommand    = new RelayCommand(_ => ExecuteMinifyCode(), _ => HasNonEmptyBasicActiveTab());
        EditPrettifyCommand  = new RelayCommand(_ => ExecutePrettifyCode(), _ => HasNonEmptyBasicActiveTab());
        EditRenumberCommand  = new RelayCommand(_ => ExecuteRenumberCode(), _ => HasNonEmptyBasicActiveTab());
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

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ShowColumnGuide) or nameof(MainViewModel.WordWrap))
                ApplyEditorAppearance();
            if (e.PropertyName == nameof(MainViewModel.ShowVariableExplorer))
                ApplyVariableExplorerVisibility();
        };

        FindBar.CloseRequested      += (_, _) => { _findHighlightColorizer.Clear(); Editor.TextArea.TextView.Redraw(); Editor.Focus(); };
        FindBar.SearchChanged       += (_, _) => UpdateFindMatches();
        FindBar.FindNextRequested   += (_, _) => FindNext();
        FindBar.FindPreviousRequested += (_, _) => FindPrev();
        FindBar.ReplaceRequested    += (_, _) => ExecuteReplace();
        FindBar.ReplaceAllRequested += (_, _) => ExecuteReplaceAll();

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
        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _diagnosticsTimer.Tick += (_, _) => { _diagnosticsTimer.Stop(); RunDiagnostics(); RunFolding(); RunVariableIndex(); RunAsmSymbolIndex(); };
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            _lineNumberColorizer.ActiveDocumentLineNumber =
                Editor.Document.GetLineByOffset(Editor.CaretOffset).LineNumber;
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            Editor.TextArea.TextView.Redraw();
        };
        // AvalonEdit's built-in control-character boxes (e.g. "DC1", "GS") would otherwise
        // render before our generator gets a chance to show the actual C64 ROM glyph
        Editor.Options.ShowBoxForControlCharacters = false;
        Editor.TextArea.TextView.ElementGenerators.Add(new PetsciiGlyphGenerator());
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

        if (ViewModel.Project.IsOpen && Directory.Exists(ViewModel.Project.RootPath))
        {
            ViewModel.LoadFolder(ViewModel.Project.RootPath);
        }

        if (ViewModel.Settings.RestoreOpenTabsOnStartup)
        {
            // Skip missing files quietly - OpenFileByPath's own error dialog would otherwise
            // pop up once per moved/deleted file on every launch.
            foreach (string path in ViewModel.Settings.OpenTabPaths)
            {
                if (File.Exists(path))
                    OpenFileByPath(path);
            }
        }
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the view model backing the window's title and status bar.
    /// </summary>
    public MainViewModel ViewModel { get; } = new();

    // Command properties
    /// <summary>Gets the command that creates a new BASIC tab.</summary>
    public ICommand FileNewCommand { get; }
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
    /// <summary>Gets the command that opens the Minify dialog.</summary>
    public ICommand EditMinifyCommand    { get; }
    /// <summary>Gets the command that opens the Prettify dialog.</summary>
    public ICommand EditPrettifyCommand  { get; }
    /// <summary>Gets the command that opens the Renumber dialog.</summary>
    public ICommand EditRenumberCommand  { get; }
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

        ViewModel.Settings.Save();
        base.OnClosed(e);
    }

    /// <summary>
    /// Prompts to save any modified tabs before the window closes, cancelling the close if the
    /// user dismisses a save prompt.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
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
        base.OnClosing(e);
    }

    #endregion

    #region Private Methods

    #region File Operations

    private void FileNew_Click(object sender, RoutedEventArgs e) => CreateNewTab(EditorLanguage.Basic);

    private void FileNewAsm_Click(object sender, RoutedEventArgs e) => CreateNewTab(EditorLanguage.Asm);

    private void CreateNewTab(EditorLanguage language)
    {
        var tab = new EditorTab { Language = language };
        if (language == EditorLanguage.Asm)
        {
            tab.Kind = C64UFileKind.Asm;
            tab.DisplayName = "Untitled.asm";
        }

        ViewModel.OpenTabs.Add(tab);
        ActivateTab(tab);

        // ActivateTab already focuses the editor, but invoking via the File menu lets the
        // menu's own focus-restore logic run afterward and steal it back - defer one more
        // focus call so the editor keeps focus regardless of invocation path (menu or Ctrl+N).
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => Editor.Focus());
    }

    private void FileOpen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Commodore 64 Programs (*.prg)|*.prg|6502 Assembly (*.asm;*.s)|*.asm;*.s|All Files (*.*)|*.*",
            Title = "Open File"
        };
        if (dialog.ShowDialog() == true)
            OpenFileByPath(dialog.FileName);
    }

    // Dropping files from Explorer is only allowed when every dropped file is a .prg - the
    // drop is rejected as a whole (no partial-open) if any other file type is included.
    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        e.Effects = IsAllPrgFileDrop(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        e.Handled = true;
        if (!IsAllPrgFileDrop(e, out string[] paths)) return;

        foreach (string path in paths)
            OpenFileByPath(path);
    }

    private static bool IsAllPrgFileDrop(DragEventArgs e, out string[] paths)
    {
        paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        return paths.Length > 0 && paths.All(p => p.EndsWith(".prg", StringComparison.OrdinalIgnoreCase));
    }

    private void OpenFileByPath(string path)
    {
        // A disk image isn't text - it's ~175KB-800KB of binary bytes. Reading it as UTF-8 and
        // running it through AvalonEdit's colorizers/diagnostics would be pathologically slow
        // (looks like a hang) rather than throw, since decoding arbitrary binary as text usually
        // "succeeds" with garbage. Expand it in the Explorer tree to see its contents instead.
        if (FileClassifier.Classify(path, isFolder: false).IsDiskImageKind())
        {
            ViewModel.SetStatus("Disk images can't be opened as text - expand it in the Explorer tree to see the programs inside.", StatusType.Warning);
            return;
        }

        // If already open, activate that tab instead of opening a duplicate
        var existing = ViewModel.OpenTabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            ActivateTab(existing);
            return;
        }

        try
        {
            string text;
            byte[]? prgData = null;
            if (path.EndsWith(".prg", StringComparison.OrdinalIgnoreCase))
            {
                prgData = File.ReadAllBytes(path);
                var converter = new PrgConverter();
                text = PadLineNumbers(converter.ConvertFromPrg(prgData));
            }
            else
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }

            var tab = new EditorTab
            {
                FilePath = path,
                Kind = FileClassifier.Classify(path, isFolder: false, prgData != null ? () => prgData : null),
                Language = LanguageClassifier.Classify(path),
            };
            tab.Document.Text = text;
            ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            tab.IsModified = false; // reset any spurious change event from document setup
            TrackRecentFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error opening file: {ex.Message}",
                "Open File Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Opens a "virtual" file found inside a mounted .d64 image in the Folder Explorer tree -
    // its content is already in memory, so no disk read is needed beyond what already happened
    // when the disk image itself was expanded.
    private Task OpenLocalVirtualFileInEditor(FileTreeItem item)
    {
        if (item.Content == null) return Task.CompletedTask;

        // Virtual entries have no real FilePath to dedupe on, so use the disk image's own path
        // plus the entry's name as a stable identity instead - re-activate an already-open tab
        // rather than opening a duplicate.
        string sourceId = $"{item.SourcePath}!{item.Name}";
        var existingTab = ViewModel.OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
        if (existingTab != null)
        {
            ActivateTab(existingTab);
            return Task.CompletedTask;
        }

        try
        {
            string text = item.Kind == C64UFileKind.Prg
                ? PadLineNumbers(new PrgConverter().ConvertFromPrg(item.Content))
                : Encoding.UTF8.GetString(item.Content);

            var tab = new EditorTab { DisplayName = item.Name, VirtualSourceId = sourceId, Kind = item.Kind };
            tab.Document.Text = text;
            ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            tab.IsModified = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening file: {ex.Message}", "Open File Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private void LocalDiskEntryContextOpen_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) _ = OpenLocalVirtualFileInEditor(item);
    }

    // Reuses the same inline-rename UI as real files/folders (BeginInlineRename/CommitRename) -
    // the virtual entry still renders in the tree with the same RenameBox template, just routes
    // through CommitVirtualEntryRename instead of File.Move since it has no real FullPath.
    private void LocalDiskEntryContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) BeginInlineRename(item);
    }

    private void LocalDiskEntryContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item?.SourcePath == null) return;

        if (MessageBox.Show($"Permanently delete \"{item.Name}\" from this disk image?",
                "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            byte[] diskBytes = File.ReadAllBytes(item.SourcePath);
            var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).DeleteEntry(diskBytes, item.Name);
            File.WriteAllBytes(item.SourcePath, updated);

            string sourceId = $"{item.SourcePath}!{item.Name}";
            var openTab = ViewModel.OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
            if (openTab != null)
            {
                openTab.IsModified = false; // its source entry is gone; no need to save
                CloseTab(openTab);
            }

            RefreshDiskImageNode(item.SourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete \"{item.Name}\":\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // "Add File..." on a .d64/.d81 node itself - lets the user pick a local .prg (embedded as-is,
    // since a .prg file's bytes already are the on-disk PRG format, load address included) or
    // .bas (tokenized first via the same converter SaveFile uses) to add into the image.
    private void LocalDiskImageContextAddFile_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Commodore 64 Programs (*.prg)|*.prg|BASIC Source (*.bas)|*.bas|All Files (*.*)|*.*",
            Title = "Add File to Disk Image"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            byte[] prgData = dialog.FileName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)
                ? new PrgConverter().ConvertToPrg(File.ReadAllText(dialog.FileName, Encoding.UTF8))
                : File.ReadAllBytes(dialog.FileName);
            string entryName = Path.GetFileNameWithoutExtension(dialog.FileName);

            byte[] diskBytes = File.ReadAllBytes(item.FullPath);
            var kind = FileClassifier.Classify(item.FullPath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).AddEntry(diskBytes, entryName, C64UFileKind.Prg, prgData);
            File.WriteAllBytes(item.FullPath, updated);

            RefreshDiskImageNode(item.FullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not add file to disk image:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Reloads a disk image node's virtual children after its bytes were rewritten (add/delete/
    // rename/replace), so the tree reflects the change without a full explorer reload.
    private void RefreshDiskImageNode(string diskImagePath) => FindItemByPath(diskImagePath)?.RefreshChildren();

    private void FileSave_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab == null) return;
        if (ViewModel.ActiveTab.VirtualSourceId != null)
        {
            _ = SaveVirtualTabAsync(ViewModel.ActiveTab);
            return;
        }
        if (string.IsNullOrEmpty(ViewModel.ActiveTab.FilePath))
        {
            FileSaveAs_Click(sender, e);
            return;
        }
        SaveFile(ViewModel.ActiveTab.FilePath);
    }

    // Writes a virtual tab's (a program opened from inside a mounted .d64/.d81) edits back into
    // its source disk image, re-tokenizing BASIC or writing Asm source as plain text - mirrors
    // SaveFile's language check. Routes to a local file write or an FTP download/modify/re-upload
    // round trip depending on whether SourcePath is a local file or a C64 Ultimate remote path.
    private async Task SaveVirtualTabAsync(EditorTab tab)
    {
        var parts = tab.VirtualSourceId!.Split('!', 2);
        if (parts.Length != 2) return;
        string sourcePath = parts[0];
        string entryName = parts[1];

        byte[] newContent = tab.Language == EditorLanguage.Asm
            ? Encoding.UTF8.GetBytes(tab.Document.Text)
            : new PrgConverter().ConvertToPrg(tab.Document.Text);

        if (File.Exists(sourcePath))
        {
            try
            {
                byte[] diskBytes = File.ReadAllBytes(sourcePath);
                var kind = FileClassifier.Classify(sourcePath, isFolder: false);
                byte[] updated = DiskImage.ForKind(kind).ReplaceEntry(diskBytes, entryName, newContent);
                File.WriteAllBytes(sourcePath, updated);

                tab.IsModified = false;
                ViewModel.SetStatus("File saved.");
                RefreshDiskImageNode(sourcePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving to disk image: {ex.Message}", "Save File Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        if (ViewModel.C64UFtp == null)
        {
            MessageBox.Show(
                "Not connected to the C64 Ultimate, so this disk image can't be saved to. Connect in the C64U Explorer panel, then try again.",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            byte[] diskBytes = await ViewModel.C64UFtp.DownloadBytesAsync(sourcePath);
            var kind = FileClassifier.Classify(sourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).ReplaceEntry(diskBytes, entryName, newContent);
            await ViewModel.C64UFtp.UploadBytesAsync(sourcePath, updated);

            tab.IsModified = false;
            ViewModel.SetStatus("File saved.");
            await RefreshC64UNode(sourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving to disk image: {ex.Message}", "Save File Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FileSaveAs_Click(object sender, RoutedEventArgs e)
    {
        bool isAsm = ViewModel.ActiveTab?.Language == EditorLanguage.Asm;
        var dialog = new SaveFileDialog
        {
            Filter = isAsm
                ? "6502 Assembly (*.asm;*.s)|*.asm;*.s|All Files (*.*)|*.*"
                : "Commodore 64 Programs (*.prg)|*.prg|All Files (*.*)|*.*",
            Title = "Save File As",
            DefaultExt = isAsm ? ".asm" : ".prg",
            AddExtension = true
        };
        if (!string.IsNullOrEmpty(ViewModel.CurrentFilePath))
            dialog.FileName = Path.GetFileNameWithoutExtension(ViewModel.CurrentFilePath);

        if (dialog.ShowDialog() == true)
        {
            ViewModel.CurrentFilePath = dialog.FileName;
            if (ViewModel.ActiveTab != null)
                ViewModel.ActiveTab.Language = LanguageClassifier.Classify(ViewModel.CurrentFilePath!);
            SaveFile(ViewModel.CurrentFilePath!);
            RefreshExplorerForSavedFile(ViewModel.CurrentFilePath!);
        }
    }

    // Writes the active tab's content to filePath - plain text for assembly source, or
    // BASIC-tokenized PRG bytes otherwise. Must check the tab's language rather than always
    // tokenizing, or saving a .asm file would silently overwrite it with binary PRG data.
    private void SaveFile(string filePath)
    {
        try
        {
            if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm)
            {
                File.WriteAllText(filePath, Editor.Text, Encoding.UTF8);
                ViewModel.IsModified = false;
                TrackRecentFile(filePath);
                ViewModel.SetStatus("File saved.");
            }
            else
            {
                var converter = new PrgConverter();
                var prgData = converter.ConvertToPrg(Editor.Text);
                File.WriteAllBytes(filePath, prgData);
                ViewModel.IsModified = false;
                TrackRecentFile(filePath);
                ViewModel.SetStatus($"File saved: {prgData.Length:N0} tokenized bytes.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error saving file: {ex.Message}",
                "Save File Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // After a Save/Save As writes a file to a brand-new path, refresh just the affected
    // explorer folder (or root) so it appears without collapsing other expanded folders,
    // and select/focus it since it's the active tab.
    private void RefreshExplorerForSavedFile(string filePath)
    {
        string folder = ViewModel.Project.RootPath;
        if (string.IsNullOrEmpty(folder)) return;

        string? dir = Path.GetDirectoryName(filePath);
        if (dir == null) return;

        string normalizedFolder = folder.TrimEnd(Path.DirectorySeparatorChar);
        bool isFolderItself = string.Equals(dir, normalizedFolder, StringComparison.OrdinalIgnoreCase);
        bool isNestedInFolder = dir.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!isFolderItself && !isNestedInFolder) return;

        RefreshAfterCreate(dir, filePath);
    }

    // Saves a tab (possibly not the active one); prompts for path if untitled.
    // Returns false if the user cancels.
    private bool SaveTabWithDialog(EditorTab tab)
    {
        bool isAsm = tab.Language == EditorLanguage.Asm;
        if (string.IsNullOrEmpty(tab.FilePath))
        {
            var dialog = new SaveFileDialog
            {
                Filter = isAsm
                    ? "6502 Assembly (*.asm;*.s)|*.asm;*.s|All Files (*.*)|*.*"
                    : "Commodore 64 Programs (*.prg)|*.prg|All Files (*.*)|*.*",
                Title = "Save File",
                DefaultExt = isAsm ? ".asm" : ".prg",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true) return false;
            tab.FilePath = dialog.FileName;
            tab.Language = LanguageClassifier.Classify(tab.FilePath);
            isAsm = tab.Language == EditorLanguage.Asm;
        }
        try
        {
            if (isAsm)
            {
                File.WriteAllText(tab.FilePath, tab.Document.Text, Encoding.UTF8);
                tab.IsModified = false;
                TrackRecentFile(tab.FilePath);
                RefreshExplorerForSavedFile(tab.FilePath);
                ViewModel.SetStatus("File saved.");
            }
            else
            {
                var converter = new PrgConverter();
                var prgData = converter.ConvertToPrg(tab.Document.Text);
                File.WriteAllBytes(tab.FilePath, prgData);
                tab.IsModified = false;
                TrackRecentFile(tab.FilePath);
                RefreshExplorerForSavedFile(tab.FilePath);
                ViewModel.SetStatus($"File saved: {prgData.Length:N0} tokenized bytes.");
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving file: {ex.Message}", "Save File Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void FileExport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Editor.Text))
        {
            MessageBox.Show("There is no code to export.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Export as Text File",
            DefaultExt = ".txt",
            AddExtension = true
        };
        if (!string.IsNullOrEmpty(ViewModel.CurrentFilePath))
            dialog.FileName = Path.GetFileNameWithoutExtension(ViewModel.CurrentFilePath);

        if (dialog.ShowDialog() == true)
        {
            try { File.WriteAllText(dialog.FileName, Editor.Text, Encoding.UTF8); }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting file: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FileImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Import Text File"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var tab = new EditorTab();
                tab.Document.Text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                ViewModel.OpenTabs.Add(tab);
                ActivateTab(tab);
                tab.IsModified = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing file: {ex.Message}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FileExit_Click(object sender, RoutedEventArgs e) => Close();

    #endregion

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

    // Gates Cut/Copy, which need an actual text selection rather than just non-empty content.
    private bool HasSelection() => ViewModel.ActiveTab != null && Editor.SelectionLength > 0;

    // Gates Close Folder, which only makes sense once a folder has been opened.
    private bool HasFolderOpen() => ViewModel.Project.IsOpen;

    // Gates Reopen Closed Tab, which only makes sense once a tab has actually been closed.
    private bool HasClosedTabHistory() => _closedTabHistory.Count > 0;

    #region Tab Management

    private void ActivateTab(EditorTab? tab)
    {
        // Persist outgoing tab's caret, scroll position, and collapsed-fold state
        if (ViewModel.ActiveTab != null && !ReferenceEquals(ViewModel.ActiveTab, tab))
        {
            ViewModel.ActiveTab.CaretOffset = Editor.CaretOffset;
            ViewModel.ActiveTab.ScrollOffsetY = Editor.VerticalOffset;
            SaveFoldingState(ViewModel.ActiveTab);
        }

        // FoldingManager is bound to whichever document was on the TextArea at Install time and
        // must be uninstalled before the shared Editor control is rebound to a different one -
        // there's no "reinstall"/rebind API, so a fresh manager is created per activation below.
        if (_foldingManager != null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

        _activatingTab = true;
        ViewModel.ActiveTab = tab;

        if (tab != null)
        {
            // Assigning Editor.Document raises AvalonEdit's TextChanged event (a new document
            // counts as the visible text changing), so the guard must still be up here -
            // otherwise Editor_TextChanged marks the freshly activated tab as modified.
            Editor.Document = tab.Document;
            ApplyLineTransformersForLanguage(tab.Language);
            Editor.CaretOffset = Math.Min(tab.CaretOffset, tab.Document.TextLength);
            Editor.ScrollToVerticalOffset(tab.ScrollOffsetY);
            Editor.Focus();

            if (ViewModel.Settings.EnableCodeFolding)
            {
                _foldingManager = FoldingManager.Install(Editor.TextArea);
                RunFolding();
                foreach (var fs in _foldingManager.AllFoldings)
                    fs.IsFolded = tab.CollapsedFoldStartOffsets.Contains(fs.StartOffset);
            }

            RunVariableIndex();
            RunAsmSymbolIndex();
        }
        else
        {
            ViewModel.Variables.Clear();
            ViewModel.Symbols.Clear();
            _lastAsmResult = null;
        }

        _activatingTab = false;

        // No open tabs -> show the empty-state image instead of the (now contentless) editor.
        Editor.Visibility = tab != null ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateImage.Visibility = tab != null ? Visibility.Collapsed : Visibility.Visible;

        _tabSwitching = true;
        TabBar.SelectedItem = tab;
        _tabSwitching = false;

        // Selecting an item doesn't reliably auto-scroll it into view with a plain horizontal
        // StackPanel as the tab bar's items panel, so scroll explicitly - covers activation from
        // the overflow menu, keyboard shortcuts, etc., not just clicking a visible tab.
        if (tab != null)
            TabBar.ScrollIntoView(tab);
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
        }
        else
        {
            transformers.Add(_lineNumberColorizer);
            transformers.Add(_keywordColorizer);
            transformers.Add(_numberLiteralColorizer);
            transformers.Add(_stringLiteralColorizer);
            transformers.Add(_dataLiteralColorizer);
            transformers.Add(_remCommentColorizer);

            Editor.TextArea.LeftMargins.Remove(_asmLineNumberMargin);
        }

        transformers.Add(_findHighlightColorizer);

        Editor.FontFamily = language == EditorLanguage.Asm ? _asmEditorFont : _basicEditorFont;

        bool isAsm = language == EditorLanguage.Asm;
        VariablesPanel.Visibility = isAsm ? Visibility.Collapsed : Visibility.Visible;
        SymbolsPanel.Visibility = isAsm ? Visibility.Visible : Visibility.Collapsed;

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
        if (snapshot.FilePath != null) tab.Language = LanguageClassifier.Classify(snapshot.FilePath);
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

    #endregion

    #region Edit Operations

    private void EditUndo_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.CanUndo)
        {
            Editor.Undo();
        }
    }

    private void EditRedo_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.CanRedo)
        {
            Editor.Redo();
        }
    }

    private void EditCut_Click(object sender, RoutedEventArgs e)
    {
        Editor.Cut();
    }

    private void EditCopy_Click(object sender, RoutedEventArgs e)
    {
        Editor.Copy();
    }

    private void EditPaste_Click(object sender, RoutedEventArgs e)
    {
        Editor.Paste();
    }

    private void EditDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.SelectionLength > 0)
        {
            Editor.SelectedText = string.Empty;
        }
    }

    private void EditSelectAll_Click(object sender, RoutedEventArgs e)
    {
        Editor.SelectAll();
    }

    // ── Chord shortcut: Ctrl+K → Ctrl+C / Ctrl+K → Ctrl+U ───────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
            }
            else if (Keyboard.Modifiers == ModifierKeys.None)
            {
                if (e.Key == Key.F) { CloseFolder(); e.Handled = true; return; }
            }
        }
    }

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

        int increment = ViewModel.Settings.AutoNumberIncrement;
        int padding   = ViewModel.Settings.LineNumberPadding;
        string renumbered = CodePrettifier.RenumberLines(doc.Text, increment, increment, padding);

        if (renumbered == doc.Text)
        {
            ViewModel.SetStatus("No changes — line numbers are already sequential.");
            return;
        }

        // Renumbering can't fix a reference to a line number that never existed - it's left
        // unchanged, so warn rather than silently applying a renumber with dangling references.
        int danglingCount = BasicDiagnostics.Analyze(renumbered)
            .Count(d => d.Message.EndsWith("does not exist."));
        if (danglingCount > 0)
        {
            var result = MessageBox.Show(
                $"{danglingCount} GOTO/GOSUB/THEN reference(s) point to line numbers that don't exist " +
                "and will be left unchanged. Apply the renumber anyway?",
                "Renumber Code", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        doc.BeginUpdate();
        try { doc.Text = renumbered; }
        finally { doc.EndUpdate(); }

        ViewModel.SetStatus("Code renumbered.");
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
                if (!BasicTokens.TryMatchKeyword(lineText, i, BasicTokens.WordKeywordsLongestFirst, out string keyword))
                { i++; continue; }

                if (string.Equals(keyword, "REM", StringComparison.OrdinalIgnoreCase))
                    return false; // rest of the line is a comment

                // "THEN 420" is CBM BASIC shorthand for "THEN GOTO 420" - an implied GOTO.
                bool isTarget =
                    string.Equals(keyword, "GOTO",  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(keyword, "THEN",  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(keyword, "GOSUB", StringComparison.OrdinalIgnoreCase);

                i += keyword.Length;
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

    #endregion

    #region C64U Context Menu

    private void EditorContextTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.C64UTransferCommand.CanExecute(null))
            ViewModel.C64UTransferCommand.Execute(null);
    }

    private void EditorContextGoToLineNumber_Click(object sender, RoutedEventArgs e)
    {
        if (GoToDefinitionCommand.CanExecute(null))
            GoToDefinitionCommand.Execute(null);
    }

    private void EditorContextRun_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.C64URunCommand.CanExecute(null))
            ViewModel.C64URunCommand.Execute(null);
    }

    private void EditorContextViceTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ViceTransferCommand.CanExecute(null))
            ViewModel.ViceTransferCommand.Execute(null);
    }

    private void EditorContextViceRun_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ViceRunCommand.CanExecute(null))
            ViewModel.ViceRunCommand.Execute(null);
    }

    #endregion

    #region Side Panels

    private void ExplorerToggle_Click(object sender, RoutedEventArgs e) => ActivateLeftPanel(ExplorerToggle, ExplorerPanel, "Explorer");

    // Deliberately does not auto-connect - opening the tab just shows the "Not connected" state
    // (with its "Connect" button) until the user explicitly asks to connect.
    private void C64UToggle_Click(object sender, RoutedEventArgs e) => ActivateLeftPanel(C64UToggle, C64UPanel, "C64U");

    // All left-panel toggle/panel/settings-key triples. Centralized so adding a new tab only
    // means adding one entry here rather than touching every call site that needs to
    // enumerate, save, or restore which left-panel tab is active.
    private IEnumerable<(ToggleButton Toggle, Grid Panel, string SettingsKey)> LeftPanelToggles => new (ToggleButton, Grid, string)[]
    {
        (ExplorerToggle, ExplorerPanel, "Explorer"),
        (C64UToggle,     C64UPanel,     "C64U"),
        (SearchToggle,   SearchPanel,   "Search"),
    };

    private void ActivateLeftPanel(ToggleButton toggle, Grid panel, string settingsKey)
    {
        if (toggle.IsChecked == true)
        {
            foreach (var (otherToggle, otherPanel, _) in LeftPanelToggles)
            {
                if (ReferenceEquals(otherToggle, toggle)) continue;
                otherToggle.IsChecked = false;
                otherPanel.Visibility = Visibility.Collapsed;
            }
            panel.Visibility = Visibility.Visible;

            if (LeftPanelCol.Width.Value == 0)
            {
                LeftPanelCol.Width = new GridLength(ViewModel.Settings.LeftPanelWidth);
                LeftSplitterCol.Width = new GridLength(4);
            }
            ViewModel.Settings.ActiveLeftPanel = settingsKey;
        }
        else
        {
            if (LeftPanelCol.Width.Value > 0)
                ViewModel.Settings.LeftPanelWidth = LeftPanelCol.Width.Value;
            panel.Visibility = Visibility.Collapsed;
            LeftPanelCol.Width = new GridLength(0);
            LeftSplitterCol.Width = new GridLength(0);
        }
        ViewModel.IsLeftPanelOpen = LeftPanelToggles.Any(t => t.Toggle.IsChecked == true);
    }

    // All right-panel toggle/panel/settings-key triples, in activity-bar order. Centralized so
    // adding a new tab only means adding one entry here rather than touching every call site
    // that needs to enumerate, save, or restore which right-panel tab is active.
    private IEnumerable<(ToggleButton Toggle, DockPanel Panel, string SettingsKey)> RightPanelToggles => new (ToggleButton, DockPanel, string)[]
    {
        (SpecialCharsToggle,   SpecialCharsPanel,   "QuickKeys"),
        (PetsciiToggle,        PetsciiPanel,        "Petscii"),
        (BasicKeywordsToggle,  BasicKeywordsPanel,  "BasicKeywords"),
        (AsmKeywordsToggle,    AsmKeywordsPanel,    "AsmKeywords"),
        (MusicNotesToggle,     MusicNotesPanel,     "MusicNotes"),
    };

    private void ActivateRightPanel(ToggleButton toggle, DockPanel panel)
    {
        if (toggle.IsChecked == true)
        {
            foreach (var (otherToggle, otherPanel, _) in RightPanelToggles)
            {
                if (ReferenceEquals(otherToggle, toggle)) continue;
                otherToggle.IsChecked = false;
                otherPanel.Visibility = Visibility.Collapsed;
            }
            panel.Visibility = Visibility.Visible;

            // Only restore the saved width when opening the panel from closed - switching
            // between already-open tabs must not clobber a width resized via the splitter
            // earlier this session.
            if (RightPanelCol.Width.Value == 0)
            {
                RightPanelCol.Width = new GridLength(ViewModel.Settings.RightPanelWidth);
                RightSplitterCol.Width = new GridLength(4);
            }
        }
        else
        {
            CloseRightPanel(panel);
        }
        ViewModel.IsRightPanelOpen = RightPanelToggles.Any(t => t.Toggle.IsChecked == true);
    }

    // Collapses a single right panel and reclaims its column width. Shared by
    // ActivateRightPanel's manual-toggle-off path and by language-driven auto-close
    // (e.g. switching to an Asm tab force-closes an open BASIC Keywords panel).
    private void CloseRightPanel(DockPanel panel)
    {
        if (RightPanelCol.Width.Value > 0)
            ViewModel.Settings.RightPanelWidth = RightPanelCol.Width.Value;
        panel.Visibility = Visibility.Collapsed;
        RightPanelCol.Width = new GridLength(0);
        RightSplitterCol.Width = new GridLength(0);
    }

    private void SpecialCharsToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(SpecialCharsToggle, SpecialCharsPanel);

    private void PetsciiToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(PetsciiToggle, PetsciiPanel);

    private void BasicKeywordsToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(BasicKeywordsToggle, BasicKeywordsPanel);

    private void AsmKeywordsToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(AsmKeywordsToggle, AsmKeywordsPanel);

    private void MusicNotesToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(MusicNotesToggle, MusicNotesPanel);

    #endregion

    #region C64U FTP Explorer

    private async void C64UConnect_Click(object sender, RoutedEventArgs e) => await ViewModel.ConnectToC64UAsync();

    private void C64USettingsHeader_Click(object sender, RoutedEventArgs e) => OpenSettingsDialog("c64u");

    private async void C64URefreshToolbar_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshC64UFolderAsync();

    private async void C64UNewFolderToolbar_Click(object sender, RoutedEventArgs e)
    {
        var selected = C64UFileTree.SelectedItem as C64UFileItem;
        C64UFileItem? parentFolder = selected == null ? null
            : selected.IsFolder ? selected
            : FindC64UParentFolder(selected);
        await CreateC64UNewFolderInlineAsync(parentFolder);
    }

    private async void C64UUploadToolbar_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.C64UFtp == null) return;

        var selected = C64UFileTree.SelectedItem as C64UFileItem;
        string targetDir = selected == null ? "/"
            : selected.IsFolder ? selected.FullPath
            : GetC64UParentPath(selected.FullPath);

        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog() != true) return;

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            string remotePath = CombineC64UPath(targetDir, Path.GetFileName(dialog.FileName));
            await ViewModel.C64UFtp.UploadBytesAsync(remotePath, bytes);
            await RefreshC64UNode(targetDir);
            ViewModel.SetStatus($"Uploaded \"{Path.GetFileName(dialog.FileName)}\" to the C64 Ultimate.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not upload file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void C64UFileTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as C64UFileItem;
        if (item == null || item.IsFolder || !item.IsRunnable) return;
        _ = OpenC64UFileInEditorAsync(item);
        e.Handled = true;
    }

    private void C64UFileTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (C64UFileTree.SelectedItem is not C64UFileItem item || item.IsRenaming) return;

        if (e.Key == Key.F2)
        {
            BeginC64UInlineRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _ = DeleteC64UItemAsync(item);
            e.Handled = true;
        }
    }

    // ── Folder context menu ───────────────────────────────────────────────────

    private async void C64UFolderContextNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await CreateC64UNewFolderInlineAsync(item);
    }

    private async void C64UFolderContextNewD64_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await CreateC64UNewDiskImageInlineAsync(item, C64UFileKind.D64);
    }

    private async void C64UFolderContextNewD81_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await CreateC64UNewDiskImageInlineAsync(item, C64UFileKind.D81);
    }

    private async void C64UFolderContextRefresh_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await item.RefreshChildrenAsync();
    }

    // ── File context menu ─────────────────────────────────────────────────────

    private async void C64UFileContextOpen_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await OpenC64UFileInEditorAsync(item);
    }

    private async void C64UFileContextRun_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item == null || ViewModel.C64UFtp == null) return;

        try
        {
            byte[] prgData = await DownloadAsPrgAsync(item);
            await ViewModel.RunOnC64UAsync(prgData);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Run failed: {ex.Message}", StatusType.Error);
        }
    }

    private async void C64UFileContextLoad_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item == null || ViewModel.C64UFtp == null) return;

        try
        {
            byte[] prgData = await DownloadAsPrgAsync(item);
            await ViewModel.LoadOnC64UAsync(prgData);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Load failed: {ex.Message}", StatusType.Error);
        }
    }

    private async void C64UFileContextMountA_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await ViewModel.MountC64UDriveAsync("a", item.FullPath);
    }

    private async void C64UFileContextMountB_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await ViewModel.MountC64UDriveAsync("b", item.FullPath);
    }

    private async void C64UEjectDriveA_Click(object sender, RoutedEventArgs e) => await ViewModel.EjectC64UDriveAsync("a");

    private async void C64UEjectDriveB_Click(object sender, RoutedEventArgs e) => await ViewModel.EjectC64UDriveAsync("b");

    private async void C64UFileContextDownload_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item == null || ViewModel.C64UFtp == null) return;

        var dialog = new SaveFileDialog { FileName = item.Name };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var bytes = await ViewModel.C64UFtp.DownloadBytesAsync(item.FullPath);
            File.WriteAllBytes(dialog.FileName, bytes);
            ViewModel.SetStatus($"Downloaded \"{item.Name}\".");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not download file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // "Add File..." on a .d64/.d81 node itself - lets the user pick a local .prg (embedded as-is,
    // since a .prg file's bytes already are the on-disk PRG format) or .bas (tokenized first via
    // the same converter SaveFile uses) to add into the image mounted over FTP.
    private async void C64UDiskImageContextAddFile_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item == null || ViewModel.C64UFtp == null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Commodore 64 Programs (*.prg)|*.prg|BASIC Source (*.bas)|*.bas|All Files (*.*)|*.*",
            Title = "Add File to Disk Image"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            byte[] prgData = dialog.FileName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)
                ? new PrgConverter().ConvertToPrg(File.ReadAllText(dialog.FileName, Encoding.UTF8))
                : File.ReadAllBytes(dialog.FileName);
            string entryName = Path.GetFileNameWithoutExtension(dialog.FileName);

            byte[] diskBytes = await ViewModel.C64UFtp.DownloadBytesAsync(item.FullPath);
            var kind = FileClassifier.Classify(item.FullPath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).AddEntry(diskBytes, entryName, C64UFileKind.Prg, prgData);
            await ViewModel.C64UFtp.UploadBytesAsync(item.FullPath, updated);

            await RefreshC64UNode(item.FullPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not add file to disk image:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Reuses the same inline-rename UI as real files/folders (BeginC64UInlineRename/
    // CommitC64URename) - routes through CommitC64UVirtualEntryRenameAsync since a virtual entry
    // has no real FullPath to FTP-rename.
    private void C64UDiskEntryContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) BeginC64UInlineRename(item);
    }

    private async void C64UDiskEntryContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item?.SourcePath == null || ViewModel.C64UFtp == null) return;

        if (MessageBox.Show($"Permanently delete \"{item.Name}\" from this disk image?",
                "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            byte[] diskBytes = await ViewModel.C64UFtp.DownloadBytesAsync(item.SourcePath);
            var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).DeleteEntry(diskBytes, item.Name);
            await ViewModel.C64UFtp.UploadBytesAsync(item.SourcePath, updated);

            string sourceId = $"{item.SourcePath}!{item.Name}";
            var openTab = ViewModel.OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
            if (openTab != null)
            {
                openTab.IsModified = false; // its source entry is gone; no need to save
                CloseTab(openTab);
            }

            await RefreshC64UNode(item.SourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete \"{item.Name}\":\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Shared folder/file context menu ───────────────────────────────────────

    private void C64UContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) BeginC64UInlineRename(item);
    }

    private async void C64UContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetC64UContextItem(sender);
        if (item != null) await DeleteC64UItemAsync(item);
    }

    // ── Shared logic ──────────────────────────────────────────────────────────

    private async Task OpenC64UFileInEditorAsync(C64UFileItem item)
    {
        if (item.Content == null && ViewModel.C64UFtp == null) return;

        // Neither a real C64U FTP file nor a virtual disk-image entry has a local FilePath to
        // dedupe on, so use the FTP path (or, for a virtual entry, the disk image's own path
        // plus the entry's name) as a stable identity instead - re-activate an already-open tab
        // rather than opening a duplicate.
        string sourceId = item.IsVirtual ? $"{item.SourcePath}!{item.Name}" : item.FullPath;
        var existingTab = ViewModel.OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
        if (existingTab != null)
        {
            ActivateTab(existingTab);
            return;
        }

        try
        {
            var bytes = item.Content ?? await ViewModel.C64UFtp!.DownloadBytesAsync(item.FullPath);
            string text = item.Kind == C64UFileKind.Prg
                ? PadLineNumbers(new PrgConverter().ConvertFromPrg(bytes))
                : Encoding.UTF8.GetString(bytes);

            var tab = new EditorTab { DisplayName = item.Name, VirtualSourceId = sourceId, Kind = item.Kind };
            tab.Document.Text = text;
            ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            tab.IsModified = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening file: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Downloads the file and, for BASIC source, tokenizes it into PRG format ready to send to
    // the C64 Ultimate. Already-tokenized .prg files (including those found inside a mounted
    // .d64 image) pass through unchanged.
    private async Task<byte[]> DownloadAsPrgAsync(C64UFileItem item)
    {
        var bytes = item.Content ?? await ViewModel.C64UFtp!.DownloadBytesAsync(item.FullPath);
        if (item.Kind == C64UFileKind.Prg) return bytes;

        string text = Encoding.UTF8.GetString(bytes);
        return new PrgConverter().ConvertToPrg(ViewModel.PrepareCodeForTransfer(text));
    }

    private async Task DeleteC64UItemAsync(C64UFileItem item)
    {
        string kind = item.IsFolder ? "folder" : "file";
        string extra = item.IsFolder ? " and all its contents" : "";
        if (MessageBox.Show($"Permanently delete {kind} \"{item.Name}\"{extra} from the C64 Ultimate?",
                $"Delete {(item.IsFolder ? "Folder" : "File")}", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            if (item.IsFolder)
                await ViewModel.C64UFtp!.DeleteFolderAsync(item.FullPath);
            else
                await ViewModel.C64UFtp!.DeleteFileAsync(item.FullPath);

            await RefreshC64UNode(GetC64UParentPath(item.FullPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region C64U Inline Rename / New Folder

    private void BeginC64UInlineRename(C64UFileItem item)
    {
        item.IsRenaming = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            var tvi = FindC64UTreeViewItem(C64UFileTree, item);
            if (tvi == null) return;
            var box = FindVisualChild<TextBox>(tvi, "C64URenameBox");
            if (box == null) return;
            box.Focus();
            if (!item.IsFolder)
            {
                int dot = box.Text.LastIndexOf('.');
                box.Select(0, dot > 0 ? dot : box.Text.Length);
            }
            else
            {
                box.SelectAll();
            }
        });
    }

    private async Task CommitC64URename(TextBox box)
    {
        var item = box.DataContext as C64UFileItem;
        if (item == null || !item.IsRenaming) return;
        item.IsRenaming = false;

        if (item.IsNew)
        {
            if (item.Kind.IsDiskImageKind()) await CommitC64UNewDiskImage(item, box.Text.Trim(), item.Kind);
            else await CommitC64UNewFolder(item, box.Text.Trim());
            return;
        }

        if (item.IsVirtual)
        {
            await CommitC64UVirtualEntryRenameAsync(item, box.Text.Trim());
            return;
        }

        string newName = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        string parentPath = GetC64UParentPath(item.FullPath);
        string newPath = CombineC64UPath(parentPath, newName);

        try
        {
            await ViewModel.C64UFtp!.RenameAsync(item.FullPath, newPath);
            await RefreshC64UNode(parentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Renames a "virtual" entry (living only inside a disk image mounted over FTP) via
    // CommitC64URename's shared inline-rename flow - unlike a real remote file, there's no path
    // to FTP-rename, so this downloads the disk image, rewrites its directory-slot name field,
    // and re-uploads it.
    private async Task CommitC64UVirtualEntryRenameAsync(C64UFileItem item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name || item.SourcePath == null) return;

        try
        {
            byte[] diskBytes = await ViewModel.C64UFtp!.DownloadBytesAsync(item.SourcePath);
            var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).RenameEntry(diskBytes, item.Name, newName);
            await ViewModel.C64UFtp.UploadBytesAsync(item.SourcePath, updated);
            await RefreshC64UNode(item.SourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename \"{item.Name}\":\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelC64URename(TextBox box)
    {
        if (box.DataContext is not C64UFileItem item) return;
        item.IsRenaming = false;
        if (item.IsNew) RemoveC64UPendingItem(item);
    }

    private async void C64URenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)      { await CommitC64URename((TextBox)sender); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelC64URename((TextBox)sender);       e.Handled = true; }
    }

    private async void C64URenameBox_LostFocus(object sender, RoutedEventArgs e)
        => await CommitC64URename((TextBox)sender);

    // Inserts an editable, not-yet-created placeholder folder into the tree and puts it
    // straight into rename mode - mirrors the local Explorer's inline "new folder" flow.
    private async Task CreateC64UNewFolderInlineAsync(C64UFileItem? parentFolder)
    {
        string parentPath;
        ObservableCollection<C64UFileItem> targetCollection;

        if (parentFolder != null)
        {
            // Await the load (rather than just setting IsExpanded) so the folder's real
            // children are in place before we insert the pending item - IsExpanded's own
            // fire-and-forget load would otherwise wipe it out when it completes.
            if (!parentFolder.IsExpanded)
            {
                await parentFolder.LoadChildrenAsync();
                parentFolder.IsExpanded = true;
            }
            parentPath = parentFolder.FullPath;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentPath = "/";
            targetCollection = ViewModel.C64UFileItems;
        }

        var pendingItem = new C64UFileItem(parentPath);
        targetCollection.Insert(0, pendingItem);
        BeginC64UInlineRename(pendingItem);
    }

    private async Task CommitC64UNewFolder(C64UFileItem item, string folderName)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemoveC64UPendingItem(item);
        if (string.IsNullOrWhiteSpace(folderName)) return; // no name provided - create nothing

        string path = CombineC64UPath(parentPath, folderName);
        try
        {
            await ViewModel.C64UFtp!.CreateFolderAsync(path);
            await RefreshC64UNode(parentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create folder:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveC64UPendingItem(C64UFileItem item)
    {
        var parent = FindC64UParentFolder(item);
        if (parent != null) parent.Children.Remove(item);
        else ViewModel.C64UFileItems.Remove(item);
    }

    // Mirrors CreateC64UNewFolderInlineAsync for a blank disk image; the placeholder's Kind
    // (D64/D81) is threaded through so CommitC64URename knows to route it to CommitC64UNewDiskImage.
    private async Task CreateC64UNewDiskImageInlineAsync(C64UFileItem? parentFolder, C64UFileKind kind)
    {
        string parentPath;
        ObservableCollection<C64UFileItem> targetCollection;

        if (parentFolder != null)
        {
            if (!parentFolder.IsExpanded)
            {
                await parentFolder.LoadChildrenAsync();
                parentFolder.IsExpanded = true;
            }
            parentPath = parentFolder.FullPath;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentPath = "/";
            targetCollection = ViewModel.C64UFileItems;
        }

        var pendingItem = new C64UFileItem(parentPath, kind);
        targetCollection.Insert(0, pendingItem);
        BeginC64UInlineRename(pendingItem);
    }

    private async Task CommitC64UNewDiskImage(C64UFileItem item, string diskName, C64UFileKind kind)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemoveC64UPendingItem(item);
        if (string.IsNullOrWhiteSpace(diskName)) return; // no name provided - create nothing

        string extension = kind == C64UFileKind.D64 ? ".d64" : ".d81";
        string fileName = diskName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? diskName : diskName + extension;
        string path = CombineC64UPath(parentPath, fileName);

        try
        {
            byte[] blankImage = DiskImage.ForKind(kind).CreateBlankImage(Path.GetFileNameWithoutExtension(fileName));
            await ViewModel.C64UFtp!.UploadBytesAsync(path, blankImage);
            await RefreshC64UNode(parentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create disk image:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    // Reloads the item at the given remote path (or the root listing if the path is "/" or no
    // matching node is found), so newly created/renamed/deleted entries appear immediately.
    private async Task RefreshC64UNode(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            await ViewModel.RefreshC64UFolderAsync();
            return;
        }

        var item = FindC64UItemByPath(path);
        if (item != null)
            await item.RefreshChildrenAsync();
        else
            await ViewModel.RefreshC64UFolderAsync();
    }

    private static string GetC64UParentPath(string path)
    {
        string trimmed = path.TrimEnd('/');
        int idx = trimmed.LastIndexOf('/');
        return idx <= 0 ? "/" : trimmed[..idx];
    }

    private static string CombineC64UPath(string directory, string name)
    {
        string trimmed = directory.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/" + name : trimmed + "/" + name;
    }

    private static C64UFileItem? GetC64UContextItem(object sender)
    {
        var contextMenu = (sender as MenuItem)?.Parent as ContextMenu;
        return (contextMenu?.PlacementTarget as TreeViewItem)?.DataContext as C64UFileItem;
    }

    private C64UFileItem? FindC64UItemByPath(string path)
    {
        foreach (var item in ViewModel.C64UFileItems)
        {
            var found = SearchC64UTree(item, path);
            if (found != null) return found;
        }
        return null;
    }

    private static C64UFileItem? SearchC64UTree(C64UFileItem item, string path)
    {
        if (string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)) return item;
        foreach (var child in item.Children)
        {
            var found = SearchC64UTree(child, path);
            if (found != null) return found;
        }
        return null;
    }

    private C64UFileItem? FindC64UParentFolder(C64UFileItem target)
        => FindC64UParentFolderRecursive(ViewModel.C64UFileItems, target);

    private static C64UFileItem? FindC64UParentFolderRecursive(IEnumerable<C64UFileItem> items, C64UFileItem target)
    {
        foreach (var folder in items.Where(i => i.IsFolder))
        {
            if (folder.Children.Contains(target)) return folder;
            var found = FindC64UParentFolderRecursive(folder.Children, target);
            if (found != null) return found;
        }
        return null;
    }

    private static TreeViewItem? FindC64UTreeViewItem(ItemsControl container, C64UFileItem target)
    {
        foreach (var raw in container.Items)
        {
            var tvi = container.ItemContainerGenerator.ContainerFromItem(raw) as TreeViewItem;
            if (tvi == null) continue;
            if (raw == target) return tvi;
            var found = FindC64UTreeViewItem(tvi, target);
            if (found != null) return found;
        }
        return null;
    }

    #endregion

    #region Side Panels

    #region View Commands

    private void TogglePrimarySideBar()
    {
        ExplorerToggle.IsChecked = ExplorerToggle.IsChecked != true;
        ExplorerToggle_Click(this, new RoutedEventArgs());
    }

    private void ToggleSecondarySideBar()
    {
        bool currentlyOpen = SpecialCharsToggle.IsChecked == true || PetsciiToggle.IsChecked == true;
        if (currentlyOpen)
        {
            SpecialCharsToggle.IsChecked = false;
            PetsciiToggle.IsChecked = false;
            SpecialCharsToggle_Click(this, new RoutedEventArgs());
        }
        else
        {
            SpecialCharsToggle.IsChecked = true;
            SpecialCharsToggle_Click(this, new RoutedEventArgs());
        }
    }

    private void FocusExplorer()
    {
        if (ExplorerToggle.IsChecked != true)
        {
            ExplorerToggle.IsChecked = true;
            ExplorerToggle_Click(this, new RoutedEventArgs());
        }
        FileTree.Focus();
    }

    private void ShowCodeStatistics()
    {
        string text   = Editor.Text;
        int charCount = text.Length;
        int lineCount = Editor.Document.LineCount;
        int wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        string byteLabel, byteValue, byteDescription;

        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm)
        {
            var asmResult = new Asm6502Assembler().Assemble(text);
            byteLabel = "Assembled bytes";
            if (asmResult.Success)
            {
                byteValue = $"{asmResult.PrgBytes!.Length - 2:N0}";
                byteDescription = "Assembled bytes is the size of the machine code, excluding the 2-byte load address header.";
            }
            else
            {
                byteValue = "Assembly errors";
                byteDescription = "Fix the assembly errors in this file to see its assembled byte count.";
            }
        }
        else
        {
            var prgData = new PrgConverter().ConvertToPrg(text);
            int tokenBytes = prgData.Length - 2;  // subtract 2-byte load address header
            byteLabel = "Tokenized bytes";
            byteValue = $"{tokenBytes:N0} / 38,911";
            byteDescription = "Tokenized bytes is the C64 BASIC memory footprint (of 38,911 bytes available).";
        }

        var dlg = new CodeStatisticsWindow(charCount, wordCount, lineCount, byteLabel, byteValue, byteDescription) { Owner = this };
        dlg.ShowDialog();
    }

    #endregion

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

    #region Find / Replace

    private void OpenFind()
    {
        FindBar.Open(Editor.SelectedText, replaceMode: false);
        UpdateFindMatches();
    }

    private void OpenReplace()
    {
        FindBar.Open(Editor.SelectedText, replaceMode: true);
        UpdateFindMatches();
    }

    private void UpdateFindMatches()
    {
        _findMatches.Clear();
        string searchText = FindBar.SearchText;

        if (string.IsNullOrEmpty(searchText))
        {
            _findHighlightColorizer.Clear();
            Editor.TextArea.TextView.Redraw();
            FindBar.SetMatchCount(0, 0);
            return;
        }

        _findMatches.AddRange(ProjectSearcher.FindMatches(Editor.Document.Text, searchText, FindBar.MatchCase, FindBar.WholeWord, FindBar.UseRegex));

        _findMatchIndex = FindNearestMatchIndex(Editor.CaretOffset);
        _findHighlightColorizer.SetMatches(_findMatches, _findMatchIndex);
        Editor.TextArea.TextView.Redraw();
        FindBar.SetMatchCount(_findMatchIndex + 1, _findMatches.Count);
    }

    private int FindNearestMatchIndex(int caretOffset)
    {
        if (_findMatches.Count == 0) return -1;
        for (int i = 0; i < _findMatches.Count; i++)
            if (_findMatches[i].Offset >= caretOffset) return i;
        return 0;
    }

    private void FindNext()
    {
        if (_findMatches.Count == 0) return;
        _findMatchIndex = (_findMatchIndex + 1) % _findMatches.Count;
        NavigateToCurrentMatch();
    }

    private void FindPrev()
    {
        if (_findMatches.Count == 0) return;
        _findMatchIndex = (_findMatchIndex - 1 + _findMatches.Count) % _findMatches.Count;
        NavigateToCurrentMatch();
    }

    private void NavigateToCurrentMatch()
    {
        if (_findMatchIndex < 0 || _findMatchIndex >= _findMatches.Count) return;
        var (offset, length) = _findMatches[_findMatchIndex];
        Editor.Select(offset, length);
        Editor.ScrollToLine(Editor.Document.GetLineByOffset(offset).LineNumber);
        _findHighlightColorizer.SetMatches(_findMatches, _findMatchIndex);
        Editor.TextArea.TextView.Redraw();
        FindBar.SetMatchCount(_findMatchIndex + 1, _findMatches.Count);
    }

    private void ExecuteReplace()
    {
        if (_findMatchIndex < 0 || _findMatchIndex >= _findMatches.Count)
        {
            UpdateFindMatches();
            if (_findMatches.Count == 0) return;
        }
        var (offset, length) = _findMatches[_findMatchIndex];
        Editor.Document.Replace(offset, length, FindBar.ReplaceText);
        UpdateFindMatches();
        NavigateToCurrentMatch();
    }

    private void ExecuteReplaceAll()
    {
        if (_findMatches.Count == 0) return;
        using (Editor.Document.RunUpdate())
        {
            for (int i = _findMatches.Count - 1; i >= 0; i--)
                Editor.Document.Replace(_findMatches[i].Offset, _findMatches[i].Length, FindBar.ReplaceText);
        }
        UpdateFindMatches();
    }

    #endregion

    #region Project Search

    private void SearchToggle_Click(object sender, RoutedEventArgs e) => ActivateLeftPanel(SearchToggle, SearchPanel, "Search");

    private void OpenProjectSearch(bool replaceMode)
    {
        if (SearchToggle.IsChecked != true)
        {
            SearchToggle.IsChecked = true;
            SearchToggle_Click(this, new RoutedEventArgs());
        }

        SearchReplaceExpandBtn.IsChecked = replaceMode;
        SearchReplaceRow.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        SearchReplaceExpandArrow.Text = replaceMode ? "▾" : "▸";

        SearchQueryBox.Focus();
        SearchQueryBox.SelectAll();
    }

    private void SearchReplaceExpandBtn_Checked(object sender, RoutedEventArgs e)
    {
        SearchReplaceExpandArrow.Text = "▾";
        SearchReplaceRow.Visibility = Visibility.Visible;
    }

    private void SearchReplaceExpandBtn_Unchecked(object sender, RoutedEventArgs e)
    {
        SearchReplaceExpandArrow.Text = "▸";
        SearchReplaceRow.Visibility = Visibility.Collapsed;
    }

    private void SearchQueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { RunProjectSearch(); e.Handled = true; }
    }

    // Runs an explicit (not live-as-you-type) search across every plain-text source file in the
    // open project, replacing ViewModel.SearchResults wholesale. Synchronous - project folders at
    // this app's scale don't need the Task.Run/cancellation machinery a larger IDE would.
    private void RunProjectSearch()
    {
        ViewModel.SearchResults.Clear();

        string root = ViewModel.Project.RootPath;
        string query = SearchQueryBox.Text;
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(query))
        {
            SearchStatusText.Text = string.IsNullOrEmpty(root) ? "Open a folder to search across its files." : "";
            return;
        }

        bool matchCase = SearchMatchCaseBtn.IsChecked == true;
        bool wholeWord = SearchWholeWordBtn.IsChecked == true;
        bool useRegex  = SearchRegexBtn.IsChecked == true;

        int totalMatches = 0;
        foreach (string path in ProjectSearcher.EnumerateSearchableFiles(root))
        {
            string? text = ProjectSearcher.ReadSearchableText(path);
            if (text == null) continue;

            // Matches the padding OpenFileByPath applies when detokenizing a .prg for display,
            // so a result's line/column here lines up with the offsets in the opened tab.
            if (path.EndsWith(".prg", StringComparison.OrdinalIgnoreCase))
                text = PadLineNumbers(text);

            var matches = ProjectSearcher.FindMatches(text, query, matchCase, wholeWord, useRegex);
            if (matches.Count == 0) continue;

            var lineStarts = ComputeLineStartOffsets(text);
            var fileResult = new ProjectSearchFileResult(path, Path.GetRelativePath(root, path));
            foreach (var (offset, length) in matches)
            {
                var (lineNumber, columnOffset, preview) = LocateMatch(text, lineStarts, offset);
                fileResult.Matches.Add(new ProjectSearchMatchInfo(fileResult, lineNumber, columnOffset, length, preview));
            }
            ViewModel.SearchResults.Add(fileResult);
            totalMatches += matches.Count;
        }

        SearchStatusText.Text = totalMatches == 0
            ? "No results found."
            : $"{totalMatches} result{(totalMatches == 1 ? "" : "s")} in {ViewModel.SearchResults.Count} file{(ViewModel.SearchResults.Count == 1 ? "" : "s")}.";
    }

    // Line start offsets (0-based char offset of each line's first character), used to turn a
    // match's absolute offset into a 1-based line number + in-line column without an O(n) scan
    // per match.
    private static List<int> ComputeLineStartOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') offsets.Add(i + 1);
        return offsets;
    }

    private static (int LineNumber, int ColumnOffset, string LinePreview) LocateMatch(string text, List<int> lineStartOffsets, int offset)
    {
        int idx = lineStartOffsets.BinarySearch(offset);
        if (idx < 0) idx = ~idx - 1;
        int lineStart = lineStartOffsets[idx];
        int lineEnd = idx + 1 < lineStartOffsets.Count ? lineStartOffsets[idx + 1] - 1 : text.Length;
        if (lineEnd < lineStart) lineEnd = lineStart;
        string lineText = text.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
        return (idx + 1, offset - lineStart, lineText.Trim());
    }

    // Selecting a row would otherwise auto-scroll the tree's horizontal ScrollViewer to bring
    // the (often ellipsis-truncated, wider-than-the-pane) row fully into view, snapping the list
    // back to its left edge - suppress that so the horizontal scroll position only ever moves via
    // the scrollbar itself.
    private void SearchResultsTree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        => e.Handled = true;

    // Double-clicking a match jumps to it, opening (or activating) its file first.
    private void SearchResultsTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchResultsTree.SelectedItem is not ProjectSearchMatchInfo match) return;

        OpenFileByPath(match.File.FilePath);
        int offset = Editor.Document.GetOffset(match.LineNumber, match.ColumnOffset + 1);
        Editor.Select(offset, match.MatchLength);
        Editor.ScrollToLine(match.LineNumber);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    // Replaces every current search result across all matching files, confirmed up front since
    // it touches files that may not even be open. Files already open as a tab go through the
    // AvalonEdit document (so undo/IsModified stay correct, same backwards-iteration trick as
    // ExecuteReplaceAll); files not open are rewritten directly on disk.
    private void SearchReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        int totalMatches = ViewModel.SearchResults.Sum(f => f.Matches.Count);
        if (totalMatches == 0) return;

        string replacement = SearchReplaceBox.Text;
        var result = MessageBox.Show(
            $"Replace {totalMatches} occurrence{(totalMatches == 1 ? "" : "s")} across {ViewModel.SearchResults.Count} file{(ViewModel.SearchResults.Count == 1 ? "" : "s")}?",
            "Replace All in Project", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        foreach (var fileResult in ViewModel.SearchResults.ToList())
        {
            var openTab = ViewModel.OpenTabs.FirstOrDefault(t =>
                string.Equals(t.FilePath, fileResult.FilePath, StringComparison.OrdinalIgnoreCase));

            if (openTab != null)
            {
                using (openTab.Document.RunUpdate())
                {
                    for (int i = fileResult.Matches.Count - 1; i >= 0; i--)
                    {
                        var m = fileResult.Matches[i];
                        int offset = openTab.Document.GetOffset(m.LineNumber, m.ColumnOffset + 1);
                        openTab.Document.Replace(offset, m.MatchLength, replacement);
                    }
                }
                openTab.IsModified = true;
                if (ReferenceEquals(openTab, ViewModel.ActiveTab))
                    UpdateFindMatches();
            }
            else
            {
                try
                {
                    string? text = ProjectSearcher.ReadSearchableText(fileResult.FilePath);
                    if (text == null) continue;
                    bool isPrg = fileResult.FilePath.EndsWith(".prg", StringComparison.OrdinalIgnoreCase);
                    if (isPrg) text = PadLineNumbers(text);

                    var lineStarts = ComputeLineStartOffsets(text);
                    var sb = new StringBuilder(text);
                    // Backwards, so each earlier (larger-offset) replacement doesn't shift the
                    // offsets - computed from lineStarts of the original text - that later,
                    // smaller-offset matches still need.
                    for (int i = fileResult.Matches.Count - 1; i >= 0; i--)
                    {
                        var m = fileResult.Matches[i];
                        int offset = lineStarts[m.LineNumber - 1] + m.ColumnOffset;
                        sb.Remove(offset, m.MatchLength);
                        sb.Insert(offset, replacement);
                    }
                    ProjectSearcher.WriteSearchableText(fileResult.FilePath, sb.ToString());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error updating {fileResult.RelativeDisplayPath}: {ex.Message}",
                        "Replace All Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        RunProjectSearch();
    }

    #endregion

    private void FileTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as FileTreeItem;
        // Disk images aren't openable as text (see OpenFileByPath) - leave the event unhandled,
        // same as a folder, so WPF's default double-click-to-toggle-expansion behavior runs instead.
        if (item == null || item.IsFolder || item.IsDiskImage) return;

        if (item.IsVirtual)
        {
            if (item.IsRunnable) _ = OpenLocalVirtualFileInEditor(item);
        }
        else
        {
            OpenFileByPath(item.FullPath);
        }
        e.Handled = true;
    }

    private void FileTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as FileTreeItem;
        // Virtual entries (found inside a mounted .d64) have no real path on disk, so they
        // can't be dragged/moved like a real file.
        _dragItem = item?.IsVirtual == true ? null : item;
    }

    private void FileTreeItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
        var delta = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop((DependencyObject)sender, _dragItem, DragDropEffects.Move);
    }

    private void FileTreeItem_DragOver(object sender, DragEventArgs e)
    {
        var target = (sender as TreeViewItem)?.DataContext as FileTreeItem;
        if (target == null || !target.IsFolder || _dragItem == null || !IsValidDrop(_dragItem, target))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (!ReferenceEquals(_currentDropTarget, target))
        {
            if (_currentDropTarget != null) _currentDropTarget.IsDropTarget = false;
            _currentDropTarget = target;
            target.IsDropTarget = true;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void FileTreeItem_DragLeave(object sender, DragEventArgs e)
    {
        var target = (sender as TreeViewItem)?.DataContext as FileTreeItem;
        if (target != null && ReferenceEquals(_currentDropTarget, target))
        {
            target.IsDropTarget = false;
            _currentDropTarget = null;
        }
    }

    private void FileTreeItem_Drop(object sender, DragEventArgs e)
    {
        if (_currentDropTarget != null) { _currentDropTarget.IsDropTarget = false; _currentDropTarget = null; }
        var targetFolder = (sender as TreeViewItem)?.DataContext as FileTreeItem;
        if (targetFolder == null || !targetFolder.IsFolder || _dragItem == null || !IsValidDrop(_dragItem, targetFolder))
        {
            _dragItem = null; e.Handled = true; return;
        }
        string itemName    = Path.GetFileName(_dragItem.FullPath);
        string destination = Path.Combine(targetFolder.FullPath, itemName);
        if ((_dragItem.IsFolder && Directory.Exists(destination)) || (!_dragItem.IsFolder && File.Exists(destination)))
        {
            MessageBox.Show($"A {(_dragItem.IsFolder ? "folder" : "file")} named \"{itemName}\" already exists in \"{targetFolder.Name}\".",
                "Move Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            _dragItem = null; e.Handled = true; return;
        }
        string movedFrom = _dragItem.FullPath;
        _dragItem = null;
        try
        {
            if (Directory.Exists(movedFrom)) Directory.Move(movedFrom, destination);
            else                             File.Move(movedFrom, destination);

            UpdateCurrentFilePathAfterMove(movedFrom, destination);
            RefreshAfterMove(movedFrom, targetFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not move \"{itemName}\":\n{ex.Message}", "Move Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    private void RootHeader_DragOver(object sender, DragEventArgs e)
    {
        if (_dragItem == null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        string? rootPath = ViewModel.Project.RootPath;
        if (string.IsNullOrEmpty(rootPath)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        // Block drop if already at root level
        if (string.Equals(Path.GetDirectoryName(_dragItem.FullPath), rootPath, StringComparison.OrdinalIgnoreCase))
        {
            e.Effects = DragDropEffects.None; e.Handled = true; return;
        }
        ExplorerHeaderBorder.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF3CD"));
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void RootHeader_DragLeave(object sender, DragEventArgs e)
    {
        ExplorerHeaderBorder.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8"));
    }

    private void RootHeader_Drop(object sender, DragEventArgs e)
    {
        ExplorerHeaderBorder.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8"));
        string? rootPath = ViewModel.Project.RootPath;
        if (_dragItem == null || string.IsNullOrEmpty(rootPath))
        {
            _dragItem = null; e.Handled = true; return;
        }
        if (string.Equals(Path.GetDirectoryName(_dragItem.FullPath), rootPath, StringComparison.OrdinalIgnoreCase))
        {
            _dragItem = null; e.Handled = true; return;
        }
        string itemName    = Path.GetFileName(_dragItem.FullPath);
        string destination = Path.Combine(rootPath, itemName);
        if ((_dragItem.IsFolder && Directory.Exists(destination)) || (!_dragItem.IsFolder && File.Exists(destination)))
        {
            MessageBox.Show($"A {(_dragItem.IsFolder ? "folder" : "file")} named \"{itemName}\" already exists in the root folder.",
                "Move Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            _dragItem = null; e.Handled = true; return;
        }
        string movedFrom = _dragItem.FullPath;
        _dragItem = null;
        try
        {
            if (Directory.Exists(movedFrom)) Directory.Move(movedFrom, destination);
            else                             File.Move(movedFrom, destination);
            UpdateCurrentFilePathAfterMove(movedFrom, destination);
            // Source parent is a subfolder — refresh it; root gets a full refresh
            var sourceParent = FindItemByPath(Path.GetDirectoryName(movedFrom)!);
            sourceParent?.RefreshChildren();
            ViewModel.RefreshRootItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not move \"{itemName}\":\n{ex.Message}", "Move Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    private void RootContextPaste_Click(object sender, RoutedEventArgs e)
    {
        string? rootPath = ViewModel.Project.RootPath;
        if (!string.IsNullOrEmpty(rootPath))
            PasteToFolder(rootPath);
    }

    private void PasteToFolder(string targetFolderPath)
    {
        if (!Clipboard.ContainsFileDropList()) return;
        var files = Clipboard.GetFileDropList();
        if (files.Count == 0) return;

        bool isCut = false;
        if (Clipboard.GetData("Preferred DropEffect") is System.IO.MemoryStream ms)
            isCut = ms.ReadByte() == 2;

        bool anyMoved = false;
        foreach (string? sourcePath in files)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            string itemName    = Path.GetFileName(sourcePath);
            string destination = Path.Combine(targetFolderPath, itemName);
            bool isFolder      = Directory.Exists(sourcePath);
            if ((isFolder && Directory.Exists(destination)) || (!isFolder && File.Exists(destination)))
            {
                MessageBox.Show($"A {(isFolder ? "folder" : "file")} named \"{itemName}\" already exists.",
                    "Paste Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }
            try
            {
                if (isCut)
                {
                    if (isFolder) Directory.Move(sourcePath, destination);
                    else          File.Move(sourcePath, destination);
                    UpdateCurrentFilePathAfterMove(sourcePath, destination);
                }
                else
                {
                    if (isFolder) CopyDirectoryRecursive(sourcePath, destination);
                    else          File.Copy(sourcePath, destination);
                }
                anyMoved = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not paste \"{itemName}\":\n{ex.Message}", "Paste Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (!anyMoved) return;
        if (isCut) Clipboard.Clear();
        ViewModel.RefreshRootItems();
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        foreach (string dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    private void UpdateCurrentFilePathAfterMove(string movedFrom, string destination)
    {
        string prefix = movedFrom.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var tab in ViewModel.OpenTabs)
        {
            if (string.IsNullOrEmpty(tab.FilePath)) continue;
            if (tab.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                tab.FilePath = destination + tab.FilePath[movedFrom.Length..];
            else if (string.Equals(tab.FilePath, movedFrom, StringComparison.OrdinalIgnoreCase))
                tab.FilePath = destination;
        }
    }

    private void RefreshAfterMove(string movedFrom, FileTreeItem targetFolder)
    {
        string sourceParentPath = Path.GetDirectoryName(movedFrom)!;
        bool sourceIsRoot = string.Equals(sourceParentPath, ViewModel.Project.RootPath,
            StringComparison.OrdinalIgnoreCase);

        if (sourceIsRoot)
        {
            // Remove the moved item directly from FolderItems to avoid a full reload
            var toRemove = ViewModel.FolderItems.FirstOrDefault(i =>
                string.Equals(i.FullPath, movedFrom, StringComparison.OrdinalIgnoreCase));
            if (toRemove != null) ViewModel.FolderItems.Remove(toRemove);
        }
        else
        {
            var sourceParent = FindItemByPath(sourceParentPath);
            sourceParent?.RefreshChildren();
        }

        targetFolder.RefreshChildren();
        targetFolder.IsExpanded = true;
    }

    private FileTreeItem? FindItemByPath(string path)
    {
        foreach (var item in ViewModel.FolderItems)
        {
            var found = SearchTree(item, path);
            if (found != null) return found;
        }
        return null;
    }

    private static FileTreeItem? SearchTree(FileTreeItem item, string path)
    {
        if (string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)) return item;
        foreach (var child in item.Children)
        {
            var found = SearchTree(child, path);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsValidDrop(FileTreeItem source, FileTreeItem target)
    {
        if (!target.IsFolder) return false;
        if (string.Equals(Path.GetDirectoryName(source.FullPath), target.FullPath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (source.IsFolder)
        {
            string srcPrefix = source.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string tgtPrefix = target.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (tgtPrefix.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private void ExplorerNewFile_Click(object sender, RoutedEventArgs e) => CreateNewFileInline(null);

    private void ExplorerNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var selected = FileTree.SelectedItem as FileTreeItem;
        FileTreeItem? parentFolder = selected == null ? null
            : selected.IsFolder ? selected
            : FindParentFolder(selected);
        CreateNewFolderInline(parentFolder);
    }

    private void ExplorerRefresh_Click(object sender, RoutedEventArgs e)
    {
        string folder = ViewModel.Project.RootPath;
        if (!string.IsNullOrEmpty(folder))
            ViewModel.LoadFolder(folder);
    }

    private void ExplorerCollapse_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in ViewModel.FolderItems)
            item.CollapseAll();
    }

    private void FolderContextNewFile_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) CreateNewFileInline(item);
    }

    private void FolderContextNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) CreateNewFolderInline(item);
    }

    private void FolderContextNewD64_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) CreateNewDiskImageInline(item, C64UFileKind.D64);
    }

    private void FolderContextNewD81_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) CreateNewDiskImageInline(item, C64UFileKind.D81);
    }

    private void FolderContextReveal_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", item.FullPath) { UseShellExecute = true });
    }

    private void FolderContextCut_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        SetClipboardFile(item.FullPath, cut: true);
    }

    private void FolderContextCopy_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        SetClipboardFile(item.FullPath, cut: false);
    }

    private void FolderContextPaste_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) PasteToFolder(item.FullPath);
    }

    private void FolderContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) BeginInlineRename(item);
    }

    private void FolderContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) DeleteFolder(item);
    }

    private void DeleteFolder(FileTreeItem item)
    {
        if (MessageBox.Show($"Permanently delete folder \"{item.Name}\" and all its contents?",
                "Delete Folder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            Directory.Delete(item.FullPath, recursive: true);
            ViewModel.LoadFolder(ViewModel.Project.RootPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete folder:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── File context menu ─────────────────────────────────────────────────────

    private void FileContextReveal_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
    }

    private void FileContextOpen_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) OpenFileByPath(item.FullPath);
    }

    private void FileContextCut_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        SetClipboardFile(item.FullPath, cut: true);
    }

    private void FileContextCopy_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        SetClipboardFile(item.FullPath, cut: false);
    }

    private void FileContextPaste_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        string? parent = Path.GetDirectoryName(item.FullPath);
        if (!string.IsNullOrEmpty(parent)) PasteToFolder(parent);
    }

    private void FileContextCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        Clipboard.SetText(item.FullPath);
    }

    private void FileContextCopyRelativePath_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item == null) return;
        string relative = Path.GetRelativePath(ViewModel.Project.RootPath, item.FullPath);
        Clipboard.SetText(relative);
    }

    private void FileContextRename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) BeginInlineRename(item);
    }

    private void FileContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem(sender);
        if (item != null) DeleteFile(item);
    }

    private void DeleteFile(FileTreeItem item)
    {
        if (MessageBox.Show($"Permanently delete \"{item.Name}\"?",
                "Delete File", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            File.Delete(item.FullPath);
            // Close any open tab for this file
            var openTab = ViewModel.OpenTabs.FirstOrDefault(t =>
                string.Equals(t.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase));
            if (openTab != null)
            {
                openTab.IsModified = false; // file is gone; no need to save
                CloseTab(openTab);
            }
            // Remove the node directly instead of reloading the whole tree, so other
            // expanded folders stay expanded.
            RemovePendingItem(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void SetClipboardFile(string path, bool cut)
    {
        var paths = new StringCollection { path };
        var data = new DataObject();
        data.SetFileDropList(paths);
        byte[] effect = { (byte)(cut ? 2 : 5), 0, 0, 0 }; // 2=move, 5=copy
        data.SetData("Preferred DropEffect", new System.IO.MemoryStream(effect));
        Clipboard.SetDataObject(data, true);
    }

    private static FileTreeItem? GetContextItem(object sender)
    {
        var contextMenu = (sender as MenuItem)?.Parent as ContextMenu;
        return (contextMenu?.PlacementTarget as TreeViewItem)?.DataContext as FileTreeItem;
    }

    // Shared by the root/folder/file Explorer context menus: only enable "Paste" when the
    // clipboard actually holds a cut/copied file or folder.
    private void PasteContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var pasteItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Header, "Paste"));
        if (pasteItem != null)
            pasteItem.IsEnabled = Clipboard.ContainsFileDropList();
    }

    #region Inline Rename

    private void BeginInlineRename(FileTreeItem item)
    {
        item.IsRenaming = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            var tvi = FindTreeViewItem(FileTree, item);
            if (tvi == null) return;
            var box = FindVisualChild<TextBox>(tvi, "RenameBox");
            if (box == null) return;
            box.Focus();
            if (!item.IsFolder)
            {
                int dot = box.Text.LastIndexOf('.');
                box.Select(0, dot > 0 ? dot : box.Text.Length);
            }
            else
            {
                box.SelectAll();
            }
        });
    }

    private void CommitRename(TextBox box)
    {
        var item = box.DataContext as FileTreeItem;
        if (item == null || !item.IsRenaming) return;
        item.IsRenaming = false;

        if (item.IsNew)
        {
            if (item.IsFolder) CommitNewFolder(item, box.Text.Trim());
            else if (item.Kind.IsDiskImageKind()) CommitNewDiskImage(item, box.Text.Trim(), item.Kind);
            else CommitNewFile(item, box.Text.Trim());
            return;
        }

        if (item.IsVirtual)
        {
            CommitVirtualEntryRename(item, box.Text.Trim());
            return;
        }

        string newName = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        string newPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, newName);
        try
        {
            if (item.IsFolder)
            {
                Directory.Move(item.FullPath, newPath);
            }
            else
            {
                File.Move(item.FullPath, newPath);
                foreach (var tab in ViewModel.OpenTabs)
                {
                    if (string.Equals(tab.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase))
                        tab.FilePath = newPath;
                }
            }
            // Refresh only the parent folder's children instead of the whole tree, so other
            // expanded folders stay expanded.
            string parentPath = Path.GetDirectoryName(item.FullPath)!;
            RefreshAfterCreate(parentPath, newPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Renames a "virtual" entry (living only inside a disk image) via CommitRename's shared
    // inline-rename flow - unlike a real file/folder, there's no path to File.Move, so this
    // rewrites the entry's directory-slot name field in the source disk image bytes instead.
    private void CommitVirtualEntryRename(FileTreeItem item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name || item.SourcePath == null) return;

        try
        {
            byte[] diskBytes = File.ReadAllBytes(item.SourcePath);
            var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).RenameEntry(diskBytes, item.Name, newName);
            File.WriteAllBytes(item.SourcePath, updated);
            RefreshDiskImageNode(item.SourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename \"{item.Name}\":\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelRename(TextBox box)
    {
        if (box.DataContext is not FileTreeItem item) return;
        item.IsRenaming = false;
        if (item.IsNew)
            RemovePendingItem(item);
    }

    // Inserts an editable, not-yet-created placeholder into the tree (at the boundary between
    // folders and files) and puts it straight into rename mode - mirrors VS Code's "new file"
    // flow instead of popping a separate name-entry dialog.
    private void CreateNewFileInline(FileTreeItem? parentFolder)
    {
        string parentDirectory;
        ObservableCollection<FileTreeItem> targetCollection;

        if (parentFolder != null)
        {
            parentDirectory = parentFolder.FullPath;
            parentFolder.IsExpanded = true;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentDirectory = ViewModel.Project.RootPath;
            if (string.IsNullOrEmpty(parentDirectory)) return;
            targetCollection = ViewModel.FolderItems;
        }

        int insertIndex = 0;
        while (insertIndex < targetCollection.Count && targetCollection[insertIndex].IsFolder)
            insertIndex++;

        var pendingItem = new FileTreeItem(parentDirectory, false, isNewPending: true);
        targetCollection.Insert(insertIndex, pendingItem);
        BeginInlineRename(pendingItem);
    }

    private void CommitNewFile(FileTreeItem item, string fileName)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemovePendingItem(item);
        if (string.IsNullOrWhiteSpace(fileName)) return; // no name provided - create nothing

        string path = Path.Combine(parentPath, fileName);
        try
        {
            File.WriteAllText(path, string.Empty);
            RefreshAfterCreate(parentPath, path);

            // Open the blank tab directly instead of routing through OpenFileByPath, which re-reads the
            // file from disk and, for .prg paths, runs it through the PRG binary parser - a freshly
            // created empty file is too small to be a valid PRG and would fail that parse.
            var tab = new EditorTab { FilePath = path, Language = LanguageClassifier.Classify(path) };
            ViewModel.OpenTabs.Add(tab);
            ActivateTab(tab);
            tab.IsModified = false;
            TrackRecentFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Mirrors CreateNewFileInline for a blank disk image; the placeholder's Kind (D64/D81) is
    // threaded through so CommitRename knows to route it to CommitNewDiskImage.
    private void CreateNewDiskImageInline(FileTreeItem? parentFolder, C64UFileKind kind)
    {
        string parentDirectory;
        ObservableCollection<FileTreeItem> targetCollection;

        if (parentFolder != null)
        {
            parentDirectory = parentFolder.FullPath;
            parentFolder.IsExpanded = true;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentDirectory = ViewModel.Project.RootPath;
            if (string.IsNullOrEmpty(parentDirectory)) return;
            targetCollection = ViewModel.FolderItems;
        }

        int insertIndex = 0;
        while (insertIndex < targetCollection.Count && targetCollection[insertIndex].IsFolder)
            insertIndex++;

        var pendingItem = new FileTreeItem(parentDirectory, isNewPending: true, kind);
        targetCollection.Insert(insertIndex, pendingItem);
        BeginInlineRename(pendingItem);
    }

    private void CommitNewDiskImage(FileTreeItem item, string diskName, C64UFileKind kind)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemovePendingItem(item);
        if (string.IsNullOrWhiteSpace(diskName)) return; // no name provided - create nothing

        string extension = kind == C64UFileKind.D64 ? ".d64" : ".d81";
        string fileName = diskName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? diskName : diskName + extension;
        string path = Path.Combine(parentPath, fileName);

        try
        {
            byte[] blankImage = DiskImage.ForKind(kind).CreateBlankImage(Path.GetFileNameWithoutExtension(fileName));
            File.WriteAllBytes(path, blankImage);
            RefreshAfterCreate(parentPath, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create disk image:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Mirrors CreateNewFileInline, just with the placeholder always sorted to the very top
    // (folders sort before files regardless of name, until the post-create reload re-sorts everything).
    private void CreateNewFolderInline(FileTreeItem? parentFolder)
    {
        string parentDirectory;
        ObservableCollection<FileTreeItem> targetCollection;

        if (parentFolder != null)
        {
            parentDirectory = parentFolder.FullPath;
            parentFolder.IsExpanded = true;
            targetCollection = parentFolder.Children;
        }
        else
        {
            parentDirectory = ViewModel.Project.RootPath;
            if (string.IsNullOrEmpty(parentDirectory)) return;
            targetCollection = ViewModel.FolderItems;
        }

        var pendingItem = new FileTreeItem(parentDirectory, true, isNewPending: true);
        targetCollection.Insert(0, pendingItem);
        BeginInlineRename(pendingItem);
    }

    private void CommitNewFolder(FileTreeItem item, string folderName)
    {
        string parentPath = item.FullPath; // FullPath holds the parent directory while pending
        RemovePendingItem(item);
        if (string.IsNullOrWhiteSpace(folderName)) return; // no name provided - create nothing

        string path = Path.Combine(parentPath, folderName);
        try
        {
            Directory.CreateDirectory(path);
            RefreshAfterCreate(parentPath, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create folder:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Reloads only the affected folder (instead of the whole tree via LoadFolder) so sibling
    // and ancestor folders stay expanded, then selects and focuses the newly created item once
    // its TreeViewItem exists.
    private void RefreshAfterCreate(string parentPath, string newItemPath)
    {
        var parentItem = FindItemByPath(parentPath);

        if (parentItem != null)
            parentItem.RefreshChildren();
        else
            ViewModel.RefreshRootItems();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            var newItem = FindItemByPath(newItemPath);

            if (newItem == null) return;
            
            var tvi = FindTreeViewItem(FileTree, newItem);
            
            if (tvi == null) return;
            
            tvi.IsSelected = true;
            tvi.BringIntoView();
            tvi.Focus();
        });
    }

    // Returns the folder containing target, or null if target is already at the root level.
    private FileTreeItem? FindParentFolder(FileTreeItem target)
    {
        if (ViewModel.FolderItems.Contains(target)) return null;
        return FindParentFolderRecursive(ViewModel.FolderItems, target);
    }

    private static FileTreeItem? FindParentFolderRecursive(IEnumerable<FileTreeItem> items, FileTreeItem target)
    {
        foreach (var folder in items.Where(i => i.IsFolder))
        {
            if (folder.Children.Contains(target)) return folder;
            var found = FindParentFolderRecursive(folder.Children, target);
            if (found != null) return found;
        }
        return null;
    }

    private void RemovePendingItem(FileTreeItem item)
    {
        if (ViewModel.FolderItems.Remove(item)) return;
        RemoveFromChildrenRecursive(ViewModel.FolderItems, item);
    }

    private static bool RemoveFromChildrenRecursive(IEnumerable<FileTreeItem> items, FileTreeItem target)
    {
        foreach (var folder in items.Where(i => i.IsFolder))
        {
            if (folder.Children.Remove(target)) return true;
            if (RemoveFromChildrenRecursive(folder.Children, target)) return true;
        }
        return false;
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)        { CommitRename((TextBox)sender); e.Handled = true; }
        else if (e.Key == Key.Escape)   { CancelRename((TextBox)sender); e.Handled = true; }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
        => CommitRename((TextBox)sender);

    private void FileTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FileTree.SelectedItem is not FileTreeItem item || item.IsRenaming) return;

        // Virtual entries (found inside a mounted .d64) have no real path on disk, so none of
        // the file-management shortcuts below apply to them.
        if (item.IsVirtual)
        {
            if (e.Key == Key.Return && item.IsRunnable)
            {
                _ = OpenLocalVirtualFileInEditor(item);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.F2)
        {
            BeginInlineRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (item.IsFolder) DeleteFolder(item); else DeleteFile(item);
            e.Handled = true;
        }
        else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetClipboardFile(item.FullPath, cut: true);
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetClipboardFile(item.FullPath, cut: false);
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PasteToFolder(item.IsFolder ? item.FullPath : Path.GetDirectoryName(item.FullPath) ?? item.FullPath);
            e.Handled = true;
        }
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl container, FileTreeItem target)
    {
        foreach (var raw in container.Items)
        {
            var tvi = container.ItemContainerGenerator.ContainerFromItem(raw) as TreeViewItem;
            if (tvi == null) continue;
            if (raw == target) return tvi;
            var found = FindTreeViewItem(tvi, target);
            if (found != null) return found;
        }
        return null;
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl container, VariableInfo target)
    {
        foreach (var raw in container.Items)
        {
            var tvi = container.ItemContainerGenerator.ContainerFromItem(raw) as TreeViewItem;
            if (tvi == null) continue;
            if (raw == target) return tvi;
            var found = FindTreeViewItem(tvi, target);
            if (found != null) return found;
        }
        return null;
    }

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

    // Shared by the Folder Explorer and C64U trees: selects and focuses whichever item was
    // actually right-clicked before its context menu opens, so the highlighted row always
    // matches what the menu is about to act on.
    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;
        item.IsSelected = true;
        item.Focus();
    }

    #endregion

    private void OpenFolderDialog()
    {
        var dialog = new OpenFolderDialog { Title = "Open Folder" };
        if (ViewModel.Project.IsOpen)
            dialog.InitialDirectory = ViewModel.Project.RootPath;

        if (dialog.ShowDialog(this) == true)
        {
            string path = dialog.FolderName;
            ViewModel.LoadFolder(path);
            ViewModel.Project.RootPath = path;
            ViewModel.Settings.Save();

            if (ExplorerToggle.IsChecked != true)
            {
                ExplorerToggle.IsChecked = true;
                LeftPanelCol.Width = new GridLength(ViewModel.Settings.LeftPanelWidth);
                LeftSplitterCol.Width = new GridLength(4);
            }
        }
    }

    private void CloseFolder()
    {
        if (!HasFolderOpen()) return;

        ViewModel.FolderItems.Clear();
        ViewModel.ExplorerTitle = "EXPLORER";
        ViewModel.Project.RootPath = "";
        ViewModel.Settings.Save();
    }

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

    #region Helper Methods

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
        // Don't update while a popup completion window is open.
        if (_completionWindow != null)
        {
            ClearGhostText();
            return;
        }

        var (_, word) = GetWordBeforeCaret();

        if (string.IsNullOrEmpty(word) || word.All(char.IsDigit))
        {
            ClearGhostText();
            return;
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

    /// <summary>
    /// Populates the PETSCII Reference panel with three groups of character cells:
    /// printable (32-127), graphics SET 1 (96-127 overlap shown separately via description),
    /// and graphics SET 2/3 (160-223). Each cell shows the Pet Me 64 glyph + CHR$() code
    /// and inserts the character when clicked.
    /// </summary>
    private void BuildPetsciiTable()
    {
        PetsciiTablePanel.Children.Clear();

        var petMe64  = _basicEditorFont;
        var segoeUi  = new FontFamily("Segoe UI");

        Brush R(string key) => (Brush)FindResource(key);

        var labelBg  = R("ThemePanelHeaderBg");
        var labelFg  = R("ThemePanelHeaderFg");
        var glyphFg  = R("ThemePetsciiGlyphFg");
        var hoverBg  = R("ThemePetsciiRowHoverBg");
        var rowBg0   = R("ThemePetsciiRowEvenBg");
        var rowBg1   = R("ThemePetsciiRowOddBg");
        var sepBrush = R("ThemePetsciiSeparator");
        var hdrBg    = R("ThemePetsciiHeaderBg");
        var codeFg   = R("ThemePetsciiCodeFg");
        var noteBg   = R("ThemePetsciiNoteBg");

        // null label = printable glyph; "" = undefined (no PRINT element); other = control chip label
        var controlLabels = new Dictionary<int, string>
        {
            [0]  = "", [1]  = "", [2]  = "", [3]  = "", [4]  = "",
            [5]  = "WHT",
            [6]  = "DISABLE SHIFT C=",
            [7]  = "ENABLE SHIFT C=",
            [8]  = "", [9]  = "", [10] = "", [11] = "", [12] = "",
            [13] = "RETURN",
            [14] = "LOWER CASE",
            [15] = "", [16] = "",
            [17] = "CRSR↓",
            [18] = "RVS ON",
            [19] = "CLR HOME",
            [20] = "INST DEL",
            [21] = "", [22] = "", [23] = "", [24] = "", [25] = "", [26] = "", [27] = "",
            [28] = "RED",
            [29] = "CRSR→",
            [30] = "GRN",
            [31] = "BLU",
            [32] = "SPACE",
            [128] = "",
            [129] = "ORANGE",
            [130] = "", [131] = "",
            [132] = "F7/8",
            [133] = "F1",
            [134] = "F3",
            [135] = "F5",
            [136] = "F7",
            [137] = "F2",
            [138] = "F4",
            [139] = "F6",
            [140] = "F8",
            [141] = "SHIFT RETURN",
            [142] = "UPPER CASE",
            [143] = "",
            [144] = "BLK",
            [145] = "CRSR↑",
            [146] = "RVS OFF",
            [147] = "CLR HOME",
            [148] = "INST DEL",
            [149] = "BROWN",
            [150] = "LT RED",
            [151] = "GRAY 1",
            [152] = "GRAY 2",
            [153] = "LT GREEN",
            [154] = "LT BLUE",
            [155] = "GRAY 3",
            [156] = "PUR",
            [157] = "←CRSR",
            [158] = "YEL",
            [159] = "CYN",
            [160] = "SPACE",
        };

        Border MakeRow(FrameworkElement? printElem, string codeText, Brush bg, bool clickable, int code)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

            if (printElem != null)
            {
                Grid.SetColumn(printElem, 0);
                grid.Children.Add(printElem);
            }

            var codeBlock = new TextBlock
            {
                Text = codeText,
                FontFamily = segoeUi,
                FontSize = 10,
                Foreground = codeFg,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(codeBlock, 1);
            grid.Children.Add(codeBlock);

            var border = new Border
            {
                Background = bg,
                BorderBrush = sepBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinHeight = 24,
                Child = grid
            };

            if (clickable)
            {
                border.Cursor = Cursors.Hand;
                border.MouseLeftButtonDown += (_, _) => InsertSpecialChar((char)code);
                border.MouseEnter += (_, _) => border.Background = hoverBg;
                border.MouseLeave += (_, _) => border.Background = bg;
            }

            return border;
        }

        // Header row
        var hdrGrid = new Grid();
        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        hdrGrid.Background = labelBg;

        var hdrPrint = new TextBlock { Text = "PRINT", FontFamily = segoeUi, FontSize = 10,
            FontWeight = FontWeights.Bold, Foreground = labelFg,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(hdrPrint, 0);
        hdrGrid.Children.Add(hdrPrint);

        var hdrChrs = new TextBlock { Text = "CHR$", FontFamily = segoeUi, FontSize = 10,
            FontWeight = FontWeights.Bold, Foreground = labelFg,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(hdrChrs, 1);
        hdrGrid.Children.Add(hdrChrs);

        PetsciiTablePanel.Children.Add(new Border
        {
            Background = hdrBg,
            BorderBrush = sepBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = 22,
            Child = hdrGrid
        });

        // Data rows 0–191
        int rowIdx = 0;

        for (int code = 0; code <= 191; code++)
        {
            Brush bg = (rowIdx++ % 2 == 0) ? rowBg0 : rowBg0;
            FrameworkElement? printElem;
            bool clickable;

            if (controlLabels.TryGetValue(code, out string? label))
            {
                if (string.IsNullOrEmpty(label))
                {
                    printElem = null;
                    clickable = false;
                }
                else
                {
                    printElem = new Border
                    {
                        Background = labelBg,
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(4, 1, 4, 1),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 2, 4, 2),
                        Child = new TextBlock
                        {
                            Text = label,
                            Foreground = labelFg,
                            FontFamily = segoeUi,
                            FontSize = 9,
                            FontWeight = FontWeights.SemiBold
                        }
                    };

                    clickable = true;
                }
            }
            else
            {
                byte sc = PetsciiScreenCodeMap.ToScreenCode((byte)code);
                string glyph = ((char)(0xE000 + sc)).ToString();

                printElem = new TextBlock
                {
                    Text = glyph,
                    FontFamily = petMe64,
                    FontSize = 14,
                    Foreground = glyphFg,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 2, 4, 2)
                };

                clickable = true;
            }

            PetsciiTablePanel.Children.Add(MakeRow(printElem, code.ToString(), bg, clickable, code));
        }

        // Footer notes for codes 192–255
        foreach (var note in new[]
        {
            "192–223: same as 96–127",
            "224–254: same as 160–190",
            "255: same as 126"
        })
        {
            PetsciiTablePanel.Children.Add(new Border
            {
                Background = noteBg,
                BorderBrush = sepBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = note,
                    FontFamily = segoeUi,
                    FontSize = 9,
                    Foreground = codeFg,
                    Margin = new Thickness(6, 3, 4, 3),
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }
    }

    private void BuildBasicKeywordsList() =>
        BuildKeywordsList(BasicKeywordsListPanel, BasicCompletionProvider.AllItems, BasicCompletionProvider.CategoryOrder);

    private void BuildAsmKeywordsList() =>
        BuildKeywordsList(AsmKeywordsListPanel, AsmCompletionProvider.AllItems, AsmCompletionProvider.CategoryOrder);

    // Renders a reference panel (BASIC Keywords or ASM Mnemonics) as a series of category
    // headers followed by name/description rows, shared by both languages' completion tables.
    private void BuildKeywordsList(StackPanel targetPanel, IReadOnlyList<KeywordCompletionData> allItems, IReadOnlyList<string> categoryOrder)
    {
        targetPanel.Children.Clear();

        Brush R(string key) => (Brush)FindResource(key);
        var nameFg   = R("ThemeFileFg");
        var descFg   = R("ThemeSpecialCharShortcutFg");
        var labelBg  = R("ThemePanelHeaderBg");
        var labelFg  = R("ThemePanelHeaderFg");
        var itemsByCategory = allItems.ToLookup(i => i.Category);

        foreach (var category in categoryOrder)
        {
            targetPanel.Children.Add(new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = labelBg,
                Foreground = labelFg,
                Margin = new Thickness(8, 8, 8, 4),
                Padding = new Thickness(8, 4, 0, 4),
                MinHeight = 22,
                VerticalAlignment = VerticalAlignment.Center
            });

            foreach (var item in itemsByCategory[category].OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase))
            {
                var row = new StackPanel { Margin = new Thickness(8, 0, 8, 6) };

                row.Children.Add(new TextBlock
                {
                    Text = item.Text,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = nameFg
                });

                row.Children.Add(new TextBlock
                {
                    Text = item.Description?.ToString() ?? string.Empty,
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = descFg,
                    Margin = new Thickness(0, 1, 0, 0)
                });
                targetPanel.Children.Add(row);
            }
        }
    }

    private void BuildMusicNotesTable()
    {
        MusicNotesGrid.RowDefinitions.Clear();
        MusicNotesGrid.Children.Clear();

        Brush R(string key) => (Brush)FindResource(key);
        var headerBg    = R("ThemePetsciiHeaderBg");
        var headerFg    = R("ThemeFileFg");
        var sepBrush    = R("ThemePetsciiSeparator");
        var rowEvenBg   = R("ThemePetsciiRowEvenBg");
        var cellFg      = R("ThemePetsciiCodeFg");
        var labelBg  = R("ThemePanelHeaderBg");
        var labelFg  = R("ThemePanelHeaderFg");

        MusicNotesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        MusicNotesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void AddHeaderCell(string text, int column, int row, int columnSpan, int rowSpan)
        {
            var border = new Border
            {
                Background = labelBg, //headerBg,
                BorderBrush = sepBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = labelFg, //headerFg,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 4, 4, 4)
                }
            };
            Grid.SetColumn(border, column);
            Grid.SetRow(border, row);
            Grid.SetColumnSpan(border, columnSpan);
            Grid.SetRowSpan(border, rowSpan);
            MusicNotesGrid.Children.Add(border);
        }

        AddHeaderCell("MUSICAL NOTE", column: 0, row: 0, columnSpan: 2, rowSpan: 1);
        AddHeaderCell("NOTE",   column: 0, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("OCTAVE", column: 1, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("OSCILLATOR FREQ (NTSC)", column: 2, row: 0, columnSpan: 3, rowSpan: 1);
        AddHeaderCell("OSCILLATOR FREQ (PAL)",  column: 5, row: 0, columnSpan: 3, rowSpan: 1);
        AddHeaderCell("DECIMAL", column: 2, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("HI",      column: 3, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("LOW",     column: 4, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("DECIMAL", column: 5, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("HI",      column: 6, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("LOW",     column: 7, row: 1, columnSpan: 1, rowSpan: 1);

        int gridRow = 2;
        foreach (var note in SidNoteProvider.AllNotes)
        {
            MusicNotesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            void AddCell(string text, int column)
            {
                var border = new Border
                {
                    Background = rowEvenBg,
                    BorderBrush = sepBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child = new TextBlock
                    {
                        Text = text,
                        FontSize = 9,
                        Foreground = cellFg,
                        TextAlignment = TextAlignment.Center,
                        Padding = new Thickness(3, 2, 3, 2)
                    }
                };
                
                Grid.SetColumn(border, column);
                Grid.SetRow(border, gridRow);
                MusicNotesGrid.Children.Add(border);
            }

            AddCell(note.Note.ToString(),        0);
            AddCell(note.Octave,                 1);
            AddCell(note.DecimalNtsc.ToString(), 2);
            AddCell(note.HiNtsc.ToString(),      3);
            AddCell(note.LowNtsc.ToString(),     4);
            AddCell(note.DecimalPal?.ToString() ?? "—", 5);
            AddCell(note.HiPal?.ToString()      ?? "—", 6);
            AddCell(note.LowPal?.ToString()     ?? "—", 7);

            gridRow++;
        }
    }

    private void TrackRecentFile(string path)
    {
        ViewModel.Settings.AddRecentFile(path);
        ViewModel.Settings.Save();
        RefreshRecentFiles();
    }

    private void RefreshRecentFiles()
    {
        RecentFilesMenuItem.Items.Clear();
        var files = ViewModel.Settings.RecentFiles;
        if (files.Count == 0)
        {
            RecentFilesMenuItem.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "(none)",
                IsEnabled = false
            });
            return;
        }
        foreach (string path in files)
        {
            string capturedPath = path;
            var item = new System.Windows.Controls.MenuItem
            {
                Header = Path.GetFileName(path),
                ToolTip = path
            };
            item.Click += (_, _) => OpenFileByPath(capturedPath);
            RecentFilesMenuItem.Items.Add(item);
        }
    }

    #endregion

    #region Event Handlers

    private void Editor_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateScreenPositionStatus();
    }

    private void Editor_CaretPositionChanged(object? sender, EventArgs e)
    {
        UpdateScreenPositionStatus();
        UpdateGhostText();
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        UpdateLineCountStatus();

        if (!_activatingTab && !ViewModel.IsModified)
            ViewModel.IsModified = true;

        // Debounced so a full re-analysis doesn't run on every keystroke; also covers tab
        // switches, since assigning Editor.Document in ActivateTab raises this same event.
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Start();
    }

    // Re-analyzes the active document and refreshes the squiggle underlines: for BASIC, undefined
    // GOTO/GOSUB/THEN targets, unmatched FOR/NEXT, unterminated strings, and duplicate line
    // numbers; for assembly, whatever Asm6502Assembler reports as errors.
    private void RunDiagnostics()
    {
        bool isAsm = ViewModel.ActiveTab?.Language == EditorLanguage.Asm;
        _currentDiagnostics = Editor.Document != null && ViewModel.Settings.EnableLinting
            ? (isAsm ? AsmDiagnostics.Analyze(Editor.Document.Text) : BasicDiagnostics.Analyze(Editor.Document.Text))
            : Array.Empty<EditorDiagnostic>();
        _errorSquiggleRenderer.SetDiagnostics(_currentDiagnostics);
        Editor.TextArea.TextView.Redraw();
    }

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
    private void ApplyCodeAnalysisSettings()
    {
        bool foldingEnabled = ViewModel.Settings.EnableCodeFolding;
        EditorTab? activeTab = ViewModel.ActiveTab;

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
        if (document == null || ViewModel.ActiveTab?.Language == EditorLanguage.Asm) { variables.Clear(); return; }

        var byName = VariableCrossReference.Analyze(document.Text)
            .GroupBy(r => r.Name, StringComparer.Ordinal);

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byName)
        {
            seenNames.Add(group.Key);

            var existing = variables.FirstOrDefault(v => v.Name == group.Key);
            if (existing == null)
            {
                existing = new VariableInfo(group.Key);
                int insertAt = 0;
                while (insertAt < variables.Count && string.CompareOrdinal(variables[insertAt].Name, group.Key) < 0)
                    insertAt++;
                variables.Insert(insertAt, existing);
            }

            existing.Occurrences.Clear();
            foreach (var reference in group.OrderBy(r => r.Offset))
            {
                var line = document.GetLineByOffset(reference.Offset);
                TryGetBasicLineNumber(document, line.LineNumber, out int basicLineNumber);
                existing.Occurrences.Add(new VariableOccurrenceInfo(line.LineNumber, basicLineNumber, reference.IsWrite));
            }
        }

        for (int i = variables.Count - 1; i >= 0; i--)
            if (!seenNames.Contains(variables[i].Name)) variables.RemoveAt(i);
    }

    // Re-scans the active document for every label/constant's definition/reference occurrences,
    // diffing the result into ViewModel.Symbols exactly like RunVariableIndex diffs into
    // ViewModel.Variables (same IsExpanded-preservation reasoning). Also caches the underlying
    // AssemblyResult in _lastAsmResult so the hover tooltip can show resolved label/constant
    // values without re-assembling on every mouse move. Assembly-specific - see RunVariableIndex
    // for BASIC's equivalent.
    private void RunAsmSymbolIndex()
    {
        var symbols = ViewModel.Symbols;
        var document = Editor.Document;
        if (document == null || ViewModel.ActiveTab?.Language != EditorLanguage.Asm)
        {
            symbols.Clear();
            _lastAsmResult = null;
            return;
        }

        var asmResult = new Asm6502Assembler().Assemble(document.Text);
        _lastAsmResult = asmResult;

        var byName = AsmSymbolIndex.Analyze(document.Text)
            .GroupBy(o => o.Name, StringComparer.Ordinal);

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byName)
        {
            seenNames.Add(group.Key);

            var existing = symbols.FirstOrDefault(s => s.Name == group.Key);
            if (existing == null)
            {
                existing = new AsmSymbolInfo(group.Key);
                int insertAt = 0;
                while (insertAt < symbols.Count && string.CompareOrdinal(symbols[insertAt].Name, group.Key) < 0)
                    insertAt++;
                symbols.Insert(insertAt, existing);
            }

            bool isConstant = group.Any(o => o.Kind == AsmSymbolKind.ConstantDefinition);
            existing.TypeBadge = isConstant ? "CONST" : "LABEL";
            existing.ValueText = isConstant
                ? (asmResult.Constants.TryGetValue(group.Key, out int constValue) ? FormatAsmSymbolValue(constValue) : null)
                : (asmResult.Labels.TryGetValue(group.Key, out ushort labelAddress) ? FormatAsmSymbolValue(labelAddress) : null);

            existing.Occurrences.Clear();
            foreach (var occurrence in group.OrderBy(o => o.LineNumber))
                existing.Occurrences.Add(new AsmSymbolOccurrenceInfo(occurrence.LineNumber, occurrence.Kind));
        }

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

        if (!IsValidVariableName(newName))
        {
            ViewModel.SetStatus($"\"{newName}\" isn't a valid BASIC variable name.", StatusType.Warning);
            return;
        }

        RenameVariable(variable.Name, newName);
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

    // Renames every occurrence of oldName (as currently grouped in the Variable Explorer) to
    // newName throughout the active document, as one grouped undo step, then refreshes the
    // Variable Explorer immediately (rather than waiting for the debounce timer) to reflect it.
    private void RenameVariable(string oldName, string newName)
    {
        var document = Editor.Document;
        if (document == null) return;

        var occurrences = VariableCrossReference.Analyze(document.Text)
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
        ViewModel.SetStatus($"Renamed {oldName} to {newName} ({occurrences.Count} occurrence{(occurrences.Count == 1 ? "" : "s")}).");
    }

    // Continuously re-clamps FolderTreeRow during an active drag so it can never grow large
    // enough to push the Variable Explorer's header past the bottom of the visible column -
    // RowDefinition's own MinHeight only bounds the row being shrunk, not the one being grown,
    // and VariablesRow's nominal "*" sizing doesn't protect it once a GridSplitter has touched it.
    private void ExplorerRowSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double maxFolderTreeHeight = ExplorerColumnGrid.ActualHeight - ExplorerRowSplitter.ActualHeight - VariablesRow.MinHeight;
        if (maxFolderTreeHeight >= FolderTreeRow.MinHeight && FolderTreeRow.Height.Value > maxFolderTreeHeight)
            FolderTreeRow.Height = new GridLength(maxFolderTreeHeight);
    }

    // Shows a hover tooltip over either a user variable ("(variable) {type} {name}") or a BASIC
    // keyword ("{Keyword} - {Description}", reusing the same descriptions as the BASIC Keywords
    // reference panel). No tooltip for text inside a string literal or REM comment, or for an
    // unquoted value in a DATA statement's argument list (those are literals, not references).
    private void Editor_MouseHover(object sender, MouseEventArgs e)
    {
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
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

        bool inString = false;
        bool inDataArgs = false;
        int rawStart = -1;
        int i = 0;

        while (i < lineText.Length)
        {
            char c = lineText[i];

            if (c == '"')
            {
                if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, out tooltipText))
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
                if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, out tooltipText))
                    return true;
                rawStart = -1;
                inDataArgs = false;
                i++;
                continue;
            }

            if (char.IsLetter(c))
            {
                if (BasicTokens.TryMatchKeyword(lineText, i, BasicTokens.WordKeywordsLongestFirst, out string keyword))
                {
                    if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, out tooltipText))
                        return true;
                    rawStart = -1;

                    if (col >= i && col < i + keyword.Length)
                        return TryGetKeywordTooltip(keyword, out tooltipText);

                    if (string.Equals(keyword, "REM", StringComparison.OrdinalIgnoreCase))
                        return false; // everything from here to end of line is a comment
                    if (string.Equals(keyword, "DATA", StringComparison.OrdinalIgnoreCase))
                        inDataArgs = true;

                    i += keyword.Length;
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

            if (rawStart >= 0 && TryClassifyRawRun(lineText, rawStart, i, col, inDataArgs, out tooltipText))
                return true;
            rawStart = -1;
            i++;
        }

        return rawStart >= 0 &&
               TryClassifyRawRun(lineText, rawStart, lineText.Length, col, inDataArgs, out tooltipText);
    }

    // Checks whether the raw (non-keyword) run [start, end) - extended by one trailing $ or %
    // if present - contains `col`. DATA-statement values are excluded (they're literals, not
    // variable references); everything else in range is reported as a variable.
    private static bool TryClassifyRawRun(string lineText, int start, int end, int col, bool inDataArgs,
        out string tooltipText)
    {
        tooltipText = "";

        if (end < lineText.Length && (lineText[end] == '$' || lineText[end] == '%'))
            end++;

        if (col < start || col >= end || inDataArgs) return false;

        string name = lineText.Substring(start, end - start);
        string typeLabel = name[^1] switch { '%' => "Integer", '$' => "String", _ => "Float" };
        tooltipText = $"(variable) {typeLabel} {name}";
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

    /// <summary>
    /// Pads each line's leading line number to the configured width, mirroring the
    /// padding Editor_PreviewKeyDown applies as the user types - imported .prg source
    /// otherwise comes back with its zero padding stripped (the format only stores numbers)
    /// </summary>
    private string PadLineNumbers(string sourceCode)
    {
        int padding = ViewModel.Settings.LineNumberPadding;
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

                    int padding       = ViewModel.Settings.LineNumberPadding;
                    string nextLabel  = padding > 0
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

    private void Editor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // C64 BASIC is upper case by default - force typed text to match. Assembly source case
        // is significant (labels, comments) - leave it to AvalonEdit's normal input handling.
        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm) return;

        e.Handled = true;

        string upperText = e.Text.ToUpperInvariant();
        int start = Editor.SelectionStart;
        int length = Editor.SelectionLength;

        Editor.Document.Replace(start, length, upperText);

        int caretOffset = start + upperText.Length;
        Editor.CaretOffset = caretOffset;
        Editor.Select(caretOffset, 0);

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

    #region UI Updates

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
        Editor.Options.ColumnRulerPosition = ViewModel.Settings.ShowColumnGuide ? Math.Max(1, ViewModel.Settings.ColumnGuideColumn) : -1;

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
        int wrapColumn = Math.Max(1, ViewModel.Settings.ColumnGuideColumn);
        int caretIndex = Editor.CaretOffset;

        int row = 1;
        int col = 1;

        for (int i = 0; i < caretIndex && i < Editor.Text.Length; i++)
        {
            if (Editor.Text[i] == '\n')
            {
                row++;
                col = 1;
            }
            else
            {
                col++;
                if (col > wrapColumn)
                {
                    row++;
                    col = 1;
                }
            }
        }

        ViewModel.ScreenPositionText = $"Col: {col}, Row {row}";
    }

    #endregion

    #region Special Characters

    private void SpecialChar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int code))
            InsertSpecialChar((char)code);
    }

    private void InsertSpecialChar(char ch)
    {
        int start = Editor.SelectionStart;
        int length = Editor.SelectionLength;
        Editor.Document.Replace(start, length, ch.ToString());
        int newOffset = start + 1;
        Editor.CaretOffset = newOffset;
        Editor.Select(newOffset, 0);
        Editor.Focus();
    }

    #endregion

    #endregion
}

