// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using ReadyCode.Vice;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

/// <summary>
/// "About VICE" dialog, showing version information retrieved from VICE's binary monitor.
/// </summary>
public partial class AboutViceWindow : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViceWindow"/> class for the given
    /// version information.
    /// </summary>
    /// <param name="info">The version information to display.</param>
    /// <param name="emulatorPath">Full path to the VICE emulator executable.</param>
    public AboutViceWindow(ViceInfo info, string emulatorPath)
    {
        Opacity = 0;
        InitializeComponent();
        DataContext = new AboutViceViewModel(info, emulatorPath);
        Loaded += (_, _) => { Opacity = 1; };
    }

    #endregion

    #region Private Methods

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    #endregion
}
