// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Input;
using ReadyCode.Settings;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

/// <summary>
/// Dialog for choosing which prettification passes to apply to the active document.
/// </summary>
public partial class PrettifyWindow : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="PrettifyWindow"/> class.
    /// </summary>
    /// <param name="settings">The application settings to load and persist the dialog's selections from/to.</param>
    public PrettifyWindow(AppSettings settings)
    {
        Opacity = 0;
        InitializeComponent();

        ViewModel  = new PrettifyViewModel(settings);
        DataContext = ViewModel;

        ContentRendered += (_, _) => Opacity = 1;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the view model backing this dialog's controls.
    /// </summary>
    public PrettifyViewModel ViewModel { get; }

    #endregion

    #region Private Methods

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Prettify_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    #endregion
}
