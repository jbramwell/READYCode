// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace ReadyCode.UITests;

/// <summary>
/// Smoke tests for the Help menu.
/// </summary>
public class HelpMenuTests
{
    #region Public Methods

    [UIFact]
    public void CopyDiagnosticInfo_PutsReportWithRecentErrorsOnClipboard()
    {
        // An error from a "previous run", which the report should include.
        string seededLog =
            "[2026-01-02 03:04:05] UI thread" + Environment.NewLine +
            "READYCode 2.5.0.0 | .NET 10.0.0 | Arm64 | Microsoft Windows 10.0.26100" + Environment.NewLine +
            "System.InvalidOperationException: seeded failure from an earlier run" + Environment.NewLine +
            Environment.NewLine;

        // This test overwrites the clipboard - put back whatever text the developer had on it.
        string previousClipboard = AppSession.GetClipboardText();
        try
        {
            using var app = AppSession.Launch(dataFiles: new Dictionary<string, string> { ["error.log"] = seededLog });
            AppSession.SetClipboardText("");

            app.SendKeys("%h");   // Help menu
            app.SendKeys("c");    // Copy Diagnostic Info

            string report = "";
            AppSession.WaitUntil(() => (report = AppSession.GetClipboardText()).Length > 0,
                TimeSpan.FromSeconds(10), "the diagnostic report to reach the clipboard");

            Assert.Matches(@"^READYCode \(v\d+\.\d+\.\d+\) Diagnostic Info\r?\n\r?\n", report);
            Assert.Contains("Version:       2.", report);
            Assert.Contains($"Data folder:   {app.DataDirectory}", report);
            Assert.Contains("Error log entries (1):", report);
            Assert.Contains("seeded failure from an earlier run", report);

            // The app itself logged nothing new while doing this.
            Assert.Equal(seededLog, app.ErrorLogText);
        }
        finally
        {
            AppSession.SetClipboardText(previousClipboard);
        }
    }

    #endregion
}
