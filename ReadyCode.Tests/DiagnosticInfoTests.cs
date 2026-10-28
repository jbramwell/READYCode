// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Logging;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="DiagnosticInfo"/>. Error log entries are written with the real
/// <see cref="ErrorLog.Write(string, Exception, string)"/>, so the report is tested against the
/// log's actual format.
/// </summary>
public class DiagnosticInfoTests
{
    #region Public Methods

    [Fact]
    public void Build_IncludesVersionRuntimeAndPaths()
    {
        string dir = CreateTempDir();
        try
        {
            string logPath = Path.Combine(dir, "error.log");

            string report = DiagnosticInfo.Build(dir, logPath);

            var v = typeof(DiagnosticInfo).Assembly.GetName().Version!;
            string[] lines = report.Split(Environment.NewLine);
            Assert.Equal($"READYCode (v{v.Major}.{v.Minor}.{v.Build}) Diagnostic Info", lines[0]);
            Assert.Equal("", lines[1]);
            Assert.StartsWith("Version:       2.", lines[2]);
            Assert.Contains(".NET:          .NET ", report);
            Assert.Contains("Architecture:  ", report);
            Assert.Contains("Windows:       ", report);
            Assert.Contains("Installed as:  not packaged", report); // the test host has no MSIX identity
            Assert.Contains($"Data folder:   {dir}", report);
            Assert.Contains($"Error log:     {logPath}", report);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Build_NoErrorLog_SaysNoErrorsLogged()
    {
        string dir = CreateTempDir();
        try
        {
            string report = DiagnosticInfo.Build(dir, Path.Combine(dir, "error.log"));

            Assert.Contains("No errors logged.", report);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Build_FewEntries_IncludesAllOfThem()
    {
        string dir = CreateTempDir();
        try
        {
            string logPath = Path.Combine(dir, "error.log");
            ErrorLog.Write(logPath, new InvalidOperationException("first failure"), "UI thread");
            ErrorLog.Write(logPath, new ArgumentException("second failure"), "Unobserved task");

            string report = DiagnosticInfo.Build(dir, logPath);

            Assert.Contains("Error log entries (2):", report);
            Assert.Contains("System.InvalidOperationException: first failure", report);
            Assert.Contains("System.ArgumentException: second failure", report);
            Assert.True(report.IndexOf("first failure") < report.IndexOf("second failure"), "entries should be oldest first");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Build_ManyEntries_IncludesOnlyTheMostRecentThree()
    {
        string dir = CreateTempDir();
        try
        {
            string logPath = Path.Combine(dir, "error.log");
            for (int i = 1; i <= 5; i++)
                ErrorLog.Write(logPath, new InvalidOperationException($"failure #{i}"), "UI thread");

            string report = DiagnosticInfo.Build(dir, logPath);

            Assert.Contains("Most recent 3 of 5 error log entries:", report);
            Assert.DoesNotContain("failure #1", report);
            Assert.DoesNotContain("failure #2", report);
            Assert.Contains("failure #3", report);
            Assert.Contains("failure #5", report);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Build_HugeEntry_IsTruncatedToKeepTheReportPasteable()
    {
        string dir = CreateTempDir();
        try
        {
            string logPath = Path.Combine(dir, "error.log");
            ErrorLog.Write(logPath, new InvalidOperationException(new string('x', 50_000)), "UI thread");

            string report = DiagnosticInfo.Build(dir, logPath);

            Assert.Contains("[... truncated]", report);
            Assert.True(report.Length < 10_000, $"report was {report.Length} characters");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Build_LogOpenForWriting_StillReadsIt()
    {
        // The app may be appending a new entry while the report is built.
        string dir = CreateTempDir();
        try
        {
            string logPath = Path.Combine(dir, "error.log");
            ErrorLog.Write(logPath, new InvalidOperationException("while open"), "UI thread");

            string report;
            using (new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                report = DiagnosticInfo.Build(dir, logPath);

            Assert.Contains("while open", report);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    #endregion

    #region Private Methods

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ReadyCodeTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    #endregion
}
