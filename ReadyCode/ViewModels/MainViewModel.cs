// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ReadyCode.Assembler;
using ReadyCode.C64U;
using ReadyCode.Debugger;
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
    private ComparableFileRef? _pendingCompareFile;
    private readonly SourcePrinter _printer = new();

    private IDebugSession? _debugSession;
    private bool _isDebugStopped;
    private EditorTab? _debugTab;
    private BasicLineAddressTable? _debugLineAddressTable;
    private int? _debugCurrentDocumentLine;

    // How long to wait after loading a standalone (no BASIC loader stub) program - on either
    // the C64U (load_prg) or VICE (autostart with RL=0) - before typing a "SYS <origin>"
    // command, since both trigger a machine reset first. Needs to be long enough for the
    // KERNAL's cold-start/BASIC-ready sequence to finish and start reading the keyboard buffer
    // again, or the typed command is silently lost. Not user-configurable (yet) - if this turns
    // out to be too short/long, it's the first thing to tune. Internal (not private) so
    // MainWindow's own file-tree Load/Run handlers - which apply the same SYS trick to a file
    // that isn't necessarily the active tab - share this exact same value.
    internal static readonly TimeSpan SysCommandDelay = TimeSpan.FromSeconds(0.5);

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

        SymbolGroups.Add(ConstantSymbols);
        SymbolGroups.Add(LabelSymbols);

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

        // Starting a session now happens from the C64U/VICE menus' own "Debug" items (one per
        // target, since the active debug target is determined solely by which one was clicked);
        // the Debug menu itself only ever acts on a session already in progress.
        DebugStartOnViceCommand = new RelayCommand(async _ => await DebugStartOnViceAsync(),
            _ => !IsDebugging && ActiveTab is { Language: EditorLanguage.Basic });
        DebugStartOnC64UCommand = new RelayCommand(async _ => await DebugStartOnC64UAsync(),
            _ => !IsDebugging && ActiveTab is { Language: EditorLanguage.Basic });

        DebugContinueCommand = new RelayCommand(async _ => await DebugContinueAsync(), _ => IsDebugging && IsDebugStopped);
        DebugPauseCommand = new RelayCommand(async _ => await DebugPauseAsync(), _ => IsDebugging && !IsDebugStopped);
        DebugStepLineCommand = new RelayCommand(async _ => await DebugStepLineAsync(), _ => IsDebugging && IsDebugStopped);
        DebugStepOutCommand = new RelayCommand(async _ => await DebugStepOutAsync(),
            _ => IsDebugging && IsDebugStopped && DebugSession!.SupportsCallStackAndStepOut);
        // Deliberately gated on IsDebugging alone, not IsDebugStopped like the other controls -
        // Stop is this session's only escape hatch. If the running program gets halted from
        // outside the debugger entirely (RUN/STOP on the C64U, Esc in VICE), our own Stopped/
        // ConnectionLost events never fire (neither goes through the checkpoint/GONE hook this
        // debugger watches), so IsDebugStopped stays stuck at false forever. Requiring it here
        // too would gray out Stop right alongside Start, with no way back in short of restarting
        // the app - DisposeAsync is already best-effort/tolerant of a desynced target, so it's
        // safe to always allow.
        DebugStopCommand = new RelayCommand(async _ => await DebugStopAsync(), _ => IsDebugging);

        HelpGitHubCommand = new RelayCommand(_ => OpenGitHubRepo());
        HelpDocsCommand = new RelayCommand(_ => OpenDocs());
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
    /// Gets or sets whether the Variable Explorer section is shown below the Folder/C64U tree.
    /// Changes are persisted to settings immediately.
    /// </summary>
    public bool ShowVariableExplorer
    {
        get => Settings.ShowVariableExplorer;
        set
        {
            if (Settings.ShowVariableExplorer == value) return;
            Settings.ShowVariableExplorer = value;
            OnPropertyChanged();
            Settings.Save();
        }
    }

    /// <summary>
    /// Gets or sets whether the bottom debug panel (Variables/Breakpoints/Call Stack) is open.
    /// Changes are persisted to settings immediately.
    /// </summary>
    public bool IsDebugPanelOpen
    {
        get => Settings.IsDebugPanelOpen;
        set
        {
            if (Settings.IsDebugPanelOpen == value) return;
            Settings.IsDebugPanelOpen = value;
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
    /// Gets the variables found in the active document's Variable Explorer tree, kept up to date
    /// (and diffed in place, to preserve each node's expanded state) by <c>MainWindow</c> as the
    /// document changes.
    /// </summary>
    public ObservableCollection<VariableInfo> Variables { get; } = new();

    /// <summary>
    /// Gets the "Constants" root group of the active assembly document's Symbol Explorer tree,
    /// kept up to date (and diffed in place, to preserve each node's expanded state) by
    /// <c>MainWindow</c> as the document changes.
    /// </summary>
    public AsmSymbolGroupInfo ConstantSymbols { get; } = new("Constants");

    /// <summary>
    /// Gets the "Labels" root group of the active assembly document's Symbol Explorer tree,
    /// kept up to date (and diffed in place, to preserve each node's expanded state) by
    /// <c>MainWindow</c> as the document changes.
    /// </summary>
    public AsmSymbolGroupInfo LabelSymbols { get; } = new("Labels");

    /// <summary>
    /// Gets the Symbol Explorer tree's fixed root collection - always exactly
    /// <see cref="ConstantSymbols"/> followed by <see cref="LabelSymbols"/>.
    /// </summary>
    public ObservableCollection<AsmSymbolGroupInfo> SymbolGroups { get; } = new();

    /// <summary>
    /// Gets the project-wide Search panel's results, one entry per file with at least one match,
    /// each holding its own matches as child nodes. Repopulated wholesale by <c>MainWindow</c>
    /// each time a project-wide search runs.
    /// </summary>
    public ObservableCollection<ProjectSearchFileResult> SearchResults { get; } = new();

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

    /// <summary>
    /// Gets or sets the file currently pending as the "left" side of a File Compare, picked via
    /// "Select file for comparison" in either the Folder Explorer or C64U Explorer context menu.
    /// Null when nothing is pending. Cleared once a comparison is successfully opened.
    /// </summary>
    public ComparableFileRef? PendingCompareFile
    {
        get => _pendingCompareFile;
        set
        {
            if (Equals(_pendingCompareFile, value)) return;
            _pendingCompareFile = value;
            OnPropertyChanged();
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

    // Debug
    /// <summary>
    /// Gets every breakpoint set in the currently open project, independent of which files
    /// are open in tabs right now.
    /// </summary>
    public BreakpointStore BreakpointStore { get; } = new();

    /// <summary>
    /// Gets the persisted debugger configuration (breakpoints, watch expressions, default
    /// target) for every known project - app-level storage only, never written into a project
    /// folder or disk image.
    /// </summary>
    public DebugConfigStore DebugConfig { get; } = DebugConfigStore.Load();

    /// <summary>
    /// Gets the live debug session (VICE or C64 Ultimate - whichever target was used to start
    /// it), or null when no BASIC debug session is active.
    /// </summary>
    public IDebugSession? DebugSession
    {
        get => _debugSession;
        private set
        {
            if (ReferenceEquals(_debugSession, value)) return;
            _debugSession = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDebugging));
        }
    }

    /// <summary>
    /// Gets whether a BASIC debug session is currently active.
    /// </summary>
    public bool IsDebugging => DebugSession != null;

    /// <summary>
    /// Gets whether the active debug session is currently halted (as opposed to running).
    /// Meaningless while <see cref="IsDebugging"/> is false.
    /// </summary>
    public bool IsDebugStopped
    {
        get => _isDebugStopped;
        private set
        {
            if (_isDebugStopped == value) return;
            _isDebugStopped = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the tab a debug session is attached to, so Continue/Step/Stop stay scoped to that
    /// file even if the user switches to a different tab while debugging. Null when
    /// <see cref="IsDebugging"/> is false.
    /// </summary>
    public EditorTab? DebugTab
    {
        get => _debugTab;
        private set { _debugTab = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets the line address table built for <see cref="DebugTab"/> at the moment debugging
    /// started, used to resolve a halted BASIC line number back to an editor line to highlight.
    /// Null when <see cref="IsDebugging"/> is false.
    /// </summary>
    public BasicLineAddressTable? DebugLineAddressTable
    {
        get => _debugLineAddressTable;
        private set { _debugLineAddressTable = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets the 1-based document line currently highlighted as the halted line, or null while
    /// running or not debugging.
    /// </summary>
    public int? DebugCurrentDocumentLine
    {
        get => _debugCurrentDocumentLine;
        private set { _debugCurrentDocumentLine = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets the simple variables read from the machine at the last stop, sorted by name. Empty
    /// while running or not debugging.
    /// </summary>
    public ObservableCollection<BasicVariable> DebugVariables { get; } = new();

    /// <summary>
    /// Gets the GOSUB call stack read from the machine at the last stop, innermost first. Empty
    /// while running or not debugging.
    /// </summary>
    public ObservableCollection<GosubFrame> DebugCallStack { get; } = new();

    /// <summary>
    /// Gets the command that starts a new BASIC debug session on VICE for the active tab - the
    /// VICE menu's "Debug" item.
    /// </summary>
    public ICommand DebugStartOnViceCommand { get; }

    /// <summary>
    /// Gets the command that starts a new BASIC debug session on the C64 Ultimate for the active
    /// tab - the C64U menu's "Debug" item. Step Out and the Call Stack panel stay unavailable for
    /// a session started this way - see <see cref="IDebugSession.SupportsCallStackAndStepOut"/>.
    /// </summary>
    public ICommand DebugStartOnC64UCommand { get; }

    /// <summary>
    /// Gets the command that resumes a halted debug session - the Debug menu's "Continue".
    /// </summary>
    public ICommand DebugContinueCommand { get; }

    /// <summary>
    /// Gets the command that halts the active debug session at the start of the next BASIC line.
    /// </summary>
    public ICommand DebugPauseCommand { get; }

    /// <summary>
    /// Gets the command that executes one BASIC line and halts again.
    /// </summary>
    public ICommand DebugStepLineCommand { get; }

    /// <summary>
    /// Gets the command that runs until execution returns from the innermost GOSUB or FOR loop
    /// active at the current stop point.
    /// </summary>
    public ICommand DebugStepOutCommand { get; }

    /// <summary>
    /// Gets the command that ends the active debug session.
    /// </summary>
    public ICommand DebugStopCommand { get; }

    // Help
    /// <summary>
    /// Gets the command that opens the READYCode GitHub repository in the default browser.
    /// </summary>
    public ICommand HelpGitHubCommand { get; }

    /// <summary>
    /// Gets the command that opens the READYCode online documentation in the default browser.
    /// </summary>
    public ICommand HelpDocsCommand { get; }

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

        LoadBreakpointsForProject(folderPath);
    }

    // Replaces BreakpointStore's contents with whatever was persisted for this folder, if
    // anything - breakpoints are app-level storage only (never written into the project folder
    // itself), keyed by the folder's own canonical path. A loose file with no open folder simply
    // keeps breakpoints in-memory for the session, with no cross-restart persistence.
    private void LoadBreakpointsForProject(string folderPath)
    {
        string projectKey = DebugConfigStore.GetFolderProjectKey(folderPath);
        var records = DebugConfig.GetBreakpoints(projectKey);
        BreakpointStore.ReplaceAll(records.Select(r => new Breakpoint
        {
            FilePath = r.FilePath,
            LineNumber = r.LineNumber,
            IsEnabled = r.Enabled,
        }));
    }

    /// <summary>
    /// Saves <see cref="BreakpointStore"/>'s current contents to <see cref="DebugConfig"/> for
    /// the currently open folder. A no-op when no folder is open (see
    /// <see cref="LoadBreakpointsForProject"/>).
    /// </summary>
    public void PersistBreakpoints()
    {
        if (!Project.IsOpen) return;

        string projectKey = DebugConfigStore.GetFolderProjectKey(Project.RootPath);
        var records = BreakpointStore.Breakpoints.Select(b => new DebugBreakpointRecord
        {
            FilePath = b.FilePath,
            LineNumber = b.LineNumber,
            Enabled = b.IsEnabled,
        });

        DebugConfig.SaveBreakpoints(projectKey, records);
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

    // Opens the READYCode online documentation in the user's default browser.
    private void OpenDocs()
    {
        Process.Start(new ProcessStartInfo(Settings.DocsUrl) { UseShellExecute = true });
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

        _printer.Print((Window)Application.Current.MainWindow, text, ActiveTab!.FileName, ActiveTab.Language);
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

        _printer.PrintPreview((Window)Application.Current.MainWindow, text, ActiveTab!.FileName, ActiveTab.Language);
    }

    // Gates Transfer/Run: both need an open tab with at least one character typed into it.
    private bool HasNonEmptyActiveTab() => !string.IsNullOrEmpty(ActiveTab?.Document.Text);

    // Gates Print/Print Preview, which only need an open tab regardless of its content.
    private bool HasActiveTab() => ActiveTab != null;

    // Transfers the current code to the C64 Ultimate.
    // Shows status messages and errors in the status bar.
    private async Task TransferCurrentProgramAsync()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("No code to transfer. Write some code first.", StatusType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("C64U URL not set. Go to Preferences > Settings to configure it.");
            return;
        }

        if (!TryBuildPrgData(text, out byte[]? prgData, out _))
            return;

        try
        {
            SetStatus("Transferring program to C64 Ultimate…");

            var client = new C64UltimateClient();
            await client.LoadPrgAsync(Settings.C64UUrl, prgData!);

            SetStatus("Program transferred to C64 Ultimate successfully.");
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    // Transfers the current code to the C64 Ultimate and starts execution.
    private async Task RunCurrentProgramAsync()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("There is no code to run. Please write some code first.", StatusType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("Please set the Commodore 64 Ultimate URL in Settings - Preferences first.", StatusType.Error);
            return;
        }

        if (!TryBuildPrgData(text, out byte[]? prgData, out AssemblyResult? asmResult))
            return;

        try
        {
            var client = new C64UltimateClient();

            if (NeedsSysCommand(asmResult))
            {
                // Standalone machine code (an explicit ".org", or "Standalone" output mode - no
                // auto-generated BASIC loader stub) - the C64U's run_prg endpoint just issues a
                // plain BASIC RUN after loading, which does nothing useful with no BASIC program
                // in memory. Load without running instead, then simulate typing "SYS <origin>"
                // once the machine has settled back at the READY prompt from the reset load_prg
                // itself triggers - the same trick real loader carts use to launch non-BASIC code
                // from a DMA load.
                SetStatus("Transferring program to C64 Ultimate…");
                await client.LoadPrgAsync(Settings.C64UUrl, prgData!);

                await Task.Delay(SysCommandDelay);
                await client.TypeAsync(Settings.C64UUrl, $"SYS{asmResult!.Origin}\r");

                SetStatus($"Program transferred and running on the C64 Ultimate (SYS{asmResult.Origin}).", StatusType.Info);
            }
            else
            {
                SetStatus("Transferring program to C64 Ultimate…");

                await client.RunPrgAsync(Settings.C64UUrl, prgData!);

                SetStatus("Program transferred and running on the C64 Ultimate.", StatusType.Info);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer/program execution failed: {ex.Message}", StatusType.Error);
        }
    }

    // Transfers the current code to VICE without running it. The user can type RUN
    // from within the emulator to start it.
    private async Task TransferToViceAsync()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("No code to transfer. Write some code first.", StatusType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.ViceEmulatorPath))
        {
            SetStatus("VICE emulator path not set. Go to Preferences > Settings to configure it.");
            return;
        }

        if (!TryBuildPrgData(text, out byte[]? prgData, out _))
            return;

        try
        {
            SetStatus("Transferring program to VICE…");

            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);
            await client.TransferAsync(Settings.ViceEmulatorPath, prgData!, ActiveTab!.FileName, Settings.ViceBringToForeground);

            SetStatus("Program transferred to VICE. Type RUN in the emulator to start it.");
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    // Transfers the current code to VICE and starts execution.
    private async Task RunOnViceAsync()
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("There is no code to run. Please write some code first.", StatusType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.ViceEmulatorPath))
        {
            SetStatus("Please set the VICE emulator path in Settings - Preferences first.", StatusType.Error);
            return;
        }

        if (!TryBuildPrgData(text, out byte[]? prgData, out AssemblyResult? asmResult))
            return;

        try
        {
            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);

            if (NeedsSysCommand(asmResult))
            {
                // Standalone machine code (an explicit ".org", or "Standalone" output mode - no
                // auto-generated BASIC loader stub) - VICE's autostart command just issues a
                // plain BASIC RUN after loading, which does nothing useful with no BASIC program
                // in memory. Load without running instead, then simulate typing "SYS <origin>"
                // once the machine has settled back at the READY prompt from the reset the
                // autostart load itself triggers - the same trick used for the C64 Ultimate.
                SetStatus("Transferring program to VICE…");
                await client.TransferAsync(Settings.ViceEmulatorPath, prgData!, ActiveTab!.FileName, Settings.ViceBringToForeground);

                await Task.Delay(SysCommandDelay);
                await client.TypeAsync($"SYS{asmResult!.Origin}\r");

                SetStatus($"Program transferred and running on VICE (SYS{asmResult.Origin}).", StatusType.Info);
            }
            else
            {
                SetStatus("Transferring program to VICE…");

                await client.RunAsync(Settings.ViceEmulatorPath, prgData!, ActiveTab!.FileName, Settings.ViceBringToForeground);

                SetStatus("Program transferred and running on VICE.", StatusType.Info);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Transfer/program execution failed: {ex.Message}", StatusType.Error);
        }
    }

    // Returns whether the assembled program has no BASIC loader stub in its .prg bytes - either
    // because of an explicit ".org" directive or "Standalone" output mode - and therefore needs
    // a typed "SYS <origin>" command instead of a plain autostart/run_prg RUN to actually start.
    private bool NeedsSysCommand(AssemblyResult? asmResult) =>
        asmResult != null && (asmResult.HasExplicitOrigin || Settings.AsmOutputMode == "Standalone");

    // Builds the .prg bytes to send to VICE or the C64U: assembles machine code for an Asm tab, or
    // tokenizes BASIC otherwise. On assembly failure, shows every error in a dialog and returns
    // false without ever constructing a client, keeping "assembly failed" distinct from "transfer
    // failed". Also hands back the AssemblyResult itself (null for a BASIC tab) so a caller that
    // cares - see RunCurrentProgramAsync - can check HasExplicitOrigin/Origin without assembling
    // the source a second time.
    private bool TryBuildPrgData(string text, out byte[]? prgData, out AssemblyResult? asmResult)
    {
        asmResult = null;

        if (ActiveTab!.Language == EditorLanguage.Asm)
        {
            asmResult = new Asm6502Assembler().Assemble(
                text, Settings.AsmOutputMode == "Standalone", (ushort)Settings.AsmDefaultOriginAddress);
            if (!asmResult.Success)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, asmResult.Errors.Select(e => $"Line {e.LineNumber}: {e.Message}")),
                    "Assembly Errors", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus($"Assembly failed with {asmResult.Errors.Count} error(s).", StatusType.Error);
                prgData = null;
                return false;
            }

            prgData = asmResult.PrgBytes;
            return true;
        }

        var converter = new PrgConverter();
        prgData = converter.ConvertToPrg(PrepareCodeForTransfer(text));
        return true;
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

    // Resumes a halted debug session - the Debug menu's "Continue", distinct from starting a new
    // session (see DebugStartOnViceAsync), which now only ever happens from the C64U/VICE menus'
    // own "Debug" items.
    private async Task DebugContinueAsync()
    {
        if (DebugSession == null) return;

        try
        {
            await DebugSession.ContinueAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Continue failed: {ex.Message}", StatusType.Error);
        }
    }

    // Starts a new BASIC debug session on VICE for the active tab. VICE's own RUN\r is typed
    // over the debug session's own connection (not a separate one-shot one) - a second
    // connection contending with the session's while the breakpoint checkpoint is active caused
    // this exact call to stall past its timeout in practice, and the resulting "failed to start"
    // cleanup deleted the checkpoint that was, by then, correctly holding the CPU stopped at the
    // very breakpoint it hit - so the falsely-reported failure was what let execution run right
    // past it.
    private Task DebugStartOnViceAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.ViceEmulatorPath))
        {
            SetStatus("Please set the VICE emulator path in Settings - Preferences first.", StatusType.Error);
            return Task.CompletedTask;
        }

        var transferClient = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);

        return StartDebugSessionAsync(
            "VICE",
            (tab, prgData) => transferClient.TransferAsync(Settings.ViceEmulatorPath, prgData, tab.FileName, Settings.ViceBringToForeground),
            async () => (IDebugSession)await ViceDebugSession.StartAsync(Settings.ViceMonitorHost, Settings.ViceMonitorPort),
            session => ((ViceDebugSession)session).TypeAsync("RUN\r"));
    }

    // Starts a new BASIC debug session on the C64 Ultimate for the active tab. Unlike VICE's
    // binary monitor connection, every C64U REST call is an independent, stateless HTTP request -
    // there's no persistent connection to contend with another one, so typing RUN\r through a
    // fresh C64UltimateClient (rather than routing it through the session) is safe here.
    private Task DebugStartOnC64UAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("Please set the C64 Ultimate URL in Settings - Preferences first.", StatusType.Error);
            return Task.CompletedTask;
        }

        var client = new C64UltimateClient();

        return StartDebugSessionAsync(
            "the C64 Ultimate",
            async (_, prgData) =>
            {
                await client.LoadPrgAsync(Settings.C64UUrl, prgData);
                // A load appears to trigger a machine reset, same as VICE's autostart - without
                // settling first, uploading the debug stub and patching the GONE vector
                // immediately afterward races that reset.
                await Task.Delay(SysCommandDelay);
            },
            async () => (IDebugSession)await C64UDebugSession.StartAsync(Settings.C64UUrl),
            _ => client.TypeAsync(Settings.C64UUrl, "RUN\r"));
    }

    // Shared session-start orchestration for both targets: validates there's a BASIC tab to
    // debug, builds the line address table and tokenized program from the exact, unmodified
    // editor text (unlike an ordinary Run, PrepareCodeForTransfer's minify pipeline is
    // deliberately NOT applied here, since minification - in particular line renumbering - would
    // desynchronize the source the user set breakpoints against from what's actually running),
    // transfers it, opens the debug session, arms every enabled breakpoint, and starts the
    // program running.
    private async Task StartDebugSessionAsync(
        string targetName,
        Func<EditorTab, byte[], Task> transferAsync,
        Func<Task<IDebugSession>> createSessionAsync,
        Func<IDebugSession, Task> typeRunAsync)
    {
        if (DebugSession != null) return; // already debugging - only Continue (Debug menu) applies now

        if (ActiveTab is not { Language: EditorLanguage.Basic } tab)
        {
            SetStatus("Start BASIC Debugging requires an active BASIC (.bas) tab.", StatusType.Error);
            return;
        }

        string text = tab.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("There is no code to debug. Please write some code first.", StatusType.Error);
            return;
        }

        IDebugSession? session = null;
        try
        {
            SetStatus("Building program…");

            var lineTable = BasicLineAddressTable.Build(text);
            byte[] prgData = new PrgConverter().ConvertToPrg(text);

            SetStatus($"Transferring program to {targetName}…");
            await transferAsync(tab, prgData);

            SetStatus("Opening debug connection…");
            session = await createSessionAsync();

            string breakpointFileKey = tab.FilePath ?? tab.FileName;
            var enabledLines = BreakpointStore.EnabledLinesFor(breakpointFileKey)
                .Where(line => lineTable.LineAddresses.ContainsKey(line))
                .ToList();

            if (enabledLines.Count > 0)
            {
                SetStatus($"Arming {enabledLines.Count} breakpoint(s)…");
                foreach (ushort line in enabledLines)
                    await session.SetLineBreakpointAsync(line);
            }

            session.Stopped += OnDebugSessionStopped;
            session.Resumed += OnDebugSessionResumed;
            session.ConnectionLost += OnDebugSessionConnectionLost;

            DebugTab = tab;
            DebugLineAddressTable = lineTable;
            DebugSession = session;
            IsDebugStopped = false;

            SetStatus("Starting program…");
            // The transfer above triggers a machine reset - typing into the keyboard buffer
            // before the KERNAL's cold-start sequence finishes and starts polling it again
            // silently drops the keystrokes rather than erroring, leaving the machine sitting at
            // READY (matches RunOnViceAsync's own SYS-command delay for the same reason).
            await Task.Delay(SysCommandDelay);
            await typeRunAsync(session);

            SetStatus($"Debugging on {targetName}. Running…", StatusType.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to start debugging: {ex.Message}", StatusType.Error);
            if (session != null)
                await session.DisposeAsync();
        }
    }

    // Arms a trap that halts at the start of the next BASIC line without resuming - valid only
    // while the session is already running (e.g. right after Continue/Start).
    private async Task DebugPauseAsync()
    {
        if (DebugSession == null) return;

        try
        {
            await DebugSession.PauseAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Pause failed: {ex.Message}", StatusType.Error);
        }
    }

    // Executes one BASIC line and halts again - valid only while already halted.
    private async Task DebugStepLineAsync()
    {
        if (DebugSession == null) return;

        try
        {
            await DebugSession.StepLineAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Step failed: {ex.Message}", StatusType.Error);
        }
    }

    // Runs until execution returns from the innermost GOSUB or FOR loop active at the current
    // stop point - valid only while already halted.
    private async Task DebugStepOutAsync()
    {
        if (DebugSession == null) return;

        try
        {
            await DebugSession.StepOutAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Step out failed: {ex.Message}", StatusType.Error);
        }
    }

    // Ends the active debug session - deletes every checkpoint it created and detaches, without
    // resetting or otherwise disturbing the running machine.
    private async Task DebugStopAsync()
    {
        if (DebugSession == null) return;

        try
        {
            await DebugSession.DisposeAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error stopping debug session: {ex.Message}", StatusType.Error);
        }
        finally
        {
            CleanupDebugSessionState();
            SetStatus("Debug session stopped.", StatusType.Info);
        }
    }

    // Unsubscribes from the session's events and clears every debug-related property, but does
    // not itself dispose the session - callers that already hold a reference (DebugStopAsync,
    // the ConnectionLost handler) are responsible for that.
    private void CleanupDebugSessionState()
    {
        if (DebugSession != null)
        {
            DebugSession.Stopped -= OnDebugSessionStopped;
            DebugSession.Resumed -= OnDebugSessionResumed;
            DebugSession.ConnectionLost -= OnDebugSessionConnectionLost;
        }

        DebugSession = null;
        DebugTab = null;
        DebugLineAddressTable = null;
        DebugCurrentDocumentLine = null;
        DebugVariables.Clear();
        DebugCallStack.Clear();
        IsDebugStopped = false;
    }

    // Raised from ViceDebugSession's background read loop - marshals onto the UI thread before
    // touching any bindable property.
    private void OnDebugSessionStopped(object? sender, DebugStoppedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsDebugStopped = true;
            DebugCurrentDocumentLine = DebugLineAddressTable != null
                && DebugLineAddressTable.BasicLineToDocumentLine.TryGetValue(e.Curlin, out int documentLine)
                    ? documentLine
                    : null;

            string breakpointNote = e.CheckpointNumber.HasValue ? " (breakpoint)" : "";
            SetStatus($"Stopped at line {e.Curlin}{breakpointNote}.", StatusType.Info);
        });

        _ = RefreshDebugVariablesAndCallStackAsync();
    }

    private void OnDebugSessionResumed(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsDebugStopped = false;
            DebugCurrentDocumentLine = null;
            DebugVariables.Clear();
            DebugCallStack.Clear();
            SetStatus("Running…", StatusType.Info);
        });
    }

    // Reads the variable table and 6502 stack from the machine and repopulates DebugVariables/
    // DebugCallStack - the I/O runs off the UI thread (it's several round trips over the debug
    // session's connection), with only the final collection updates marshaled back.
    /// <summary>
    /// Re-reads the variable table and call stack from the machine and refreshes
    /// <see cref="DebugVariables"/>/<see cref="DebugCallStack"/> - public so a caller that just
    /// wrote a new value to a variable (see <c>MainWindow.DebugVariablesGrid_CellEditEnding</c>)
    /// can refresh the display with the true resulting value read back from the machine, rather
    /// than trusting whatever was typed.
    /// </summary>
    public async Task RefreshDebugVariablesAndCallStackAsync()
    {
        if (DebugSession is not { } session) return;

        try
        {
            byte[] zeroPage = await session.ReadMemoryAsync(0x2D, 4); // VARTAB ($2D-$2E), ARYTAB ($2F-$30)
            ushort vartab = (ushort)(zeroPage[0] | (zeroPage[1] << 8));
            ushort arytab = (ushort)(zeroPage[2] | (zeroPage[3] << 8));

            var variables = new List<BasicVariable>();
            if (arytab > vartab)
            {
                byte[] tableBytes = await session.ReadMemoryAsync(vartab, arytab - vartab);
                variables.AddRange(VariableTableParser.ParseSimpleVariables(tableBytes, vartab, arytab));

                // String values point into either literal program text or the string heap, so
                // their characters aren't part of the table read above - resolve each one with
                // its own follow-up read. Same PETSCII-byte-value-is-the-display-char-value
                // convention PrgConverter.DetokenizeLine already uses for string literals.
                for (int i = 0; i < variables.Count; i++)
                {
                    if (variables[i] is not { Type: BasicVariableType.String, Value: StringDescriptor descriptor })
                        continue;
                    if (descriptor.Length == 0) continue;

                    byte[] chars = await session.ReadMemoryAsync(descriptor.HeapPointer, descriptor.Length);
                    string text = new(chars.Select(b => (char)b).ToArray());
                    variables[i] = variables[i] with { Value = new ResolvedStringValue(text, descriptor.HeapPointer) };
                }

                variables.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            }

            // The GOSUB call stack needs the 6502 stack pointer, which not every target can
            // read (the C64 Ultimate's REST API has no register access at all - see
            // IDebugSession.SupportsCallStackAndStepOut) - left empty rather than attempted there.
            IReadOnlyList<GosubFrame> callStack = Array.Empty<GosubFrame>();
            if (session.SupportsCallStackAndStepOut)
            {
                byte stackPointer = await session.ReadStackPointerAsync();
                byte[] stackPage = await session.ReadMemoryAsync(0x0100, 256);
                callStack = GosubStackParser.Parse(stackPage, stackPointer, DebugLineAddressTable);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                DebugVariables.Clear();
                foreach (var variable in variables)
                    DebugVariables.Add(variable);

                DebugCallStack.Clear();
                foreach (var frame in callStack)
                    DebugCallStack.Add(frame);
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
                SetStatus($"Failed to refresh variables/call stack: {ex.Message}", StatusType.Error));
        }
    }

    private void OnDebugSessionConnectionLost(object? sender, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetStatus($"Debug session lost: {message}", StatusType.Error);
            CleanupDebugSessionState();
        });
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
