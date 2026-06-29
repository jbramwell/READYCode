// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Input;

namespace ReadyCode.Views;

/// <summary>
/// Dialog for jumping to a specific BASIC line number or file line number.
/// </summary>
public partial class GoToLineDialog : Window
{
    #region Private Fields

    private readonly int _minBasicLine;
    private readonly int _maxBasicLine;
    private readonly int _fileLineCount;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="GoToLineDialog"/> class.
    /// </summary>
    /// <param name="minBasicLine">The smallest BASIC line number present in the document.</param>
    /// <param name="maxBasicLine">The largest BASIC line number present in the document.</param>
    /// <param name="fileLineCount">The total number of lines in the file.</param>
    /// <param name="hasBasicLines">Whether the document has any BASIC line numbers to jump between.</param>
    public GoToLineDialog(int minBasicLine, int maxBasicLine, int fileLineCount, bool hasBasicLines = true)
    {
        _minBasicLine = minBasicLine;
        _maxBasicLine = maxBasicLine;
        _fileLineCount = fileLineCount;

        Opacity = 0;
        InitializeComponent();
        UpdatePromptLabel();

        if (!hasBasicLines)
        {
            RadioBasic.IsEnabled = false;
            RadioFile.IsChecked = true;
        }

        ContentRendered += (_, _) => Opacity = 1;
        Loaded += (_, _) => { LineNumberBox.Focus(); LineNumberBox.SelectAll(); };
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the line number entered by the user, or null if the dialog was canceled.
    /// </summary>
    public int? EnteredLineNumber { get; private set; }

    /// <summary>
    /// Gets whether the user chose to jump by file line number rather than BASIC line number.
    /// </summary>
    public bool IsFileLineMode => RadioFile.IsChecked == true;

    #endregion

    #region Private Methods

    private void RadioButton_Checked(object sender, RoutedEventArgs e) => UpdatePromptLabel();

    private void UpdatePromptLabel()
    {
        if (PromptLabel == null) return;
        if (RadioBasic?.IsChecked == true)
            PromptLabel.Text = $"Line number ({_minBasicLine} - {_maxBasicLine}):";
        else
            PromptLabel.Text = $"Line number (1 - {_fileLineCount}):";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void LineNumberBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryAccept();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => TryAccept();

    private void TryAccept()
    {
        if (int.TryParse(LineNumberBox.Text.Trim(), out int lineNum))
        {
            EnteredLineNumber = lineNum;
            DialogResult = true;
        }
    }

    #endregion
}
