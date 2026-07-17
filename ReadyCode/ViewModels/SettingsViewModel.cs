// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReadyCode.Settings;

namespace ReadyCode.ViewModels;

/// <summary>
/// Holds the editable state of the Preferences dialog so its controls can bind directly
/// to it instead of being pushed updates from code-behind.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    #region Private Fields

    private string _wrapColumnText;
    private bool _showC64UMenu;
    private string _c64UUrl;
    private bool _showViceMenu;
    private string _viceEmulatorPath;
    private string _viceMonitorHost;
    private string _viceMonitorPortText;
    private bool _viceBringToForeground;
    private string _lineNumberPaddingText;
    private bool _autoNumberLines;
    private string _autoNumberIncrementText;
    private string _editorFontSizeText;
    private bool _restoreOpenTabsOnStartup;
    private string _theme;
    private bool _showOverflowLine;
    private bool _minifyOnTransfer;
    private bool _minifyRemoveWhitespace;
    private bool _minifyReplaceZeroWithDot;
    private bool _minifyUseScientificNotation;
    private bool _minifyRemoveComments;
    private bool _minifySimplifyNext;
    private bool _minifyRenumberLines;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class, loading the
    /// editable field values from <paramref name="settings"/>.
    /// </summary>
    /// <param name="settings">The application settings to read the initial field values from.</param>
    public SettingsViewModel(AppSettings settings)
    {
        _restoreOpenTabsOnStartup = settings.RestoreOpenTabsOnStartup;
        _theme = settings.Theme;
        _wrapColumnText = settings.ColumnGuideColumn.ToString();
        _showC64UMenu = settings.ShowC64UMenu;
        _c64UUrl = settings.C64UUrl;
        _showViceMenu = settings.ShowViceMenu;
        _viceEmulatorPath = settings.ViceEmulatorPath;
        _viceMonitorHost = settings.ViceMonitorHost;
        _viceMonitorPortText = settings.ViceMonitorPort.ToString();
        _viceBringToForeground = settings.ViceBringToForeground;
        _lineNumberPaddingText = settings.LineNumberPadding.ToString();
        _autoNumberLines = settings.AutoNumberLines;
        _autoNumberIncrementText = settings.AutoNumberIncrement.ToString();
        _editorFontSizeText = settings.EditorFontSize.ToString();
        _showOverflowLine = settings.ShowColumnGuide;
        _minifyOnTransfer = settings.MinifyOnTransfer;
        _minifyRemoveWhitespace = settings.MinifyRemoveWhitespace;
        _minifyReplaceZeroWithDot = settings.MinifyReplaceZeroWithDot;
        _minifyUseScientificNotation = settings.MinifyUseScientificNotation;
        _minifyRemoveComments = settings.MinifyRemoveComments;
        _minifySimplifyNext = settings.MinifySimplifyNext;
        _minifyRenumberLines = settings.MinifyRenumberLines;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets whether tabs from the previous session are reopened on startup.
    /// </summary>
    public bool IsRestoreOpenTabsOnStartup
    {
        get => _restoreOpenTabsOnStartup;
        set
        {
            if (!value) return;
            _restoreOpenTabsOnStartup = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDoNotRestoreOpenTabsOnStartup));
        }
    }

    /// <summary>
    /// Gets or sets whether tabs from the previous session are NOT reopened on startup.
    /// </summary>
    public bool IsDoNotRestoreOpenTabsOnStartup
    {
        get => !_restoreOpenTabsOnStartup;
        set
        {
            if (!value) return;
            _restoreOpenTabsOnStartup = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRestoreOpenTabsOnStartup));
        }
    }

    /// <summary>
    /// Gets or sets whether the Light theme is selected.
    /// </summary>
    public bool IsLightTheme
    {
        get => _theme == "Light";
        set
        {
            if (!value) return;
            _theme = "Light";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsC64Theme));
        }
    }

    /// <summary>
    /// Gets or sets whether the Dark theme is selected.
    /// </summary>
    public bool IsDarkTheme
    {
        get => _theme == "Dark";
        set
        {
            if (!value) return;
            _theme = "Dark";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsC64Theme));
        }
    }

    /// <summary>
    /// Gets or sets whether the C64 theme is selected.
    /// </summary>
    public bool IsC64Theme
    {
        get => _theme == "C64";
        set
        {
            if (!value) return;
            _theme = "C64";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        }
    }

    /// <summary>
    /// Gets or sets the column guide column, as entered text, before validation.
    /// </summary>
    public string WrapColumnText
    {
        get => _wrapColumnText;
        set { if (_wrapColumnText == value) return; _wrapColumnText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the C64U menu is shown in the main menu bar.
    /// </summary>
    public bool ShowC64UMenu
    {
        get => _showC64UMenu;
        set { if (_showC64UMenu == value) return; _showC64UMenu = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the C64 Ultimate's base URL, as entered text.
    /// </summary>
    public string C64UUrl
    {
        get => _c64UUrl;
        set { if (_c64UUrl == value) return; _c64UUrl = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the VICE menu is shown in the main menu bar.
    /// </summary>
    public bool ShowViceMenu
    {
        get => _showViceMenu;
        set { if (_showViceMenu == value) return; _showViceMenu = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the path to the VICE emulator executable.
    /// </summary>
    public string ViceEmulatorPath
    {
        get => _viceEmulatorPath;
        set { if (_viceEmulatorPath == value) return; _viceEmulatorPath = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the host VICE's binary monitor listens on.
    /// </summary>
    public string ViceMonitorHost
    {
        get => _viceMonitorHost;
        set { if (_viceMonitorHost == value) return; _viceMonitorHost = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the port VICE's binary monitor listens on, as entered text, before validation.
    /// </summary>
    public string ViceMonitorPortText
    {
        get => _viceMonitorPortText;
        set { if (_viceMonitorPortText == value) return; _viceMonitorPortText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether to bring the VICE window to the foreground when loading or running a program.
    /// </summary>
    public bool ViceBringToForeground
    {
        get => _viceBringToForeground;
        set { if (_viceBringToForeground == value) return; _viceBringToForeground = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the line number zero-padding digit count, as entered text, before validation.
    /// </summary>
    public string LineNumberPaddingText
    {
        get => _lineNumberPaddingText;
        set { if (_lineNumberPaddingText == value) return; _lineNumberPaddingText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether auto-numbering of new lines is enabled.
    /// </summary>
    public bool AutoNumberLines
    {
        get => _autoNumberLines;
        set { if (_autoNumberLines == value) return; _autoNumberLines = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the auto-number line increment, as entered text, before validation.
    /// </summary>
    public string AutoNumberIncrementText
    {
        get => _autoNumberIncrementText;
        set { if (_autoNumberIncrementText == value) return; _autoNumberIncrementText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the editor font size, as entered text, before validation.
    /// </summary>
    public string EditorFontSizeText
    {
        get => _editorFontSizeText;
        set { if (_editorFontSizeText == value) return; _editorFontSizeText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the vertical column guide line is shown in the editor.
    /// </summary>
    public bool ShowOverflowLine
    {
        get => _showOverflowLine;
        set { if (_showOverflowLine == value) return; _showOverflowLine = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether code is automatically minified when transferred to the C64 Ultimate.
    /// </summary>
    public bool MinifyOnTransfer
    {
        get => _minifyOnTransfer;
        set { if (_minifyOnTransfer == value) return; _minifyOnTransfer = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the transfer-time minify pass removes unnecessary whitespace.
    /// </summary>
    public bool MinifyRemoveWhitespace
    {
        get => _minifyRemoveWhitespace;
        set { if (_minifyRemoveWhitespace == value) return; _minifyRemoveWhitespace = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the transfer-time minify pass replaces zeroes with dots where possible.
    /// </summary>
    public bool MinifyReplaceZeroWithDot
    {
        get => _minifyReplaceZeroWithDot;
        set { if (_minifyReplaceZeroWithDot == value) return; _minifyReplaceZeroWithDot = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the transfer-time minify pass converts long numbers to scientific notation.
    /// </summary>
    public bool MinifyUseScientificNotation
    {
        get => _minifyUseScientificNotation;
        set { if (_minifyUseScientificNotation == value) return; _minifyUseScientificNotation = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the transfer-time minify pass removes comments.
    /// </summary>
    public bool MinifyRemoveComments
    {
        get => _minifyRemoveComments;
        set { if (_minifyRemoveComments == value) return; _minifyRemoveComments = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the transfer-time minify pass simplifies NEXT statements.
    /// </summary>
    public bool MinifySimplifyNext
    {
        get => _minifySimplifyNext;
        set { if (_minifySimplifyNext == value) return; _minifySimplifyNext = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the transfer-time minify pass renumbers lines.
    /// </summary>
    public bool MinifyRenumberLines
    {
        get => _minifyRenumberLines;
        set { if (_minifyRenumberLines == value) return; _minifyRenumberLines = value; OnPropertyChanged(); }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Checks the current field values. Returns null if all are valid, or a user-facing
    /// message describing the first invalid field.
    /// </summary>
    public string? Validate()
    {
        if (!int.TryParse(WrapColumnText, out int column) || column < 1)
            return "Please enter a whole number greater than zero for the wrap column.";

        string url = C64UUrl.Trim();
        if (!string.IsNullOrEmpty(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))
            return "Please enter a valid URL for the C64 Ultimate (e.g. http://192.168.50.123/).";

        if (!int.TryParse(LineNumberPaddingText, out int padding) || padding < 0)
            return "Please enter a whole number of zero or greater for the line number padding length.";

        if (!int.TryParse(AutoNumberIncrementText, out int increment) || increment < 1)
            return "Please enter a whole number of 1 or greater for the auto-number increment.";

        if (!int.TryParse(EditorFontSizeText, out int fontSize) || fontSize < 6 || fontSize > 72)
            return "Please enter a font size between 6 and 72.";

        if (!int.TryParse(ViceMonitorPortText, out int monitorPort) || monitorPort < 1 || monitorPort > 65535)
            return "Please enter a valid port number (1-65535) for the VICE monitor port.";

        return null;
    }

    /// <summary>
    /// Copies the current (already-validated) field values into <paramref name="settings"/>.
    /// </summary>
    public void ApplyTo(AppSettings settings)
    {
        settings.RestoreOpenTabsOnStartup = _restoreOpenTabsOnStartup;
        settings.Theme = _theme;
        settings.ColumnGuideColumn = int.Parse(WrapColumnText);
        settings.ShowC64UMenu = ShowC64UMenu;
        settings.C64UUrl = C64UUrl.Trim();
        settings.ShowViceMenu = ShowViceMenu;
        settings.ViceEmulatorPath = ViceEmulatorPath.Trim();
        settings.ViceMonitorHost = ViceMonitorHost.Trim();
        settings.ViceMonitorPort = int.Parse(ViceMonitorPortText);
        settings.ViceBringToForeground = ViceBringToForeground;
        settings.LineNumberPadding = int.Parse(LineNumberPaddingText);
        settings.AutoNumberLines = AutoNumberLines;
        settings.AutoNumberIncrement = int.Parse(AutoNumberIncrementText);
        settings.EditorFontSize = int.Parse(EditorFontSizeText);
        settings.ShowColumnGuide = ShowOverflowLine;
        settings.MinifyOnTransfer = MinifyOnTransfer;
        settings.MinifyRemoveWhitespace = MinifyRemoveWhitespace;
        settings.MinifyReplaceZeroWithDot = MinifyReplaceZeroWithDot;
        settings.MinifyUseScientificNotation = MinifyUseScientificNotation;
        settings.MinifyRemoveComments = MinifyRemoveComments;
        settings.MinifySimplifyNext = MinifySimplifyNext;
        settings.MinifyRenumberLines = MinifyRenumberLines;
    }

    #endregion

    #region Interface Implementations

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
