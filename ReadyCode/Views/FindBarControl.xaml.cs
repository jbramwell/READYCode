// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReadyCode.Views;

/// <summary>
/// Find/replace bar overlaid on the editor, raising events for the host window to perform
/// the actual search and replace operations.
/// </summary>
public partial class FindBarControl : UserControl
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="FindBarControl"/> class.
    /// </summary>
    public FindBarControl()
    {
        InitializeComponent();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Occurs when the find bar is closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Occurs when the search text or any search option changes.
    /// </summary>
    public event EventHandler? SearchChanged;

    /// <summary>
    /// Occurs when the user requests the next match.
    /// </summary>
    public event EventHandler? FindNextRequested;

    /// <summary>
    /// Occurs when the user requests the previous match.
    /// </summary>
    public event EventHandler? FindPreviousRequested;

    /// <summary>
    /// Occurs when the user requests a single replace.
    /// </summary>
    public event EventHandler? ReplaceRequested;

    /// <summary>
    /// Occurs when the user requests replace-all.
    /// </summary>
    public event EventHandler? ReplaceAllRequested;

    /// <summary>
    /// Gets the current text in the search box.
    /// </summary>
    public string SearchText  => SearchBox.Text;

    /// <summary>
    /// Gets the current text in the replace box.
    /// </summary>
    public string ReplaceText => ReplaceBox.Text;

    /// <summary>
    /// Gets whether the "match case" option is enabled.
    /// </summary>
    public bool MatchCase     => MatchCaseBtn.IsChecked == true;

    /// <summary>
    /// Gets whether the "whole word" option is enabled.
    /// </summary>
    public bool WholeWord     => WholeWordBtn.IsChecked == true;

    /// <summary>
    /// Gets whether the "use regular expression" option is enabled.
    /// </summary>
    public bool UseRegex      => RegexBtn.IsChecked     == true;

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows the find bar, optionally pre-filled with the given text and expanded into replace mode.
    /// </summary>
    /// <param name="initialText">Text to pre-fill the search box with, if not empty.</param>
    /// <param name="replaceMode">Whether to expand the bar into replace mode.</param>
    public void Open(string initialText, bool replaceMode)
    {
        if (!string.IsNullOrEmpty(initialText))
            SearchBox.Text = initialText;

        ReplaceRow.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        ExpandBtn.IsChecked   = replaceMode;
        ExpandArrow.Text      = replaceMode ? "▾" : "▸";

        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        });
    }

    /// <summary>
    /// Hides the find bar and raises <see cref="CloseRequested"/>.
    /// </summary>
    public void Close()
    {
        Visibility = Visibility.Collapsed;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the match count indicator and search box border to reflect the current search results.
    /// </summary>
    /// <param name="current">The 1-based index of the current match.</param>
    /// <param name="total">The total number of matches.</param>
    public void SetMatchCount(int current, int total)
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            MatchCountText.Text = "";
            SearchBox.BorderBrush = (Brush)FindResource("ThemeSettingsInputBorder");
        }
        else if (total == 0)
        {
            MatchCountText.Text   = "No results";
            SearchBox.BorderBrush = Brushes.Red;
        }
        else
        {
            MatchCountText.Text   = $"{current} of {total}";
            SearchBox.BorderBrush = (Brush)FindResource("ThemeSettingsInputBorder");
        }
    }

    #endregion

    #region Private Methods

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        { Close(); e.Handled = true; return; }
        if (e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.Shift)
        { FindPreviousRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
        if (e.Key == Key.Return)
        { FindNextRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => SearchChanged?.Invoke(this, EventArgs.Empty);

    private void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        { Close(); e.Handled = true; return; }
        if (e.Key == Key.Return)
        { ReplaceRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
        => SearchChanged?.Invoke(this, EventArgs.Empty);

    private void ExpandBtn_Checked(object sender, RoutedEventArgs e)
    {
        ExpandArrow.Text      = "▾";
        ReplaceRow.Visibility = Visibility.Visible;
    }

    private void ExpandBtn_Unchecked(object sender, RoutedEventArgs e)
    {
        ExpandArrow.Text      = "▸";
        ReplaceRow.Visibility = Visibility.Collapsed;
    }

    private void PrevMatch_Click(object sender, RoutedEventArgs e)
        => FindPreviousRequested?.Invoke(this, EventArgs.Empty);

    private void NextMatch_Click(object sender, RoutedEventArgs e)
        => FindNextRequested?.Invoke(this, EventArgs.Empty);

    private void Replace_Click(object sender, RoutedEventArgs e)
        => ReplaceRequested?.Invoke(this, EventArgs.Empty);

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        => ReplaceAllRequested?.Invoke(this, EventArgs.Empty);

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    #endregion
}
