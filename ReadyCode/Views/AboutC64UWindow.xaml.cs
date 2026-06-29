// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Input;
using ReadyCode.C64U;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

/// <summary>
/// "About my C64" dialog, showing device information retrieved from the C64 Ultimate.
/// </summary>
public partial class AboutC64UWindow : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutC64UWindow"/> class for the given device information.
    /// </summary>
    /// <param name="info">The device information to display.</param>
    public AboutC64UWindow(C64UInfo info)
    {
        Opacity = 0;
        InitializeComponent();
        DataContext = new AboutC64UViewModel(info);
        Loaded += (_, _) => { Opacity = 1; };
    }

    #endregion

    #region Private Methods

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    #endregion
}
