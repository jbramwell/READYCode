// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ReadyCode.Settings;
using ReadyCode.ViewModels;

namespace ReadyCode.Views;

/// <summary>
/// Preferences dialog, with a tree of setting categories on the left and the corresponding
/// editable fields on the right.
/// </summary>
public partial class SettingsWindow : Window
{
    #region Private Fields

    private readonly AppSettings _settings;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    /// <param name="settings">The application settings to edit.</param>
    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;

        // Window is transparent (AllowsTransparency=True) so the DWM surface
        // starts at alpha=0 — no white flash from swap-chain initialization.
        // Keep it invisible until WPF's first render is complete.
        Opacity = 0;

        InitializeComponent();

        Width  = settings.SettingsWindowWidth;
        Height = settings.SettingsWindowHeight;

        ViewModel = new SettingsViewModel(settings);
        DataContext = ViewModel;

        ContentRendered += (_, _) => Opacity = 1;

        Loaded += (_, _) => TreeAppGeneral.IsSelected = true;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the view model backing this dialog's controls.
    /// </summary>
    public SettingsViewModel ViewModel { get; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Persists the window size to settings when the dialog closes.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnClosed(EventArgs e)
    {
        _settings.SettingsWindowWidth  = ActualWidth;
        _settings.SettingsWindowHeight = ActualHeight;
        _settings.Save();
        base.OnClosed(e);
    }

    #endregion

    #region Private Methods

    // ── Custom title bar ─────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ── Tree navigation ──────────────────────────────────────────────────────

    private void SettingsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item) return;

        string tag = item.Tag?.ToString() ?? "";
        PanelAppGeneral.Visibility = tag == "app-general"  ? Visibility.Visible : Visibility.Collapsed;
        PanelGeneral.Visibility    = tag == "general"      ? Visibility.Visible : Visibility.Collapsed;
        PanelFormatting.Visibility = tag == "formatting"   ? Visibility.Visible : Visibility.Collapsed;
        PanelMinify.Visibility     = tag == "code-minify"  ? Visibility.Visible : Visibility.Collapsed;
        PanelC64U.Visibility       = tag == "c64u"         ? Visibility.Visible : Visibility.Collapsed;
        PanelVice.Visibility       = tag == "vice"         ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── VICE Emulator ────────────────────────────────────────────────────────

    private void ViceBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            Title = "Select VICE Emulator Executable"
        };
        if (dialog.ShowDialog() == true)
            ViewModel.ViceEmulatorPath = dialog.FileName;
    }

    // ── Preset buttons ───────────────────────────────────────────────────────

    private void C64Preset_Click(object sender, RoutedEventArgs e)   => ViewModel.WrapColumnText = "40";
    private void Vic20Preset_Click(object sender, RoutedEventArgs e) => ViewModel.WrapColumnText = "22";

    // ── OK / Close ───────────────────────────────────────────────────────────

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? error = ViewModel.Validate();

        if (error != null)
        {
            MessageBox.Show(error, "Settings Error", MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        DialogResult = true;
    }

    #endregion
}
