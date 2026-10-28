// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

namespace ReadyCode.UITests;

/// <summary>
/// One running instance of the app under test, driven through UI Automation. Each session gets
/// its own throwaway data folder (via the app's READYCODE_DATA_DIR override), so tests never read
/// or change the developer's real settings, and anything the app writes to error.log is visible
/// to the test through <see cref="ErrorLogText"/>. Disposing closes the app and deletes the folder.
/// </summary>
public sealed class AppSession : IDisposable
{
    #region Private Fields

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly Process _process;
    private readonly string _dataDirectory;

    #endregion

    #region Constructors

    private AppSession(Process process, string dataDirectory, AutomationElement window)
    {
        _process = process;
        _dataDirectory = dataDirectory;
        Window = window;
    }

    #endregion

    #region Public Properties

    /// <summary>Gets the app's main window.</summary>
    public AutomationElement Window { get; }

    /// <summary>Gets the contents of the session's error.log, or null if nothing was logged.</summary>
    public string? ErrorLogText
    {
        get
        {
            string path = Path.Combine(_dataDirectory, "error.log");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Launches the app with a fresh data folder, optionally pre-seeded with settings.
    /// </summary>
    /// <param name="settings">
    /// Settings to write to settings.json before launch (property name to value, as in
    /// AppSettings) - e.g. LastFolderPath to open a project folder. Anything not given keeps
    /// its default.
    /// </param>
    public static AppSession Launch(IReadOnlyDictionary<string, object?>? settings = null)
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), "ReadyCodeUITests_" + Guid.NewGuid());
        Directory.CreateDirectory(dataDirectory);
        if (settings != null)
            File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), JsonSerializer.Serialize(settings));

        var startInfo = new ProcessStartInfo(FindAppExecutable()) { UseShellExecute = false };
        startInfo.Environment["READYCODE_DATA_DIR"] = dataDirectory;
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The app failed to start.");

        try
        {
            WaitUntil(() => { process.Refresh(); return process.HasExited || process.MainWindowHandle != IntPtr.Zero; },
                TimeSpan.FromSeconds(30), "the app's main window to appear");
            if (process.HasExited)
                throw new InvalidOperationException($"The app exited during startup (exit code {process.ExitCode}).");
            process.WaitForInputIdle(10_000);
            return new AppSession(process, dataDirectory, AutomationElement.FromHandle(process.MainWindowHandle));
        }
        catch
        {
            if (!process.HasExited) process.Kill();
            TryDeleteDirectory(dataDirectory);
            throw;
        }
    }

    /// <summary>Finds a control by its x:Name (automation id), or null if it isn't present.</summary>
    public AutomationElement? Find(string automationId) =>
        Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    /// <summary>Finds a control by its x:Name, waiting for it to appear.</summary>
    public AutomationElement Get(string automationId)
    {
        AutomationElement? element = null;
        WaitUntil(() => (element = Find(automationId)) != null, DefaultTimeout, $"control '{automationId}' to appear");
        return element!;
    }

    /// <summary>Gets a text-displaying control's current text (e.g. a TextBlock).</summary>
    public string GetText(string automationId) => Get(automationId).Current.Name;

    /// <summary>Gets a value-bearing control's value (e.g. a text box's or the editor's text).</summary>
    public string GetValue(string automationId) =>
        ((ValuePattern)Get(automationId).GetCurrentPattern(ValuePattern.Pattern)).Current.Value;

    /// <summary>Gets a control's tooltip text.</summary>
    public string GetToolTip(string automationId) => Get(automationId).Current.HelpText;

    /// <summary>Gets whether a control is enabled.</summary>
    public bool IsEnabled(string automationId) => Get(automationId).Current.IsEnabled;

    /// <summary>Replaces a text box's content.</summary>
    public void SetText(string automationId, string text) =>
        ((ValuePattern)Get(automationId).GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);

    /// <summary>Sets a toggle button / check box to the given state.</summary>
    public void SetToggle(string automationId, bool on)
    {
        var toggle = (TogglePattern)Get(automationId).GetCurrentPattern(TogglePattern.Pattern);
        var target = on ? ToggleState.On : ToggleState.Off;
        for (int i = 0; i < 3 && toggle.Current.ToggleState != target; i++)
            toggle.Toggle();
    }

    /// <summary>
    /// Sends keystrokes (System.Windows.Forms.SendKeys syntax, e.g. "^n" for Ctrl+N) to the app.
    /// Throws, without sending anything, if the app can't be brought to the foreground - so a
    /// stolen focus never sends keystrokes into some other window.
    /// </summary>
    public void SendKeys(string keys)
    {
        EnsureForeground();
        System.Windows.Forms.SendKeys.SendWait(keys);
    }

    /// <summary>
    /// Opens a new editor tab (Ctrl+N) and types <paramref name="text"/> into it, waiting until
    /// it appears. Compared case-insensitively: the case typed letters come out in depends on
    /// the editor's C64 case mode and Caps Lock, neither of which a test should rely on.
    /// </summary>
    public void NewTabWithText(string text)
    {
        SendKeys("^n");
        Get("Editor");
        SendKeys(text);
        WaitUntil(() => GetValue("Editor").Contains(text, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(5), $"\"{text}\" to appear in the editor");
    }

    /// <summary>Focuses a control and presses Enter in it.</summary>
    public void PressEnterIn(string automationId)
    {
        Get(automationId).SetFocus();
        SendKeys("{ENTER}");
    }

    /// <summary>Waits until <paramref name="condition"/> holds, failing with a message if it times out.</summary>
    public static void WaitUntil(Func<bool> condition, TimeSpan timeout, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
                throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0}s waiting for {description}.");
            Thread.Sleep(50);
        }
    }

    /// <summary>Waits until a control's text satisfies <paramref name="condition"/>, then returns it.</summary>
    public string WaitForText(string automationId, Func<string, bool> condition, string description, TimeSpan? timeout = null)
    {
        string text = "";
        WaitUntil(() => condition(text = GetText(automationId)), timeout ?? DefaultTimeout,
            $"{description} (last text of '{automationId}': \"{text}\")");
        return text;
    }

    /// <summary>Closes the app and deletes the session's data folder.</summary>
    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit(10_000);
        }
        _process.Dispose();
        TryDeleteDirectory(_dataDirectory);
    }

    #endregion

    #region Private Methods

    // The Debug build of the app, located from the repo root (found by walking up to
    // ReadyCode.sln) - or READYCODE_EXE, to test some other build.
    private static string FindAppExecutable()
    {
        if (Environment.GetEnvironmentVariable("READYCODE_EXE") is { Length: > 0 } exe)
            return exe;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ReadyCode.sln")))
            {
                string path = Path.Combine(dir.FullName, "ReadyCode", "bin", "Debug", "net10.0-windows", "ReadyCode.exe");
                return File.Exists(path) ? path : throw new FileNotFoundException($"Build the app first (Debug): {path}");
            }
        }
        throw new DirectoryNotFoundException("Couldn't find the repo root (ReadyCode.sln) above " + AppContext.BaseDirectory);
    }

    private void EnsureForeground()
    {
        IntPtr hwnd = _process.MainWindowHandle;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            SetForegroundWindow(hwnd);
            Thread.Sleep(100);
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundPid);
            if (foregroundPid == (uint)_process.Id)
                return;
        }
        throw new InvalidOperationException(
            "Couldn't bring the app to the foreground, so no keystrokes were sent. Don't use the keyboard or mouse while UI tests run.");
    }

    private static void TryDeleteDirectory(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); return; }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    #endregion
}
