// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Input;

namespace ReadyCode.Views;

/// <summary>
/// Dialog showing character, word, line, and token-byte counts for the active document.
/// </summary>
public partial class CodeStatisticsWindow : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeStatisticsWindow"/> class with the
    /// given document statistics.
    /// </summary>
    /// <param name="charCount">The total character count.</param>
    /// <param name="wordCount">The total word count.</param>
    /// <param name="lineCount">The total line count.</param>
    /// <param name="tokenBytes">The tokenized program size in bytes.</param>
    public CodeStatisticsWindow(int charCount, int wordCount, int lineCount, int tokenBytes)
    {
        Opacity = 0;
        InitializeComponent();

        CharCountText.Text  = charCount.ToString("N0");
        WordCountText.Text  = wordCount.ToString("N0");
        LineCountText.Text  = lineCount.ToString("N0");
        TokenBytesText.Text = $"{tokenBytes:N0} / 38,911";

        Loaded += (_, _) => { Opacity = 1; };
    }

    #endregion

    #region Private Methods

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    #endregion
}
