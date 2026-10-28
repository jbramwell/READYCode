// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ReadyCode.Settings;

namespace ReadyCode.Logging;

/// <summary>
/// Appends unexpected exceptions to a plain-text log in the app's data folder, so an error the
/// global handlers in <see cref="App"/> recover from (or can't recover from) leaves something
/// behind to diagnose. Writing never throws - a logging failure must not turn a recoverable
/// error into a crash.
/// </summary>
public static class ErrorLog
{
    #region Private Fields

    // Past this size the current log is rolled over to error.log.old (replacing any previous
    // one), so a repeating error can't grow the file without bound.
    private const long MaxLogBytes = 1024 * 1024;

    private static readonly object _writeLock = new();

    #endregion

    #region Public Properties

    /// <summary>Gets the full path of the error log file.</summary>
    public static string LogFilePath => Path.Combine(AppSettings.DataDirectory, "error.log");

    #endregion

    #region Public Methods

    /// <summary>
    /// Appends <paramref name="exception"/> to the error log at <see cref="LogFilePath"/>.
    /// </summary>
    /// <param name="exception">The exception to record.</param>
    /// <param name="source">Which handler caught it (e.g. "UI thread").</param>
    public static void Write(Exception exception, string source) => Write(LogFilePath, exception, source);

    /// <summary>
    /// Appends <paramref name="exception"/> to the error log at <paramref name="logFilePath"/>,
    /// rolling the file over first if it has grown past its size cap.
    /// </summary>
    /// <param name="logFilePath">The log file to append to.</param>
    /// <param name="exception">The exception to record.</param>
    /// <param name="source">Which handler caught it (e.g. "UI thread").</param>
    public static void Write(string logFilePath, Exception exception, string source)
    {
        try
        {
            lock (_writeLock)
            {
                string? directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var info = new FileInfo(logFilePath);
                if (info.Exists && info.Length > MaxLogBytes)
                    File.Move(logFilePath, logFilePath + ".old", overwrite: true);

                var entry = new StringBuilder()
                    .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}")
                    .AppendLine($"READYCode {typeof(ErrorLog).Assembly.GetName().Version} | {RuntimeInformation.FrameworkDescription} | {RuntimeInformation.ProcessArchitecture} | {RuntimeInformation.OSDescription}")
                    .AppendLine(exception.ToString())
                    .AppendLine();

                File.AppendAllText(logFilePath, entry.ToString());
            }
        }
        catch
        {
            // Nowhere left to report a failure to log - swallow it rather than crash.
        }
    }

    #endregion
}
