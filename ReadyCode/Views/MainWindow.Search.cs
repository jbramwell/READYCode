// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ReadyCode.Models;
using ReadyCode.Search;

namespace ReadyCode.Views;

// The single-tab Find / Replace bar and the project-wide Search panel.
public partial class MainWindow
{
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
        var previousMatches = _findMatches.Count > 0 ? _findMatches.ToArray() : null;
        _findMatches.Clear();
        string searchText = FindBar.SearchText;

        if (string.IsNullOrEmpty(searchText))
        {
            _findHighlightColorizer.Clear();
            if (previousMatches != null) RedrawMatches(previousMatches);
            FindBar.SetMatchCount(0, 0);
            return;
        }

        var pattern = SearchPattern.TryCreate(searchText, FindBar.MatchCase, FindBar.WholeWord, FindBar.UseRegex, out string? patternError);
        bool timedOut = false;
        if (pattern != null)
            _findMatches.AddRange(pattern.FindMatches(Editor.Document.Text, out timedOut));

        _findMatchIndex = FindNearestMatchIndex(Editor.CaretOffset);
        _findHighlightColorizer.SetMatches(Editor.Document, _findMatches, _findMatchIndex);
        if (previousMatches != null) RedrawMatches(previousMatches);
        RedrawMatches(_findMatches);

        if (patternError != null)
            FindBar.SetSearchProblem("Invalid regex", patternError);
        else if (timedOut)
            FindBar.SetSearchProblem("Regex too slow", _regexTooSlowMessage);
        else
            FindBar.SetMatchCount(_findMatchIndex + 1, _findMatches.Count);
    }

    // Redraws only the visual lines overlapping the given match segments, rather than
    // invalidating the entire document's cached visual-line set (TextEditor.TextArea.TextView
    // .Redraw() with no arguments). For a large document, that blanket invalidation forces
    // AvalonEdit to reconstruct every currently-visible line simultaneously on the next layout
    // pass instead of the usual one-or-two-at-a-time incremental construction normal scrolling
    // does - repeating it on every keystroke/navigation/close was corrupting the rendering
    // (lines showing collapsed/overlapping) once combined with further scrolling. Off-screen
    // matches are cheap no-ops here since there's no cached visual line to touch yet - they'll
    // colorize correctly whenever they're actually scrolled into view.
    private void RedrawMatches(IEnumerable<(int Offset, int Length)> matches)
    {
        foreach (var (offset, length) in matches)
            Editor.TextArea.TextView.Redraw(offset, length);
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

        // Editor.Select() moves the caret, which fires Editor_CaretPositionChanged ->
        // UpdateGhostText - if the match happens to be a complete keyword/function name (e.g.
        // "SIN"), that finds a real completion ("SIN()") and shows non-empty ghost text ("()")
        // positioned at the caret. Ghost text only makes sense while actively typing, not after
        // jumping here via Find, and rendering it forces GhostTextRenderer.OnRender to query the
        // TextView's layout for this line while Find's own highlight/keyword colorizers are
        // simultaneously acting on the exact same span - confirmed via logging that this
        // combination touched off a runaway TextView.VisualLinesChanged loop (1600+ firings)
        // that starved rendering and corrupted the view. A search term that isn't a keyword
        // (e.g. a variable name) never triggers this, since ClearGhostText's empty-ghost-text
        // path is a no-op that never queries the layout at all.
        ClearGhostText();

        // Update the highlighter's state BEFORE revealing the line, so BringCaretToView's own
        // construction of the target line already reflects the new "current match" color -
        // deliberately NOT redrawing that same range again afterward. Confirmed via logging that
        // doing so - Redraw()-ing the exact segment BringCaretToView just built, which the caret
        // also sits at the edge of - touched off a runaway reconstruction loop for that one line
        // (rebuilt continuously, tens of thousands of times a second) that starved the TextView
        // and corrupted rendering everywhere else on screen.
        _findHighlightColorizer.SetMatches(Editor.Document, _findMatches, _findMatchIndex);

        // Deliberately not TextEditor.ScrollTo/ScrollToLine: those set the ScrollViewer's
        // offset directly from the folding-aware height-tree's cached line heights, without
        // constructing the visual lines being jumped over - fine for a short hop, but a long
        // jump (a match far outside the current viewport) can leave that skipped range with
        // stale/uncomputed height data, corrupting the view (lines rendering collapsed/blank)
        // until it's scrolled through normally. BringCaretToView instead goes through the same
        // caret-follows-viewport path as ordinary keyboard/mouse scrolling (which already
        // reveals distant content correctly) - it explicitly constructs the target visual line
        // before asking the TextView to make it visible.
        Editor.TextArea.Caret.BringCaretToView();

        // AvalonEdit's height-tree only learns a line's TRUE rendered height (this app's
        // LineSpacingElementGenerator adds +4px per line beyond AvalonEdit's own internal
        // DefaultLineHeight estimate) once that line has actually been measured - for a match
        // far below the current viewport, the extent BringCaretToView just computed from is
        // still using the placeholder estimate for every unvisited line in between, so it
        // undershoots. The layout pass that call just triggered is what measures (and corrects
        // the height-tree for) the target line, so calling it again - deferred until after that
        // pass has actually run - lands exactly right. Automates what would otherwise need a
        // second Enter press to reach the match.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Editor.TextArea.Caret.BringCaretToView());

        // Redraw every OTHER match (a previous current match dropping back to the regular
        // color, or leftover highlights from an earlier search) - not the one BringCaretToView
        // just built, per above.
        for (int i = 0; i < _findMatches.Count; i++)
        {
            if (i == _findMatchIndex) continue;
            var (otherOffset, otherLength) = _findMatches[i];
            Editor.TextArea.TextView.Redraw(otherOffset, otherLength);
        }

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

    private async void SearchQueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RunProjectSearchAsync();
    }

    // Runs an explicit (not live-as-you-type) search across every searchable source file in the
    // open project, replacing ViewModel.SearchResults wholesale. The file scan runs on a
    // background thread so a large folder (or a slow regex) doesn't freeze the window. Starting
    // a new search cancels one still running, and Replace All is disabled meanwhile so it can't
    // act on results from a search that hasn't finished.
    private async Task RunProjectSearchAsync()
    {
        _projectSearchCts?.Cancel();
        ViewModel.SearchResults.Clear();

        string root = ViewModel.Project.RootPath;
        string query = SearchQueryBox.Text;
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(query))
        {
            SearchStatusText.Text = string.IsNullOrEmpty(root) ? "Open a folder to search across its files." : "";
            return;
        }

        var pattern = SearchPattern.TryCreate(query, SearchMatchCaseBtn.IsChecked == true,
            SearchWholeWordBtn.IsChecked == true, SearchRegexBtn.IsChecked == true, out string? patternError);
        if (pattern == null)
        {
            SearchStatusText.Text = $"Invalid regular expression: {patternError}";
            return;
        }

        // Matches the padding OpenFileByPath applies when detokenizing a .prg for display, so a
        // result's line/column lines up with the offsets in the opened tab. The setting is read
        // here, on the UI thread, since the scan itself runs in the background.
        int lineNumberPadding = ViewModel.Settings.LineNumberPadding;
        string PrepareText(string path, string text) =>
            path.EndsWith(".prg", StringComparison.OrdinalIgnoreCase) ? PadLineNumbers(text, lineNumberPadding) : text;

        using var cts = new CancellationTokenSource();
        _projectSearchCts = cts;
        SearchStatusText.Text = "Searching…";
        SearchReplaceAllBtn.IsEnabled = false;

        try
        {
            var outcome = await Task.Run(() => ProjectSearcher.SearchProject(root, pattern, PrepareText, cts.Token), cts.Token);

            // Can finish just as a newer search cancels it - that one has already cleared the
            // results and owns the status text, so don't add stale results to its list.
            if (cts.IsCancellationRequested) return;

            foreach (var fileResult in outcome.Files)
                ViewModel.SearchResults.Add(fileResult);

            int fileCount = outcome.Files.Count;
            string summary = outcome.TotalMatches == 0
                ? "No results found."
                : $"{outcome.TotalMatches} result{(outcome.TotalMatches == 1 ? "" : "s")} in {fileCount} file{(fileCount == 1 ? "" : "s")}.";
            if (outcome.TimedOutFiles > 0)
                summary += $" {outcome.TimedOutFiles} file{(outcome.TimedOutFiles == 1 ? " was" : "s were")} skipped because the regular expression took too long to evaluate.";
            SearchStatusText.Text = summary;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search, which now owns the results and status text.
        }
        finally
        {
            if (ReferenceEquals(_projectSearchCts, cts))
            {
                _projectSearchCts = null;
                SearchReplaceAllBtn.IsEnabled = true;
            }
        }
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
    private async void SearchReplaceAll_Click(object sender, RoutedEventArgs e)
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

                    var lineStarts = ProjectSearcher.ComputeLineStartOffsets(text);
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

        await RunProjectSearchAsync();
    }

    #endregion
}
