// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Input;

namespace ReadyCode.Views;

/// <summary>
/// Dialog for renumbering a BASIC document's line numbers - the starting number, the increment,
/// and whether to renumber the whole document or only the lines in the current editor selection.
/// </summary>
public partial class RenumberDialog : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="RenumberDialog"/> class.
    /// </summary>
    /// <param name="defaultStart">The starting line number to pre-fill.</param>
    /// <param name="defaultIncrement">The increment to pre-fill, sourced from the app's auto-number setting.</param>
    /// <param name="hasSelection">
    /// Whether the editor currently has a non-empty text selection. When true, "Selected lines
    /// only" is offered and pre-selected (the user having a selection is taken as intent to act
    /// on it); when false, it's disabled and "All lines" is used instead.
    /// </param>
    public RenumberDialog(int defaultStart, int defaultIncrement, bool hasSelection)
    {
        Opacity = 0;
        InitializeComponent();

        StartLineNumberBox.Text = defaultStart.ToString();
        IncrementBox.Text = defaultIncrement.ToString();

        if (hasSelection)
            RadioSelectedOnly.IsChecked = true;
        else
            RadioSelectedOnly.IsEnabled = false;

        ContentRendered += (_, _) => Opacity = 1;
        Loaded += (_, _) => { StartLineNumberBox.Focus(); StartLineNumberBox.SelectAll(); };
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the starting line number entered by the user.
    /// </summary>
    public int StartLineNumber { get; private set; }

    /// <summary>
    /// Gets the increment entered by the user.
    /// </summary>
    public int Increment { get; private set; }

    /// <summary>
    /// Gets whether the user chose to renumber only the lines in the current selection, rather
    /// than the whole document.
    /// </summary>
    public bool RenumberSelectedOnly => RadioSelectedOnly.IsChecked == true;

    #endregion

    #region Private Methods

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryAccept();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => TryAccept();

    private void TryAccept()
    {
        if (!int.TryParse(StartLineNumberBox.Text.Trim(), out int start) || start < 0) return;
        if (!int.TryParse(IncrementBox.Text.Trim(), out int increment) || increment <= 0) return;

        StartLineNumber = start;
        Increment = increment;
        DialogResult = true;
    }

    #endregion
}
