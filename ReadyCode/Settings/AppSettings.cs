// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text.Json;

namespace ReadyCode.Settings;

/// <summary>
/// Persisted application settings for ReadyCode.
/// </summary>
public class AppSettings
{
    #region Public Properties

    /// <summary>
    /// Whether the C64U menu is shown in the main menu bar.
    /// </summary>
    public bool ShowC64UMenu { get; set; } = true;

    /// <summary>
    /// Base URL of the Commodore 64 Ultimate's REST API (e.g. http://192.168.50.123/).
    /// </summary>
    public string C64UUrl { get; set; } = "http://{YOUR-C64U-IP-ADDRESS-GOES-HERE}/";

    /// <summary>
    /// Whether the VICE menu is shown in the main menu bar.
    /// </summary>
    public bool ShowViceMenu { get; set; } = true;

    /// <summary>
    /// Path to the VICE emulator executable, used when launching VICE.
    /// </summary>
    public string ViceEmulatorPath { get; set; } = "";

    /// <summary>
    /// Host VICE's binary monitor listens on.
    /// </summary>
    public string ViceMonitorHost { get; set; } = "127.0.0.1";

    /// <summary>
    /// Port VICE's binary monitor listens on.
    /// </summary>
    public int ViceMonitorPort { get; set; } = 6502;

    /// <summary>
    /// Whether to bring the VICE window to the foreground when loading or running a program.
    /// </summary>
    public bool ViceBringToForeground { get; set; } = true;

    /// <summary>
    /// Whether to show a vertical column guide line at the specified column in the editor.
    /// </summary>
    public bool ShowColumnGuide { get; set; } = true;

    /// <summary>
    /// The column at which to show the vertical column guide line
    /// (e.g. 40 for the C64, 22 for the VIC-20).
    /// </summary>
    public int ColumnGuideColumn { get; set; } = 40;

    /// <summary>
    /// Number of digits to zero-pad line numbers to (e.g. 4 -> 0010, 0020). 0 disables padding.
    /// </summary>
    public int LineNumberPadding { get; set; } = 4;

    /// <summary>
    /// When true, pressing Enter after a non-empty line automatically inserts the next line number.
    /// </summary>
    public bool AutoNumberLines { get; set; } = false;

    /// <summary>
    /// Amount added to the previous line number when auto-numbering (e.g. 10 → 10, 20, 30…).
    /// </summary>
    public int AutoNumberIncrement { get; set; } = 10;

    /// <summary>
    /// Font size used in the code editor.
    /// </summary>
    public int EditorFontSize { get; set; } = 12;

    /// <summary>
    /// Whether the editor wraps long lines.
    /// </summary>
    public bool WordWrap { get; set; } = false;

    /// <summary>
    /// Whether the status bar is visible.
    /// </summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>
    /// Remembered width of the Settings window.
    /// </summary>
    public double SettingsWindowWidth { get; set; } = 750;

    /// <summary>
    /// Remembered height of the Settings window.
    /// </summary>
    public double SettingsWindowHeight { get; set; } = 520;

    /// <summary>
    /// The 5 most recently opened or saved files, newest first.
    /// </summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>
    /// When true, tabs open at the end of the previous session are reopened on startup.
    /// </summary>
    public bool RestoreOpenTabsOnStartup { get; set; } = true;

    /// <summary>
    /// File paths of the tabs open when the app last closed, in tab order. Only populated when
    /// <see cref="RestoreOpenTabsOnStartup"/> is true.
    /// </summary>
    public List<string> OpenTabPaths { get; set; } = new();

    /// <summary>
    /// UI color theme: "Light", "Dark", or "C64".
    /// </summary>
    public string Theme { get; set; } = "Light";

    /// <summary>
    /// Remembers the width of the left panel in the main window.
    /// </summary>
    public double LeftPanelWidth { get; set; } = 230;

    /// <summary>
    /// Remembers the width of the right panel in the main window.
    /// </summary>
    public double RightPanelWidth { get; set; } = 230;

    /// <summary>
    /// Remembers the left position of the main window. Left and Top are
    /// nullable so they can be reset to default (centered) if needed.
    /// </summary>
    public double? MainWindowLeft   { get; set; } = null;

    /// <summary>
    /// Remembers the top position of the main window. Left and Top are
    /// nullable so they can be reset to default (centered) if needed.
    /// </summary>
    public double? MainWindowTop    { get; set; } = null;

    /// <summary>
    /// Remembers the width of the main window. This is used to restore the window
    /// to its previous size on launch, and can be reset to default if needed.
    /// </summary>
    public double  MainWindowWidth  { get; set; } = 1000;

    /// <summary>
    /// Remembers the height of the main window. This is used to restore the window
    /// to its previous size on launch, and can be reset to default if needed.
    /// </summary>
    public double  MainWindowHeight { get; set; } = 700;

    /// <summary>
    /// Remembers whether the main window was maximized when the application last closed.
    /// </summary>
    public bool IsMainWindowMaximized { get; set; } = false;

    /// <summary>
    /// Remembers whether the left panel in the main window is open or closed.
    /// </summary>
    public bool IsLeftPanelOpen { get; set; } = true;

    /// <summary>
    /// Remembers whether the right panel in the main window is open or closed.
    /// </summary>
    public bool IsRightPanelOpen { get; set; } = false;

    /// <summary>
    /// Remembers which right-panel tab was last active: "QuickKeys", "Petscii", or "BasicKeywords".
    /// </summary>
    public string ActiveRightPanel { get; set; } = "QuickKeys";

    /// <summary>
    /// Remembers which left-panel tab was last active: "Explorer" or "C64U".
    /// </summary>
    public string ActiveLeftPanel { get; set; } = "Explorer";

    /// <summary>
    /// Remembers the last folder path used in Open/Save dialogs, to start
    /// from that location next time.
    /// </summary>
    public string LastFolderPath { get; set; } = "";

    /// <summary>
    /// URL of the READYCode GitHub repository, opened by Help &gt; Visit READYCode on GitHub.
    /// </summary>
    public string GitHubUrl { get; set; } = "https://github.com/jbramwell/READYCode";

    /// <summary>
    /// When true, code will be automatically minified (unnecessary whitespace
    /// removed, etc.) when transferred to the Commodore 64 Ultimate.
    /// </summary>
    public bool MinifyOnTransfer { get; set; } = false;

    /// <summary>
    /// When true, the minify operation will remove unnecessary whitespace
    /// (spaces, tabs, newlines).
    /// </summary>
    public bool MinifyRemoveWhitespace { get; set; } = true;

    /// <summary>
    /// When true, the minify operation will replace zeroes with dots where possible.
    /// </summary>
    public bool MinifyReplaceZeroWithDot { get; set; } = true;

    /// <summary>
    /// When true, the minify operation will convert long numbers to scientific notation
    /// (e.g. 1000 → 1E3).
    /// </summary>
    public bool MinifyUseScientificNotation { get; set; } = true;

    /// <summary>
    /// When true, the minify operation will remove comments (text after REM statements).
    /// </summary>
    public bool MinifyRemoveComments { get; set; } = true;

    /// <summary>
    /// When true, the minify operation will simplify NEXT statements.
    /// </summary>
    public bool MinifySimplifyNext { get; set; } = true;

    /// <summary>
    /// When true, the minify operation will renumber lines to be sequential starting from 1.
    /// </summary>
    public bool MinifyRenumberLines { get; set; } = true;

    /// <summary>
    /// Prettify dialog's own persisted selections (independent of the Settings window) to
    /// add extra whitespace for readability, even if MinifyOnTransfer is true and
    /// MinifyRemoveWhitespace is true.
    /// </summary>
    public bool PrettifyDialogAddWhitespace { get; set; } = true;

    /// <summary>
    /// Prettify dialog's own persisted selections (independent of the Settings window) to
    /// replace dots with zeroes where possible for readability, even if MinifyOnTransfer is
    /// true and MinifyReplaceZeroWithDot is true.
    /// </summary>
    public bool PrettifyDialogReplacePeriodWithZero { get; set; } = true;

    /// <summary>
    /// Prettify dialog's own persisted selections (independent of the Settings window) to
    /// convert numbers in scientific notation back to standard notation for readability, even
    /// if MinifyOnTransfer is true and MinifyUseScientificNotation is true.
    /// </summary>
    public bool PrettifyDialogUseStandardNotation { get; set; } = true;

    /// <summary>
    /// Prettify dialog's own persisted selections (independent of the Settings window) to
    /// add REM statements back to comments for readability, even if MinifyOnTransfer is true and
    /// MinifyRemoveComments is true.
    /// </summary>
    public bool PrettifyDialogAddNextVariables { get; set; } = true;

    /// <summary>
    /// Prettify dialog's own persisted selections (independent of the Settings window) to
    /// convert simplified NEXT statements back to their original form for readability, even if
    /// MinifyOnTransfer is true and MinifySimplifyNext is true.
    /// </summary>
    public bool PrettifyDialogRenumberLines { get; set; } = true;

    /// <summary>
    /// Minify dialog's own persisted selections (independent of the Settings window) to
    /// add extra whitespace for readability, even if MinifyOnTransfer is false and
    /// MinifyRemoveWhitespace is false.
    /// </summary>
    public bool MinifyDialogRemoveWhitespace { get; set; } = true;

    /// <summary>
    /// Minify dialog's own persisted selections (independent of the Settings window) to
    /// replace zeroes with dots where possible, even if MinifyOnTransfer is false and
    /// MinifyReplaceZeroWithDot is false.
    /// </summary>
    public bool MinifyDialogReplaceZeroWithDot { get; set; } = true;

    /// <summary>
    /// Minify dialog's own persisted selections (independent of the Settings window) to
    /// convert long numbers to scientific notation (e.g. 1000 → 1E3) for compactness,
    /// even if MinifyOnTransfer is false and MinifyUseScientificNotation is false.
    /// </summary>
    public bool MinifyDialogUseScientificNotation { get; set; } = false;

    /// <summary>
    /// Minify dialog's own persisted selections (independent of the Settings window) to
    /// remove comments (text after REM statements), even if MinifyOnTransfer is false and
    /// MinifyRemoveComments is false, for compactness.
    /// </summary>
    public bool MinifyDialogRemoveComments { get; set; } = true;

    /// <summary>
    /// Minify dialog's own persisted selections (independent of the Settings window) to
    /// simplify NEXT statements for compactness, even if MinifyOnTransfer is false and
    /// MinifySimplifyNext is false.
    /// </summary>
    public bool MinifyDialogSimplifyNext { get; set; } = false;

    /// <summary>
    /// Minify dialog's own persisted selections (independent of the Settings window) to
    /// renumber lines for compactness, even if MinifyOnTransfer is false and
    /// MinifyRenumberLines is false.
    /// </summary>
    public bool MinifyDialogRenumberLines { get; set; } = false;

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads settings from disk, falling back to defaults if none exist or the file
    /// can't be read.
    /// </summary>
    /// <returns>The loaded settings, or defaults if loading fails.</returns>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);

                if (settings != null)
                    return settings;
            }
        }
        catch (Exception)
        {
            // Settings file is missing or corrupt - fall back to defaults
        }

        return new AppSettings();
    }

    /// <summary>
    /// Adds a file path to the list of recent files, moving it to the front if it already
    /// exists, and ensuring the list doesn't exceed 10 items.
    /// </summary>
    /// <param name="path">The file path to add.</param>
    public void AddRecentFile(string path)
    {
        RecentFiles.Remove(path);
        RecentFiles.Insert(0, path);

        if (RecentFiles.Count > 10)
            RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }

    /// <summary>
    /// Persists the application settings to disk.
    /// </summary>
    public void Save()
    {
        string? directory = Path.GetDirectoryName(SettingsFilePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(SettingsFilePath, json);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Gets the file path for the settings file.
    /// </summary>
    private static string SettingsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "READYCode", "settings.json");

    #endregion
}
