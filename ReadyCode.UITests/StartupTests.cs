// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace ReadyCode.UITests;

/// <summary>
/// Smoke tests for app startup - catches anything that breaks MainWindow's construction or
/// wiring (e.g. after moving code between its partial-class files).
/// </summary>
public class StartupTests
{
    #region Public Methods

    [UIFact]
    public void App_StartsWithMainWindow_AndLogsNoErrors()
    {
        using var app = AppSession.Launch();

        // (The editor itself stays hidden until a tab is open, so check the side bar instead.)
        Assert.StartsWith("READYCode", app.Window.Current.Name);
        Assert.NotNull(app.Find("ExplorerToggle"));

        // Give anything deferred to after startup (dispatcher work, timers) a moment to fail.
        Thread.Sleep(2000);
        Assert.Null(app.ErrorLogText);
    }

    [UIFact]
    public void NewTab_AcceptsTyping()
    {
        using var app = AppSession.Launch();

        app.NewTabWithText("REM HELLO");
        Assert.Null(app.ErrorLogText);
    }

    #endregion
}
