// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ReadyCode.C64U;
using ReadyCode.Minify;
using ReadyCode.Models;
using ReadyCode.Printing;
using ReadyCode.Settings;
using ReadyCode.Tokenizer;
using ReadyCode.Vice;
using ReadyCode.Views;

namespace ReadyCode.ViewModels;

using RelayCommand = ReadyCode.RelayCommand;

/// <summary>
/// Holds the main window's document state and settings, independent of the editor control,
/// so the window's title and status bar can bind to it directly instead of being pushed
/// updates from code-behind.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    #region Private Fields

    private string _lineCountText = "Lines: 1";
    private string _screenPositionText = "Col: 1, Row 1";
    private string _statusMessage = "Ready";
    private StatusType _statusType = StatusType.Info;
    private bool _isLeftPanelOpen;
    private bool _isRightPanelOpen;
    private ObservableCollection<FileTreeItem> _folderItems = new();
    private string _explorerTitle = "EXPLORER";
    private ObservableCollection<C64UFileItem> _c64uFileItems = new();
    private C64UConnectionState _c64uConnectionState = C64UConnectionState.NotConnected;
    private C64UFtpClient? _c64uFtpClient;
    private string? _c64uDeviceHostname;
    private C64UDriveStatus? _c64uDriveA;
    private C64UDriveStatus? _c64uDriveB;
    private EditorTab? _activeTab;
    private readonly SourcePrinter _printer = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class, loading settings
    /// and wiring up all commands.
    /// </summary>
    public MainViewModel()
    {
        Project = new ProjectContext(Settings);
        _isLeftPanelOpen  = Settings.IsLeftPanelOpen;
        _isRightPanelOpen = Settings.IsRightPanelOpen;

        FilePrintCommand = new RelayCommand(_ => PrintActiveTab(), _ => HasActiveTab());
        FilePrintPreviewCommand = new RelayCommand(_ => PrintPreviewActiveTab(), _ => HasActiveTab());
        FilePageSetupCommand = new RelayCommand(_ => _printer.ShowPageSetupDialog((Window)Application.Current.MainWindow));

        C64UTransferCommand = new RelayCommand(async _ => await TransferCurrentProgramAsync(), _ => HasNonEmptyActiveTab());
        C64URunCommand = new RelayCommand(async _ => await RunCurrentProgramAsync(), _ => HasNonEmptyActiveTab());
        C64UResetCommand    = new RelayCommand(async _ => await MachineActionAsync("reset",    "Machine reset."));
        C64URebootCommand   = new RelayCommand(async _ => await MachineActionAsync("reboot",   "Machine rebooted."));
        C64UPauseCommand    = new RelayCommand(async _ => await MachineActionAsync("pause",    "Machine paused."));
        C64UResumeCommand   = new RelayCommand(async _ => await MachineActionAsync("resume",   "Machine resumed."));
        C64UPowerOffCommand = new RelayCommand(async _ => await MachineActionAsync("poweroff", "Machine powered off."));
        C64USystemInfoCommand = new RelayCommand(async _ => await ShowC64USystemInfoAsync());

        ViceTransferCommand = new RelayCommand(async _ => await TransferToViceAsync(), _ => HasNonEmptyActiveTab());
        ViceRunCommand = new RelayCommand(async _ => await RunOnViceAsync(), _ => HasNonEmptyActiveTab());
        ViceResetCommand    = new RelayCommand(async _ => await ViceMachineActionAsync(c => c.ResetAsync(Settings.ViceEmulatorPath),    "VICE machine reset."));
        ViceRebootCommand   = new RelayCommand(async _ => await ViceMachineActionAsync(c => c.RebootAsync(Settings.ViceEmulatorPath),   "VICE machine rebooted."));
        VicePauseCommand    = new RelayCommand(async _ => await ViceMachineActionAsync(c => c.PauseAsync(Settings.ViceEmulatorPath),    "VICE machine paused."));
        ViceResumeCommand   = new RelayCommand(async _ => await ViceMachineActionAsync(c => c.ResumeAsync(),                            "VICE machine resumed."));
        VicePowerOffCommand = new RelayCommand(async _ => await ViceMachineActionAsync(c => c.PowerOffAsync(Settings.ViceEmulatorPath), "VICE emulator closed."));
        ViceSystemInfoCommand = new RelayCommand(async _ => await ShowViceSystemInfoAsync());

        HelpGitHubCommand = new RelayCommand(_ => OpenGitHubRepo());
        HelpAboutCommand = new RelayCommand(_ => ShowAboutDialog());
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the persisted application settings.
    /// </summary>
    public AppSettings Settings { get; } = AppSettings.Load();

    /// <summary>
    /// Gets the currently open folder ("project") context, wrapping <see cref="AppSettings.LastFolderPath"/>.
    /// </summary>
    public ProjectContext Project { get; }

    /// <summary>
    /// Gets or sets whether the vertical column guide line is shown in the editor.
    /// Changes are persisted to settings immediately.
    /// </summary>
    public bool ShowColumnGuide
    {
        get => Settings.ShowColumnGuide;
        set
        {
            if (Settings.ShowColumnGuide == value) return;
            Settings.ShowColumnGuide = value;
            OnPropertyChanged();
            Settings.Save();
        }
    }

    /// <summary>
    /// Gets or sets whether the editor wraps long lines. Changes are persisted to settings immediately.
    /// </summary>
    public bool WordWrap
    {
        get => Settings.WordWrap;
        set
        {
            if (Settings.WordWrap == value) return;
            Settings.WordWrap = value;
            OnPropertyChanged();
            Settings.Save();
        }
    }

    /// <summary>
    /// Gets or sets whether the left panel (folder explorer) is open.
    /// </summary>
    public bool IsLeftPanelOpen
    {
        get => _isLeftPanelOpen;
        set { if (_isLeftPanelOpen == value) return; _isLeftPanelOpen = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the right panel (Quick Keys / PETSCII reference) is open.
    /// </summary>
    public bool IsRightPanelOpen
    {
        get => _isRightPanelOpen;
        set { if (_isRightPanelOpen == value) return; _isRightPanelOpen = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets the root items shown in the folder explorer tree.
    /// </summary>
    public ObservableCollection<FileTreeItem> FolderItems
    {
        get => _folderItems;
        private set { _folderItems = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets the title shown above the folder explorer tree (the open folder's name).
    /// </summary>
    public string ExplorerTitle
    {
        get => _explorerTitle;
        set { _explorerTitle = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets the root items shown in the C64U FTP explorer tree.
    /// </summary>
    public ObservableCollection<C64UFileItem> C64UFileItems
    {
        get => _c64uFileItems;
        private set { _c64uFileItems = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets the current connection state of the C64U FTP explorer.
    /// </summary>
    public C64UConnectionState C64UConnectionState
    {
        get => _c64uConnectionState;
        private set
        {
            if (_c64uConnectionState == value) return;
            _c64uConnectionState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsC64UNotConnected));
            OnPropertyChanged(nameof(IsC64UConnecting));
            OnPropertyChanged(nameof(IsC64UConnected));
            OnPropertyChanged(nameof(C64UHeaderText));
        }
    }

    /// <summary>
    /// Gets whether the C64U FTP explorer is not connected.
    /// </summary>
    public bool IsC64UNotConnected => C64UConnectionState == C64UConnectionState.NotConnected;

    /// <summary>
    /// Gets whether the C64U FTP explorer is in the middle of connecting.
    /// </summary>
    public bool IsC64UConnecting => C64UConnectionState == C64UConnectionState.Connecting;

    /// <summary>
    /// Gets whether the C64U FTP explorer is connected.
    /// </summary>
    public bool IsC64UConnected => C64UConnectionState == C64UConnectionState.Connected;

    /// <summary>
    /// Gets the C64 Ultimate's FTP host name or IP address, derived from
    /// <see cref="AppSettings.C64UUrl"/>, or empty if that URL isn't configured.
    /// </summary>
    public string C64UFtpHost => GetC64UFtpHost(Settings.C64UUrl);

    /// <summary>
    /// Gets the device's own network hostname, as reported by its REST API, or null if not
    /// yet fetched or unavailable.
    /// </summary>
    public string? C64UDeviceHostname
    {
        get => _c64uDeviceHostname;
        private set
        {
            if (_c64uDeviceHostname == value) return;
            _c64uDeviceHostname = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(C64UHeaderText));
        }
    }

    /// <summary>
    /// Gets the text shown in the C64U explorer panel header: "{host} - {device hostname}"
    /// once connected and the device's hostname is known, otherwise "C64U".
    /// </summary>
    public string C64UHeaderText => IsC64UConnected && !string.IsNullOrWhiteSpace(C64UDeviceHostname)
        ? $"{C64UFtpHost} — {C64UDeviceHostname}"
        : "C64U";

    /// <summary>
    /// Gets the active FTP client for the C64U explorer, or null if not currently connected.
    /// </summary>
    public C64UFtpClient? C64UFtp => _c64uFtpClient;

    /// <summary>
    /// Gets the current status of Drive A, or null if not yet fetched.
    /// </summary>
    public C64UDriveStatus? C64UDriveA
    {
        get => _c64uDriveA;
        private set
        {
            _c64uDriveA = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(C64UDriveALabel));
            OnPropertyChanged(nameof(IsC64UDriveAMounted));
        }
    }

    /// <summary>
    /// Gets the current status of Drive B, or null if not yet fetched.
    /// </summary>
    public C64UDriveStatus? C64UDriveB
    {
        get => _c64uDriveB;
        private set
        {
            _c64uDriveB = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(C64UDriveBLabel));
            OnPropertyChanged(nameof(IsC64UDriveBMounted));
        }
    }

    /// <summary>
    /// Gets the display label for Drive A's status footer row: the mounted image's file name,
    /// or "empty" if nothing is mounted.
    /// </summary>
    public string C64UDriveALabel => string.IsNullOrEmpty(C64UDriveA?.ImageFile) ? "empty" : Path.GetFileName(C64UDriveA.ImageFile);

    /// <summary>
    /// Gets the display label for Drive B's status footer row: the mounted image's file name,
    /// or "empty" if nothing is mounted.
    /// </summary>
    public string C64UDriveBLabel => string.IsNullOrEmpty(C64UDriveB?.ImageFile) ? "empty" : Path.GetFileName(C64UDriveB.ImageFile);

    /// <summary>
    /// Gets whether Drive A currently has a disk image mounted.
    /// </summary>
    public bool IsC64UDriveAMounted => !string.IsNullOrEmpty(C64UDriveA?.ImageFile);

    /// <summary>
    /// Gets whether Drive B currently has a disk image mounted.
    /// </summary>
    public bool IsC64UDriveBMounted => !string.IsNullOrEmpty(C64UDriveB?.ImageFile);

    /// <summary>
    /// Gets or sets the current file path of the active tab, or null if there is no active
    /// tab or the active tab has no file.
    /// </summary>
    public string? CurrentFilePath
    {
        get => ActiveTab?.FilePath;
        set { if (ActiveTab != null) ActiveTab.FilePath = value; }
    }

    /// <summary>
    /// Gets or sets whether the active tab has unsaved changes.
    /// </summary>
    public bool IsModified
    {
        get => ActiveTab?.IsModified ?? false;
        set { if (ActiveTab != null) ActiveTab.IsModified = value; }
    }

    /// <summary>
    /// Gets the window title, which includes the current file name and a modified
    /// indicator if there are unsaved changes.
    /// </summary>
    public string Title
    {
        get
        {
            string fileName = string.IsNullOrEmpty(CurrentFilePath) ? "Untitled" : Path.GetFileName(CurrentFilePath);
            string modified = IsModified ? "*" : "";
            return $"READYCode - {fileName}{modified}";
        }
    }

    /// <summary>
    /// Gets or sets whether the status bar is shown. Changes are persisted to settings immediately.
    /// </summary>
    public bool ShowStatusBar
    {
        get => Settings.ShowStatusBar;
        set
        {
            if (Settings.ShowStatusBar == value) return;
            Settings.ShowStatusBar = value;
            OnPropertyChanged();
            Settings.Save();
        }
    }

    /// <summary>
    /// Gets or sets whether the C64U menu is shown in the main menu bar.
    /// </summary>
    public bool ShowC64UMenu
    {
        get => Settings.ShowC64UMenu;
        set
        {
            if (Settings.ShowC64UMenu == value) return;
            Settings.ShowC64UMenu = value;
            OnPropertyChanged();
            Settings.Save();
        }
    }

    /// <summary>
    /// Gets or sets whether the VICE menu is shown in the main menu bar.
    /// </summary>
    public bool ShowViceMenu
    {
        get => Settings.ShowViceMenu;
        set
        {
            if (Settings.ShowViceMenu == value) return;
            Settings.ShowViceMenu = value;
            OnPropertyChanged();
            Settings.Save();
        }
    }

    /// <summary>
    /// Gets the text shown in the status bar for the current file: either its file path,
    /// or "New File" if there is none.
    /// </summary>
    public string FileStatusText => string.IsNullOrEmpty(CurrentFilePath) ? "New File" : CurrentFilePath;

    /// <summary>
    /// Gets or sets the text shown in the status bar for the document's total line count.
    /// </summary>
    public string LineCountText
    {
        get => _lineCountText;
        set
        {
            if (_lineCountText == value) return;
            _lineCountText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the text shown in the status bar for the cursor's current line and column position.
    /// </summary>
    public string ScreenPositionText
    {
        get => _screenPositionText;
        set
        {
            if (_screenPositionText == value) return;
            _screenPositionText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the current status bar message (e.g. an error or success message).
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the severity of the current <see cref="StatusMessage"/>, which determines its
    /// color in the status bar.
    /// </summary>
    public StatusType StatusType
    {
        get => _statusType;
        private set
        {
            if (_statusType == value) return;
            _statusType = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the collection of currently open editor tabs.
    /// </summary>
    public ObservableCollection<EditorTab> OpenTabs { get; } = new();

    /// <summary>
    /// Gets or sets the currently active editor tab.
    /// </summary>
    public EditorTab? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (ReferenceEquals(_activeTab, value)) return;
            if (_activeTab != null)
                _activeTab.PropertyChanged -= OnActiveTabPropertyChanged;
            _activeTab = value;
            if (_activeTab != null)
                _activeTab.PropertyChanged += OnActiveTabPropertyChanged;
            OnPropertyChanged();
            NotifyFileStateChanged();
        }
    }

    // File
    /// <summary>
    /// Gets the command that prints the active tab's source code.
    /// </summary>
    public ICommand FilePrintCommand { get; }

    /// <summary>
    /// Gets the command that shows a print preview of the active tab's source code.
    /// </summary>
    public ICommand FilePrintPreviewCommand { get; }

    /// <summary>
    /// Gets the command that shows the page setup dialog.
    /// </summary>
    public ICommand FilePageSetupCommand { get; }

    // C64U
    /// <summary>
    /// Gets the command that transfers the active tab's code to the C64 Ultimate.
    /// </summary>
    public ICommand C64UTransferCommand { get; }

    /// <summary>
    /// Gets the command that transfers the active tab's code to the C64 Ultimate and runs it.
    /// </summary>
    public ICommand C64URunCommand { get; }

    /// <summary>
    /// Gets the command that resets the C64 Ultimate.
    /// </summary>
    public ICommand C64UResetCommand { get; }

    /// <summary>
    /// Gets the command that reboots the C64 Ultimate.
    /// </summary>
    public ICommand C64URebootCommand { get; }

    /// <summary>
    /// Gets the command that pauses the C64 Ultimate.
    /// </summary>
    public ICommand C64UPauseCommand { get; }

    /// <summary>
    /// Gets the command that resumes the C64 Ultimate.
    /// </summary>
    public ICommand C64UResumeCommand { get; }

    /// <summary>
    /// Gets the command that powers off the C64 Ultimate.
    /// </summary>
    public ICommand C64UPowerOffCommand { get; }

    /// <summary>
    /// Gets the command that shows system information about the C64 Ultimate.
    /// </summary>
    public ICommand C64USystemInfoCommand { get; }

    // VICE
    /// <summary>
    /// Gets the command that transfers the active tab's code to VICE.
    /// </summary>
    public ICommand ViceTransferCommand { get; }

    /// <summary>
    /// Gets the command that transfers the active tab's code to VICE and runs it.
    /// </summary>
    public ICommand ViceRunCommand { get; }

    /// <summary>
    /// Gets the command that resets the machine running in VICE.
    /// </summary>
    public ICommand ViceResetCommand { get; }

    /// <summary>
    /// Gets the command that reboots the machine running in VICE.
    /// </summary>
    public ICommand ViceRebootCommand { get; }

    /// <summary>
    /// Gets the command that pauses the machine running in VICE.
    /// </summary>
    public ICommand VicePauseCommand { get; }

    /// <summary>
    /// Gets the command that resumes the machine running in VICE.
    /// </summary>
    public ICommand ViceResumeCommand { get; }

    /// <summary>
    /// Gets the command that powers off (quits) VICE.
    /// </summary>
    public ICommand VicePowerOffCommand { get; }

    /// <summary>
    /// Gets the command that shows version information about VICE.
    /// </summary>
    public ICommand ViceSystemInfoCommand { get; }

    // Help
    /// <summary>
    /// Gets the command that opens the READYCode GitHub repository in the default browser.
    /// </summary>
    public ICommand HelpGitHubCommand { get; }

    /// <summary>
    /// Gets the command that shows the About dialog.
    /// </summary>
    public ICommand HelpAboutCommand { get; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the status bar message and severity.
    /// </summary>
    /// <param name="message">The message to show in the status bar.</param>
    /// <param name="type">The severity of the message, which determines its color.</param>
    public void SetStatus(string message, StatusType type = StatusType.Info)
    {
        StatusMessage = message;
        StatusType = type;
    }

    /// <summary>
    /// Re-raises property-changed notifications for the C64U/VICE menu visibility settings.
    /// Call after settings are written directly to <see cref="Settings"/> (bypassing the
    /// <see cref="ShowC64UMenu"/>/<see cref="ShowViceMenu"/> setters) so bound menu items refresh.
    /// </summary>
    public void RefreshMenuVisibility()
    {
        OnPropertyChanged(nameof(ShowC64UMenu));
        OnPropertyChanged(nameof(ShowViceMenu));
    }

    /// <summary>
    /// Loads the folder explorer tree from the given folder path, replacing any existing items.
    /// </summary>
    /// <param name="folderPath">The folder to load.</param>
    public void LoadFolder(string folderPath)
    {
        string name = Path.GetFileName(folderPath);
        ExplorerTitle = string.IsNullOrEmpty(name) ? folderPath.ToUpperInvariant() : name.ToUpperInvariant();

        FolderItems.Clear();
        try
        {
            foreach (string dir in Directory.GetDirectories(folderPath)
                                            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                FolderItems.Add(new FileTreeItem(dir, true));

            foreach (string file in Directory.GetFiles(folderPath)
                                             .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                FolderItems.Add(new FileTreeItem(file, false));
        }
        catch { /* Access denied, etc. */ }
    }

    /// <summary>
    /// Reloads the folder explorer tree from the last-used folder, preserving the expanded
    /// state of any folders.
    /// </summary>
    public void RefreshRootItems()
    {
        string folder = Project.RootPath;

        if (string.IsNullOrEmpty(folder)) return;

        var expandedPaths = FolderItems
            .Where(i => i.IsFolder && i.IsExpanded)
            .Select(i => i.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        LoadFolder(folder);

        foreach (var item in FolderItems.Where(i => i.IsFolder && expandedPaths.Contains(i.FullPath)))
            item.IsExpanded = true;
    }

    /// <summary>
    /// Minifies BASIC source code according to settings before transferring it to the C64
    /// Ultimate. No-op when <see cref="AppSettings.MinifyOnTransfer"/> is off.
    /// </summary>
    /// <param name="text">The BASIC source code to prepare.</param>
    public string PrepareCodeForTransfer(string text)
    {
        if (!Settings.MinifyOnTransfer) return text;
        return CodeMinifier.Minify(text,
            removeWhitespace:       Settings.MinifyRemoveWhitespace,
            replace0WithPeriod:     Settings.MinifyReplaceZeroWithDot,
            useScientificNotation:  Settings.MinifyUseScientificNotation,
            removeComments:         Settings.MinifyRemoveComments,
            simplifyNextStatements: Settings.MinifySimplifyNext,
            renumberLines:          Settings.MinifyRenumberLines);
    }

    /// <summary>
    /// Connects to the C64 Ultimate's FTP server using the host derived from
    /// <see cref="AppSettings.C64UUrl"/>, and loads the root folder listing on success.
    /// </summary>
    public async Task ConnectToC64UAsync()
    {
        string host = C64UFtpHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            C64UConnectionState = C64UConnectionState.NotConnected;
            return;
        }

        C64UConnectionState = C64UConnectionState.Connecting;

        var client = new C64UFtpClient();
        try
        {
            await client.ConnectAsync(host);
            var entries = await client.ListDirectoryAsync("/");

            _c64uFtpClient?.Dispose();
            _c64uFtpClient = client;

            C64UFileItems.Clear();
            foreach (var entry in entries)
                C64UFileItems.Add(new C64UFileItem(client, entry.FullPath, entry.IsFolder, entry.Size));

            C64UConnectionState = C64UConnectionState.Connected;

            // Best-effort: the panel header still shows the FTP host if the REST API is
            // unreachable, so a failure here shouldn't affect the FTP connection itself.
            try
            {
                var info = await new C64UltimateClient().GetInfoAsync(Settings.C64UUrl);
                C64UDeviceHostname = info.Hostname;
            }
            catch
            {
                C64UDeviceHostname = null;
            }

            await RefreshC64UDriveStatusAsync();
        }
        catch (Exception ex)
        {
            client.Dispose();
            C64UConnectionState = C64UConnectionState.NotConnected;
            C64UDeviceHostname = null;
            C64UDriveA = null;
            C64UDriveB = null;
            SetStatus($"Could not connect to the C64 Ultimate: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>
    /// Reloads the C64U FTP explorer's root folder listing, preserving the expanded state of
    /// any folders.
    /// </summary>
    public async Task RefreshC64UFolderAsync()
    {
        if (_c64uFtpClient == null) return;

        var expandedPaths = C64UFileItems
            .Where(i => i.IsFolder && i.IsExpanded)
            .Select(i => i.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var entries = await _c64uFtpClient.ListDirectoryAsync("/");
            C64UFileItems.Clear();
            foreach (var entry in entries)
                C64UFileItems.Add(new C64UFileItem(_c64uFtpClient, entry.FullPath, entry.IsFolder, entry.Size));

            foreach (var item in C64UFileItems.Where(i => i.IsFolder && expandedPaths.Contains(i.FullPath)))
                item.IsExpanded = true;

            await RefreshC64UDriveStatusAsync();
        }
        catch (Exception ex)
        {
            _c64uFtpClient?.Dispose();
            _c64uFtpClient = null;
            C64UFileItems.Clear();
            C64UDeviceHostname = null;
            C64UDriveA = null;
            C64UDriveB = null;
            C64UConnectionState = C64UConnectionState.NotConnected;
            SetStatus($"Lost connection to the C64 Ultimate: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>
    /// Refreshes Drive A/B mount status from the device's REST API. Best-effort - a failure
    /// here doesn't affect the FTP connection or file listing.
    /// </summary>
    public async Task RefreshC64UDriveStatusAsync()
    {
        try
        {
            var drives = await new C64UltimateClient().GetDrivesAsync(Settings.C64UUrl);
            C64UDriveA = drives.FirstOrDefault(d => d.Id == "a");
            C64UDriveB = drives.FirstOrDefault(d => d.Id == "b");
        }
        catch
        {
            C64UDriveA = null;
            C64UDriveB = null;
        }
    }

    /// <summary>
    /// Mounts a disk image already on the device's storage to the given drive, then refreshes
    /// drive status so the footer reflects the change.
    /// </summary>
    /// <param name="driveId">The drive to mount to (e.g. "a", "b").</param>
    /// <param name="imagePath">The full path of the disk image on the device.</param>
    public async Task MountC64UDriveAsync(string driveId, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("Please set the Commodore 64 Ultimate URL in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            await new C64UltimateClient().MountDriveAsync(Settings.C64UUrl, driveId, imagePath);
            SetStatus($"Mounted \"{Path.GetFileName(imagePath)}\" to Drive {driveId.ToUpperInvariant()}.");
            await RefreshC64UDriveStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not mount to Drive {driveId.ToUpperInvariant()}: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>
    /// Ejects the disk image currently mounted on the given drive, then refreshes drive status
    /// so the footer reflects the change.
    /// </summary>
    /// <param name="driveId">The drive to eject (e.g. "a", "b").</param>
    public async Task EjectC64UDriveAsync(string driveId)
    {
        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("Please set the Commodore 64 Ultimate URL in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            await new C64UltimateClient().RemoveDriveAsync(Settings.C64UUrl, driveId);
            SetStatus($"Ejected Drive {driveId.ToUpperInvariant()}.");
            await RefreshC64UDriveStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not eject Drive {driveId.ToUpperInvariant()}: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>
    /// Transfers already-tokenized PRG data to the C64 Ultimate and runs it.
    /// </summary>
    /// <param name="prgData">The PRG-format program data to run.</param>
    public async Task RunOnC64UAsync(byte[] prgData)
    {
        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("Please set the Commodore 64 Ultimate URL in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            var client = new C64UltimateClient();
            SetStatus("Transferring program to C64 Ultimate…");
            await client.RunPrgAsync(Settings.C64UUrl, prgData);
            SetStatus("Program transferred and running on the C64 Ultimate.", StatusType.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer/program execution failed: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>
    /// Transfers already-tokenized PRG data to the C64 Ultimate without running it.
    /// </summary>
    /// <param name="prgData">The PRG-format program data to load.</param>
    public async Task LoadOnC64UAsync(byte[] prgData)
    {
        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("C64U URL not set. Go to Preferences > Settings to configure it.");
            return;
        }

        try
        {
            var client = new C64UltimateClient();
            SetStatus("Transferring program to C64 Ultimate…");
            await client.LoadPrgAsync(Settings.C64UUrl, prgData);
            SetStatus("Program transferred to C64 Ultimate successfully.");
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    #endregion

    #region Interface Implementations

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    // Opens the READYCode GitHub repository in the user's default browser.
    private void OpenGitHubRepo()
    {
        Process.Start(new ProcessStartInfo(Settings.GitHubUrl) { UseShellExecute = true });
    }

    // Shows the About dialog with application information.
    private static void ShowAboutDialog()
    {
        new Views.AboutWindow { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    // Raises the PropertyChanged event for the given property name, or for the caller member if no name is provided.
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Forwards file-state-relevant changes from the active tab to this view model's own notifications.
    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorTab.IsModified) or nameof(EditorTab.FilePath) or nameof(EditorTab.FileName))
            NotifyFileStateChanged();
    }

    private void NotifyFileStateChanged()
    {
        OnPropertyChanged(nameof(CurrentFilePath));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(FileStatusText));
    }

    // Prints the active tab's source code. Shows a status message if there is nothing to print.
    private void PrintActiveTab()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("No code to print. Write some BASIC code first.", StatusType.Error);
            return;
        }

        _printer.Print((Window)Application.Current.MainWindow, text, ActiveTab!.FileName);
    }

    // Shows a print preview of the active tab's source code. Shows a status message if there is nothing to print.
    private void PrintPreviewActiveTab()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("No code to print. Write some BASIC code first.", StatusType.Error);
            return;
        }

        _printer.PrintPreview((Window)Application.Current.MainWindow, text, ActiveTab!.FileName);
    }

    // Gates Transfer/Run: both need an open tab with at least one character typed into it.
    private bool HasNonEmptyActiveTab() => !string.IsNullOrEmpty(ActiveTab?.Document.Text);

    // Gates Print/Print Preview, which only need an open tab regardless of its content.
    private bool HasActiveTab() => ActiveTab != null;

    // Transfers the current BASIC code to the C64 Ultimate.
    // Shows status messages and errors in the status bar.
    private async Task TransferCurrentProgramAsync()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("No code to transfer. Write some BASIC code first.", StatusType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("C64U URL not set. Go to Preferences > Settings to configure it.");
            return;
        }

        try
        {
            SetStatus("Transferring program to C64 Ultimate…");

            var converter = new PrgConverter();
            var prgData = converter.ConvertToPrg(PrepareCodeForTransfer(text));

            var client = new C64UltimateClient();
            await client.LoadPrgAsync(Settings.C64UUrl, prgData);

            SetStatus("Program transferred to C64 Ultimate successfully.");
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    // Transfers the current BASIC code to the C64 Ultimate and starts execution.
    private async Task RunCurrentProgramAsync()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("There is no code to run. Please write some BASIC code first.", StatusType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("Please set the Commodore 64 Ultimate URL in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            var converter = new PrgConverter();
            var prgData = converter.ConvertToPrg(PrepareCodeForTransfer(text));
            var client = new C64UltimateClient();

            SetStatus("Transferring program to C64 Ultimate…");

            await client.RunPrgAsync(Settings.C64UUrl, prgData);

            SetStatus("Program transferred and running on the C64 Ultimate.", StatusType.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer/program execution failed: {ex.Message}", StatusType.Error);
        }
    }

    // Transfers the current BASIC code to VICE without running it. The user can type RUN
    // from within the emulator to start it.
    private async Task TransferToViceAsync()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("No code to transfer. Write some BASIC code first.", StatusType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.ViceEmulatorPath))
        {
            SetStatus("VICE emulator path not set. Go to Preferences > Settings to configure it.");
            return;
        }

        try
        {
            SetStatus("Transferring program to VICE…");

            var converter = new PrgConverter();
            var prgData = converter.ConvertToPrg(PrepareCodeForTransfer(text));

            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);
            await client.TransferAsync(Settings.ViceEmulatorPath, prgData, ActiveTab!.FileName, Settings.ViceBringToForeground);

            SetStatus("Program transferred to VICE. Type RUN in the emulator to start it.");
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    // Transfers the current BASIC code to VICE and starts execution.
    private async Task RunOnViceAsync()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("There is no code to run. Please write some BASIC code first.", StatusType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.ViceEmulatorPath))
        {
            SetStatus("Please set the VICE emulator path in Settings - Preferences first.", StatusType.Error);
            return;
        }

        try
        {
            var converter = new PrgConverter();
            var prgData = converter.ConvertToPrg(PrepareCodeForTransfer(text));
            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);

            SetStatus("Transferring program to VICE…");

            await client.RunAsync(Settings.ViceEmulatorPath, prgData, ActiveTab!.FileName, Settings.ViceBringToForeground);

            SetStatus("Program transferred and running on VICE.", StatusType.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer/program execution failed: {ex.Message}", StatusType.Error);
        }
    }

    // Performs a machine action (reset, reboot, pause, resume, poweroff) on VICE via its binary monitor.
    private async Task ViceMachineActionAsync(Func<ViceClient, Task> action, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(Settings.ViceEmulatorPath))
        {
            SetStatus("VICE emulator path not set. Go to Preferences > Settings to configure it.");
            return;
        }

        try
        {
            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);
            await action(client);
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            SetStatus($"VICE action failed: {ex.Message}", StatusType.Error);
        }
    }

    // Performs a machine action (reset, reboot, pause, resume, poweroff) on the C64 Ultimate.
    private async Task MachineActionAsync(string action, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("C64U URL not set. Go to Preferences > Settings to configure it.");
            return;
        }

        try
        {
            var client = new C64UltimateClient();
            await client.MachineActionAsync(Settings.C64UUrl, action);
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            SetStatus($"Machine {action} failed: {ex.Message}", StatusType.Error);
        }
    }

    // Retrieves system information from the C64 Ultimate and shows it in a dialog.
    private async Task ShowC64USystemInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            MessageBox.Show(
                "Please set the Commodore 64 Ultimate URL in Settings - Preferences first.",
                "Commodore 64 Ultimate",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var client = new C64UltimateClient();
            var info = await client.GetInfoAsync(Settings.C64UUrl);

            new AboutC64UWindow(info) { Owner = Application.Current.MainWindow }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error retrieving information from the Commodore 64 Ultimate: {ex.Message}",
                "About Commodore 64 Ultimate",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Retrieves version information from VICE and shows it in a dialog.
    private async Task ShowViceSystemInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.ViceEmulatorPath))
        {
            MessageBox.Show(
                "Please set the VICE emulator path in Settings - Preferences first.",
                "VICE",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);
            var info = await client.GetInfoAsync();

            new AboutViceWindow(info, Settings.ViceEmulatorPath) { Owner = Application.Current.MainWindow }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error retrieving information from VICE: {ex.Message}",
                "About VICE",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Strips the scheme and path from the C64U's REST URL, leaving just the host name or IP
    // address to connect to over FTP.
    private static string GetC64UFtpHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
    }

    #endregion
}
