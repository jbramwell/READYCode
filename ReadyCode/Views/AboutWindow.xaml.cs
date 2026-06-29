// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace ReadyCode.Views;

/// <summary>
/// "About READYCode" dialog, showing the application name and version.
/// </summary>
public partial class AboutWindow : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutWindow"/> class.
    /// </summary>
    public AboutWindow()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        AppTitle = ver != null ? $"READYCode v{ver.Major}.{ver.Minor}" : "READYCode";

        Opacity = 0;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { Opacity = 1; };
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the application title, including its version number if available.
    /// </summary>
    public string AppTitle { get; }

    #endregion

    #region Private Methods

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void LicensesButton_Click(object sender, RoutedEventArgs e) => new LicensesWindow { Owner = this }.ShowDialog();

    #endregion
}
