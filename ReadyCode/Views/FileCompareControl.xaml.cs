// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ReadyCode.Diff;
using ReadyCode.Editor;

namespace ReadyCode.Views;

/// <summary>
/// Read-only WPF UserControl showing the File Compare feature's diff view for a
/// <see cref="FileCompareResult"/> - a GitHub-style Split (side-by-side) or Unified (single-pane)
/// diff with color-coded changes, word-level highlighting, a gutter change-indicator strip,
/// Previous/Next hunk navigation, an "ignore whitespace" toggle, and folded (collapsed) long
/// unchanged runs. Hosted as a third mode alongside the ordinary text editor and hex grid - see
/// <c>MainWindow.ActivateTab</c> - and never used for editing, only viewing.
/// </summary>
public partial class FileCompareControl : UserControl
{
    #region Private Fields

    private static readonly FontFamily _asciiFont = new("Consolas");
    private static readonly FontFamily _petsciiFont =
        new(new Uri("pack://application:,,,/ReadyCode;component/Assets/Fonts/"), "./#Pet Me 64");

    private readonly DiffLineColorizer _leftColorizer = new() { Role = DiffPaneRole.Old };
    private readonly DiffLineColorizer _rightColorizer = new() { Role = DiffPaneRole.New };
    private readonly DiffLineColorizer _unifiedColorizer = new() { Role = DiffPaneRole.Unified };
    private readonly DiffPrefixMargin _unifiedPrefixMargin = new();

    // Renders PETSCII control/graphics bytes as their real C64 ROM glyphs instead of AvalonEdit's
    // default control-character boxes - same mechanism as MainWindow's own shared Editor, but each
    // pane here needs its own generator instance/IsAsmMode toggle since Left/Right/Unified can each
    // display a differently-styled file.
    private readonly PetsciiGlyphGenerator _leftPetscii = new();
    private readonly PetsciiGlyphGenerator _rightPetscii = new();
    private readonly PetsciiGlyphGenerator _unifiedPetscii = new();

    private FoldingManager? _leftFoldingManager;
    private FoldingManager? _rightFoldingManager;
    private FoldingManager? _unifiedFoldingManager;

    private FileCompareResult? _result;
    private bool _isUnified;
    private bool _ignoreWhitespace;
    private int _currentHunkIndex = -1;
    private bool _syncingScroll;
    private bool _leftSyncPending;
    private bool _rightSyncPending;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="FileCompareControl"/> class.
    /// </summary>
    public FileCompareControl()
    {
        InitializeComponent();

        LeftEditor.TextArea.TextView.LineTransformers.Add(_leftColorizer);
        RightEditor.TextArea.TextView.LineTransformers.Add(_rightColorizer);
        UnifiedEditor.TextArea.TextView.LineTransformers.Add(_unifiedColorizer);
        UnifiedEditor.TextArea.LeftMargins.Insert(0, _unifiedPrefixMargin);

        // AvalonEdit's built-in control-character boxes would otherwise render before the PETSCII
        // generator gets a chance to show the real C64 ROM glyph - same reasoning as MainWindow's
        // own Editor.Options.ShowBoxForControlCharacters = false.
        LeftEditor.Options.ShowBoxForControlCharacters = false;
        RightEditor.Options.ShowBoxForControlCharacters = false;
        UnifiedEditor.Options.ShowBoxForControlCharacters = false;
        LeftEditor.TextArea.TextView.ElementGenerators.Add(_leftPetscii);
        RightEditor.TextArea.TextView.ElementGenerators.Add(_rightPetscii);
        UnifiedEditor.TextArea.TextView.ElementGenerators.Add(_unifiedPetscii);

        LeftEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => ScheduleSyncScroll(LeftEditor, RightEditor, isLeftSource: true);
        RightEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => ScheduleSyncScroll(RightEditor, LeftEditor, isLeftSource: false);

        // Keeps the location pane's draggable viewport thumbs in sync with the editors' actual
        // scroll state - VisualLinesChanged also fires on resize/folding/document changes, which
        // can move ExtentHeight/ViewportHeight without a scroll offset change of their own.
        LeftEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => UpdateSplitViewport();
        LeftEditor.TextArea.TextView.VisualLinesChanged += (_, _) => UpdateSplitViewport();
        UnifiedEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => UpdateUnifiedViewport();
        UnifiedEditor.TextArea.TextView.VisualLinesChanged += (_, _) => UpdateUnifiedViewport();

        // The split thumb drives scrolling through LeftEditor - the existing scroll sync above
        // then carries it over to RightEditor, so there's exactly one place that decides "what
        // does scrolling to fraction X mean" rather than duplicating that math.
        SplitViewportThumb.ScrollRequested += fraction => ScrollToFraction(LeftEditor, fraction);
        UnifiedViewportThumb.ScrollRequested += fraction => ScrollToFraction(UnifiedEditor, fraction);
    }

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs when the user toggles Split/Unified or Ignore Whitespace, so the hosting tab can
    /// persist the new state (see <see cref="Models.EditorTab.CompareIsUnified"/> and
    /// <see cref="Models.EditorTab.CompareIgnoreWhitespace"/>).
    /// </summary>
    public event Action<bool, bool>? ViewStateChanged;

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads a computed comparison into the view, restoring the given view-mode/ignore-whitespace
    /// state (e.g. when reactivating a tab that was already showing a comparison).
    /// </summary>
    /// <param name="result">The computed diff to display.</param>
    /// <param name="isUnified">Whether to show the unified view rather than the default split view.</param>
    /// <param name="ignoreWhitespace">Whether whitespace-only differences should be ignored.</param>
    public void LoadResult(FileCompareResult result, bool isUnified, bool ignoreWhitespace)
    {
        _result = result;
        _isUnified = isUnified;
        _ignoreWhitespace = ignoreWhitespace;
        _currentHunkIndex = -1;

        SplitViewToggle.IsChecked = !isUnified;
        UnifiedViewToggle.IsChecked = isUnified;
        IgnoreWhitespaceToggle.IsChecked = ignoreWhitespace;

        Render();
    }

    #endregion

    #region Private Methods

    private void SplitViewToggle_Click(object sender, RoutedEventArgs e) => SetView(isUnified: false);

    private void UnifiedViewToggle_Click(object sender, RoutedEventArgs e) => SetView(isUnified: true);

    private void SetView(bool isUnified)
    {
        _isUnified = isUnified;
        SplitViewToggle.IsChecked = !isUnified;
        UnifiedViewToggle.IsChecked = isUnified;
        _currentHunkIndex = -1;
        Render();
        ViewStateChanged?.Invoke(_isUnified, _ignoreWhitespace);
    }

    private void IgnoreWhitespaceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null) return;

        _ignoreWhitespace = IgnoreWhitespaceToggle.IsChecked == true;
        _result = FileCompareEngine.Compute(
            _result.LeftName, _result.LeftText, _result.LeftIsAsciiStyled, _result.LeftWarning,
            _result.RightName, _result.RightText, _result.RightIsAsciiStyled, _result.RightWarning,
            _ignoreWhitespace);
        _currentHunkIndex = -1;
        Render();
        ViewStateChanged?.Invoke(_isUnified, _ignoreWhitespace);
    }

    private void PrevHunk_Click(object sender, RoutedEventArgs e) => NavigateHunk(-1);

    private void NextHunk_Click(object sender, RoutedEventArgs e) => NavigateHunk(1);

    private void NavigateHunk(int direction)
    {
        if (_result == null) return;

        var hunks = _isUnified ? _result.UnifiedHunkStartLines : _result.SplitHunkStartRows;
        if (hunks.Count == 0) return;

        _currentHunkIndex = ((_currentHunkIndex + direction) % hunks.Count + hunks.Count) % hunks.Count;
        int row = hunks[_currentHunkIndex];

        if (_isUnified)
        {
            UnifiedEditor.ScrollToLine(row + 1);
        }
        else
        {
            LeftEditor.ScrollToLine(row + 1);
            RightEditor.ScrollToLine(row + 1);
        }
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var manager in new[] { _leftFoldingManager, _rightFoldingManager, _unifiedFoldingManager })
            if (manager != null)
                foreach (var fold in manager.AllFoldings)
                    fold.IsFolded = false;

        // Deferred for the same reason Render()'s own initial viewport update is: unfolding
        // changes the document's rendered height, but AvalonEdit's actual layout pass - the one
        // that recomputes ExtentHeight/ViewportHeight to match - doesn't happen synchronously
        // inside the IsFolded setter, so reading it back immediately here would race that pass and
        // see stale values (the thumb wouldn't resize until something else, like a drag, forced
        // another read afterward). Posting at Loaded priority runs this after that layout settles.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            UpdateSplitViewport();
            UpdateUnifiedViewport();
        });
    }

    private void Render()
    {
        if (_result == null) return;

        SplitViewGrid.Visibility = _isUnified ? Visibility.Collapsed : Visibility.Visible;
        UnifiedViewGrid.Visibility = _isUnified ? Visibility.Visible : Visibility.Collapsed;

        LeftHeaderText.Text = _result.LeftName;
        RightHeaderText.Text = _result.RightName;
        UnifiedHeaderText.Text = $"{_result.LeftName} ↔ {_result.RightName}";

        bool hasLeftWarning = !string.IsNullOrEmpty(_result.LeftWarning);
        bool hasRightWarning = !string.IsNullOrEmpty(_result.RightWarning);
        WarningsPanel.Visibility = hasLeftWarning || hasRightWarning ? Visibility.Visible : Visibility.Collapsed;
        LeftWarningText.Visibility = hasLeftWarning ? Visibility.Visible : Visibility.Collapsed;
        LeftWarningText.Text = _result.LeftWarning;
        RightWarningText.Visibility = hasRightWarning ? Visibility.Visible : Visibility.Collapsed;
        RightWarningText.Text = _result.RightWarning;

        // CanCompare requires both files to share the same Kind, so in practice the two sides'
        // styling always agrees - the unified pane (which mixes both files' lines in one editor)
        // simply follows the left side's.
        var leftFont = _result.LeftIsAsciiStyled ? _asciiFont : _petsciiFont;
        var rightFont = _result.RightIsAsciiStyled ? _asciiFont : _petsciiFont;
        LeftEditor.FontFamily = leftFont;
        RightEditor.FontFamily = rightFont;
        UnifiedEditor.FontFamily = leftFont;

        // IsAsmMode = true skips PETSCII substitution entirely (assembly/plain source must never
        // be reinterpreted as PETSCII bytes) - the same predicate that already picks the font.
        _leftPetscii.IsAsmMode = _result.LeftIsAsciiStyled;
        _rightPetscii.IsAsmMode = _result.RightIsAsciiStyled;
        _unifiedPetscii.IsAsmMode = _result.LeftIsAsciiStyled;

        var oldLines = _result.SideBySide.OldText.Lines;
        var newLines = _result.SideBySide.NewText.Lines;
        var unifiedLines = _result.Unified.Lines;

        _leftColorizer.Lines = oldLines;
        _rightColorizer.Lines = newLines;
        _unifiedColorizer.Lines = unifiedLines;
        _unifiedPrefixMargin.Lines = unifiedLines;

        RebindDocument(LeftEditor, ref _leftFoldingManager, JoinLines(oldLines), _result.SplitCollapsedRuns);
        RebindDocument(RightEditor, ref _rightFoldingManager, JoinLines(newLines), _result.SplitCollapsedRuns);
        RebindDocument(UnifiedEditor, ref _unifiedFoldingManager, JoinLines(unifiedLines), _result.UnifiedCollapsedRuns);

        LeftMapStrip.Update(_result.LeftChangeRuns, [], oldLines.Count);
        RightMapStrip.Update([], _result.RightChangeRuns, newLines.Count);
        UnifiedMapStrip.Update(_result.UnifiedDeletedRuns, _result.UnifiedInsertedRuns, unifiedLines.Count);

        // Deferred rather than called inline: on the very first comparison of a session,
        // FileCompareControl itself is transitioning from Visibility.Collapsed (its default) to
        // Visible in this same call stack, so LeftEditor/RightEditor/UnifiedEditor have never been
        // through a real WPF layout pass yet - reading ExtentHeight/ViewportHeight right now would
        // race that pending layout and see 0, which makes the thumb's fraction default to 1 (fills
        // the whole track) until something else (e.g. a later scroll) recomputes it. Posting at
        // Loaded priority runs this after layout/render for the pass that made everything visible
        // has actually completed. Later renders (already-visible tab, already laid out) are
        // unaffected either way - one deferred frame is imperceptible.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            UpdateSplitViewport();
            UpdateUnifiedViewport();
        });

        int hunkCount = (_isUnified ? _result.UnifiedHunkStartLines : _result.SplitHunkStartRows).Count;
        HunkCountText.Text = hunkCount == 1 ? "1 change" : $"{hunkCount} changes";
    }

    // Drives the location pane's draggable thumb from the editor's actual scroll state - the
    // single source of truth the thumb displays regardless of which pane's wheel/thumb drag moved it.
    private void UpdateSplitViewport()
    {
        double extent = LeftEditor.ExtentHeight;
        double start = extent > 0 ? LeftEditor.VerticalOffset / extent : 0;
        double fraction = extent > 0 ? LeftEditor.ViewportHeight / extent : 1;
        SplitViewportThumb.UpdateViewport(start, fraction);
    }

    private void UpdateUnifiedViewport()
    {
        double extent = UnifiedEditor.ExtentHeight;
        double start = extent > 0 ? UnifiedEditor.VerticalOffset / extent : 0;
        double fraction = extent > 0 ? UnifiedEditor.ViewportHeight / extent : 1;
        UnifiedViewportThumb.UpdateViewport(start, fraction);
    }

    // Translates a location-pane drag's normalized [0,1] target back into a real scroll offset -
    // the thumb itself only knows fractions, not any particular editor's extent.
    private static void ScrollToFraction(TextEditor editor, double fraction) =>
        editor.ScrollToVerticalOffset(fraction * editor.ExtentHeight);

    private static string JoinLines(IReadOnlyList<DiffPiece> lines) =>
        string.Join(Environment.NewLine, lines.Select(l => l.Text ?? string.Empty));

    // FoldingManager can't be rebound to a new document (same constraint MainWindow's own
    // _foldingManager has - see ActivateTab), so each render tears down the previous manager and
    // installs a fresh one for the freshly rebuilt document.
    private static void RebindDocument(
        TextEditor editor, ref FoldingManager? foldingManager, string text,
        IReadOnlyList<(int Start, int Count)> collapsedRuns)
    {
        if (foldingManager != null)
        {
            FoldingManager.Uninstall(foldingManager);
            foldingManager = null;
        }

        editor.Document = new TextDocument(text);
        var manager = FoldingManager.Install(editor.TextArea);
        manager.UpdateFoldings(BuildFoldings(editor.Document, collapsedRuns), -1);
        foreach (var fold in manager.AllFoldings)
            fold.IsFolded = true;
        foldingManager = manager;
    }

    private static IEnumerable<NewFolding> BuildFoldings(TextDocument document, IReadOnlyList<(int Start, int Count)> runs)
    {
        foreach (var (start, count) in runs)
        {
            int startLineNumber = start + 1;
            int endLineNumber = start + count;
            if (startLineNumber < 1 || endLineNumber > document.LineCount || endLineNumber <= startLineNumber)
                continue;

            var startLine = document.GetLineByNumber(startLineNumber);
            var endLine = document.GetLineByNumber(endLineNumber);
            yield return new NewFolding(startLine.Offset, endLine.EndOffset)
            {
                Name = $" ⋯ {count} unchanged lines ⋯ "
            };
        }
    }

    // Mirrors scroll position between the split view's two panes, but deferred to a later
    // Dispatcher pass rather than done inline. TextView.ScrollOffsetChanged fires synchronously as
    // part of whatever's driving the scroll - including a ScrollBar Thumb's own DragDelta
    // processing - so calling ScrollToVerticalOffset on the *other* pane's editor from directly
    // inside that handler forces a layout pass on the same call stack as the Thumb's drag, which
    // was breaking its mouse capture entirely (the Thumb could be clicked but not dragged; the
    // mouse wheel was unaffected since it doesn't go through the Thumb's capture at all). Posting
    // the actual sync to run after the current input round trip avoids that reentrancy. The
    // pending-flag coalesces bursts of ScrollOffsetChanged events (e.g. every pixel of a drag or
    // every wheel tick) into a single deferred sync that reads the live offset when it finally runs,
    // rather than queuing one BeginInvoke per event.
    private void ScheduleSyncScroll(TextEditor source, TextEditor target, bool isLeftSource)
    {
        if (_syncingScroll) return;
        if (isLeftSource ? _leftSyncPending : _rightSyncPending) return;

        if (isLeftSource) _leftSyncPending = true; else _rightSyncPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (isLeftSource) _leftSyncPending = false; else _rightSyncPending = false;
            SyncScroll(source, target);
        });
    }

    private void SyncScroll(TextEditor source, TextEditor target)
    {
        if (_syncingScroll) return;
        if (Math.Abs(source.VerticalOffset - target.VerticalOffset) < 0.5) return;

        _syncingScroll = true;
        target.ScrollToVerticalOffset(source.VerticalOffset);
        _syncingScroll = false;
    }

    #endregion
}
