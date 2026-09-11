// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Globalization;
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

    private string _basicColumnGuideText;
    private string _asmColumnGuideText;
    private string _asmMnemonicIndentColumnText;
    private string _asmCommentAlignColumnText;
    private bool _asmAutoIndent;
    private bool _asmEnableCodeFolding;
    private string _asmOutputMode;
    private string _asmDefaultOriginAddressText;
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
    private bool _enableLinting;
    private bool _enableCodeFolding;

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
        _basicColumnGuideText = settings.BasicColumnGuideColumn.ToString();
        _asmColumnGuideText = settings.AsmColumnGuideColumn.ToString();
        _asmMnemonicIndentColumnText = settings.AsmMnemonicIndentColumn.ToString();
        _asmCommentAlignColumnText = settings.AsmCommentAlignColumn.ToString();
        _asmAutoIndent = settings.AsmAutoIndent;
        _asmEnableCodeFolding = settings.AsmEnableCodeFolding;
        _asmOutputMode = settings.AsmOutputMode;
        _asmDefaultOriginAddressText = "$" + settings.AsmDefaultOriginAddress.ToString("X4");
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
        _enableLinting = settings.EnableLinting;
        _enableCodeFolding = settings.EnableCodeFolding;
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
    /// Gets or sets the BASIC column guide column, as entered text, before validation.
    /// </summary>
    public string BasicColumnGuideText
    {
        get => _basicColumnGuideText;
        set { if (_basicColumnGuideText == value) return; _basicColumnGuideText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the assembly column guide column, as entered text, before validation.
    /// </summary>
    public string AsmColumnGuideText
    {
        get => _asmColumnGuideText;
        set { if (_asmColumnGuideText == value) return; _asmColumnGuideText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the column assembly mnemonics are indented to, as entered text, before
    /// validation.
    /// </summary>
    public string AsmMnemonicIndentColumnText
    {
        get => _asmMnemonicIndentColumnText;
        set { if (_asmMnemonicIndentColumnText == value) return; _asmMnemonicIndentColumnText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the column inline ";" comments are aligned to in assembly source, as entered
    /// text, before validation.
    /// </summary>
    public string AsmCommentAlignColumnText
    {
        get => _asmCommentAlignColumnText;
        set { if (_asmCommentAlignColumnText == value) return; _asmCommentAlignColumnText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether pressing Enter in an assembly tab automatically indents the new line
    /// to match the previous one.
    /// </summary>
    public bool AsmAutoIndent
    {
        get => _asmAutoIndent;
        set { if (_asmAutoIndent == value) return; _asmAutoIndent = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether runs of consecutive full-line ";" comments can be collapsed via code
    /// folding, for an assembly tab.
    /// </summary>
    public bool AsmEnableCodeFolding
    {
        get => _asmEnableCodeFolding;
        set { if (_asmEnableCodeFolding == value) return; _asmEnableCodeFolding = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the assembler packages its output as a runnable program with an
    /// auto-generated BASIC loader stub.
    /// </summary>
    public bool IsAsmOutputAuto
    {
        get => _asmOutputMode == "Auto";
        set
        {
            if (!value) return;
            _asmOutputMode = "Auto";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAsmOutputStandalone));
        }
    }

    /// <summary>
    /// Gets or sets whether the assembler packages its output as a standalone .prg with no
    /// BASIC loader stub.
    /// </summary>
    public bool IsAsmOutputStandalone
    {
        get => _asmOutputMode == "Standalone";
        set
        {
            if (!value) return;
            _asmOutputMode = "Standalone";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAsmOutputAuto));
        }
    }

    /// <summary>
    /// Gets or sets the default origin address for standalone assembler output, as entered hex
    /// text (e.g. "$C000"), before validation.
    /// </summary>
    public string AsmDefaultOriginAddressText
    {
        get => _asmDefaultOriginAddressText;
        set { if (_asmDefaultOriginAddressText == value) return; _asmDefaultOriginAddressText = value; OnPropertyChanged(); }
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
    /// Gets or sets whether duplicate line numbers, unterminated strings, invalid GOTO/GOSUB
    /// targets, and unmatched NEXT variables are flagged as squiggle diagnostics.
    /// </summary>
    public bool EnableLinting
    {
        get => _enableLinting;
        set { if (_enableLinting == value) return; _enableLinting = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether FOR/NEXT and multi-line REM statements can be collapsed via code
    /// folding.
    /// </summary>
    public bool EnableCodeFolding
    {
        get => _enableCodeFolding;
        set { if (_enableCodeFolding == value) return; _enableCodeFolding = value; OnPropertyChanged(); }
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
        if (!int.TryParse(BasicColumnGuideText, out int basicColumn) || basicColumn < 1)
            return "Please enter a whole number greater than zero for the BASIC column guide.";

        if (!int.TryParse(AsmColumnGuideText, out int asmColumn) || asmColumn < 1)
            return "Please enter a whole number greater than zero for the assembly column guide.";

        if (!int.TryParse(AsmMnemonicIndentColumnText, out int mnemonicIndent) || mnemonicIndent < 1)
            return "Please enter a whole number greater than zero for the mnemonic indent column.";

        if (!int.TryParse(AsmCommentAlignColumnText, out int commentAlign) || commentAlign < 1)
            return "Please enter a whole number greater than zero for the comment alignment column.";

        string url = C64UUrl.Trim();
        if (!string.IsNullOrEmpty(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))
            return "Please enter a valid URL for the C64 Ultimate (e.g. http://192.168.50.123/).";

        if (!int.TryParse(LineNumberPaddingText, out int padding) || padding < 0)
            return "Please enter a whole number of zero or greater for the line number padding length.";

        if (!int.TryParse(AutoNumberIncrementText, out int increment) || increment < 1)
            return "Please enter a whole number of 1 or greater for the auto-number increment.";

        if (!int.TryParse(EditorFontSizeText, out int fontSize) || fontSize < 6 || fontSize > 72)
            return "Please enter a font size between 6 and 72.";

        if (!TryParseHexAddress(AsmDefaultOriginAddressText, out _))
            return "Please enter a valid hex address (e.g. $C000) for the default origin, between $0000 and $FFFF.";

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
        settings.BasicColumnGuideColumn = int.Parse(BasicColumnGuideText);
        settings.AsmColumnGuideColumn = int.Parse(AsmColumnGuideText);
        settings.AsmMnemonicIndentColumn = int.Parse(AsmMnemonicIndentColumnText);
        settings.AsmCommentAlignColumn = int.Parse(AsmCommentAlignColumnText);
        settings.AsmAutoIndent = AsmAutoIndent;
        settings.AsmEnableCodeFolding = AsmEnableCodeFolding;
        settings.AsmOutputMode = _asmOutputMode;
        TryParseHexAddress(AsmDefaultOriginAddressText, out int originAddress);
        settings.AsmDefaultOriginAddress = originAddress;
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
        settings.EnableLinting = EnableLinting;
        settings.EnableCodeFolding = EnableCodeFolding;
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

    // Accepts an optional leading "$" (e.g. "$C000" or "C000"), same convention as
    // DisassemblyToolbarControl's address boxes.
    private static bool TryParseHexAddress(string text, out int value)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith('$')) trimmed = trimmed[1..];

        bool ok = ushort.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address);
        value = address;
        return ok;
    }

    #endregion
}
