// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ReadyCode.Interop;
using ReadyCode.Settings;

namespace ReadyCode.Logging;

/// <summary>
/// Builds the plain-text report behind Help > Copy Diagnostic Info, for pasting into a bug
/// report: the app version and build, .NET runtime, CPU architecture, Windows version, how the
/// app is installed, where its data lives, and the most recent <see cref="ErrorLog"/> entries.
/// </summary>
public static class DiagnosticInfo
{
    #region Private Fields

    // Enough recent errors to show a pattern, capped so the report stays pasteable.
    private const int MaxErrorLogEntries = 3;
    private const int MaxErrorLogChars = 8000;

    // Each ErrorLog entry starts with a "[yyyy-MM-dd HH:mm:ss] source" line.
    private static readonly Regex _entryStartPattern =
        new(@"^(?=\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] )", RegexOptions.Multiline | RegexOptions.Compiled);

    #endregion

    #region Public Methods

    /// <summary>Builds the report for the running app.</summary>
    public static string Build() => Build(AppSettings.DataDirectory, ErrorLog.LogFilePath);

    /// <summary>
    /// Builds the report, reading recent errors from <paramref name="errorLogPath"/>.
    /// </summary>
    /// <param name="dataDirectory">The app's data folder, shown in the report.</param>
    /// <param name="errorLogPath">The error log to include recent entries from.</param>
    public static string Build(string dataDirectory, string errorLogPath)
    {
        var assembly = typeof(DiagnosticInfo).Assembly;
        var assemblyVersion = assembly.GetName().Version;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assemblyVersion?.ToString()
            ?? "unknown";

        // Same Major.Minor.Build form as the About box; the Version line below adds the commit.
        string title = assemblyVersion != null
            ? $"READYCode (v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}) Diagnostic Info"
            : "READYCode Diagnostic Info";

        var report = new StringBuilder()
            .AppendLine(title)
            .AppendLine()
            .AppendLine($"Version:       {version}")
            .AppendLine($".NET:          {RuntimeInformation.FrameworkDescription}")
            .AppendLine($"Architecture:  {RuntimeInformation.ProcessArchitecture} (OS: {RuntimeInformation.OSArchitecture})")
            .AppendLine($"Windows:       {RuntimeInformation.OSDescription}")
            .AppendLine($"Installed as:  {(PackageIdentity.IsPackaged ? "MSIX package (e.g. Microsoft Store)" : "not packaged (MSI installer or development build)")}")
            .AppendLine($"Data folder:   {dataDirectory}")
            .AppendLine($"Error log:     {errorLogPath}");

        AppendRecentErrors(report, errorLogPath);
        return report.ToString();
    }

    #endregion

    #region Private Methods

    private static void AppendRecentErrors(StringBuilder report, string errorLogPath)
    {
        string log;
        try
        {
            if (!File.Exists(errorLogPath))
            {
                report.AppendLine().AppendLine("No errors logged.");
                return;
            }

            // Shared access, in case the app is writing a new entry right now.
            using var stream = new FileStream(errorLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            log = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.AppendLine().AppendLine($"Couldn't read the error log: {ex.Message}");
            return;
        }

        var entries = SplitEntries(log);
        if (entries.Count == 0)
        {
            report.AppendLine().AppendLine("No errors logged.");
            return;
        }

        // Newest entries first into the budget, then shown oldest-to-newest.
        var shown = new List<string>();
        int budget = MaxErrorLogChars;
        for (int i = entries.Count - 1; i >= 0 && shown.Count < MaxErrorLogEntries; i--)
        {
            string entry = entries[i];
            if (entry.Length > budget)
            {
                if (shown.Count == 0)
                    shown.Add(entry[..budget] + Environment.NewLine + "[... truncated]");
                break;
            }
            shown.Add(entry);
            budget -= entry.Length;
        }
        shown.Reverse();

        report.AppendLine()
            .AppendLine(entries.Count == shown.Count
                ? $"Error log entries ({entries.Count}):"
                : $"Most recent {shown.Count} of {entries.Count} error log entries:")
            .AppendLine();
        foreach (string entry in shown)
            report.AppendLine(entry).AppendLine();
    }

    private static List<string> SplitEntries(string log) =>
        _entryStartPattern.Split(log)
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .ToList();

    #endregion
}
