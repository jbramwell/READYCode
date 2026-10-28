// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace ReadyCode.UITests;

/// <summary>
/// Smoke tests for the single-tab Find bar, including how it reports a regular expression it
/// couldn't run (rather than showing a misleading "No results"). Patterns are matched
/// case-insensitively (the Find bar's default), since typed letters' case depends on the
/// editor's C64 case mode and Caps Lock.
/// </summary>
public class FindBarTests
{
    #region Public Methods

    [UIFact]
    public void InvalidRegex_ShowsInvalidRegexWithDetailsInTooltip()
    {
        using var app = OpenFindBarOnText(new string('X', 40));

        app.SetText("SearchBox", "[unclosed");

        app.WaitForText("MatchCountText", t => t == "Invalid regex", "the Find bar to report an invalid regex");
        Assert.Contains("[unclosed", app.GetToolTip("MatchCountText"));
        Assert.Null(app.ErrorLogText);
    }

    [UIFact]
    public void CatastrophicRegex_ShowsRegexTooSlowInsteadOfFreezing()
    {
        // Backtracks catastrophically on a long run of X's with no Y - the search must give up
        // after its timeout and say so, with the window still responding.
        using var app = OpenFindBarOnText(new string('X', 40));

        app.SetText("SearchBox", "(X+X+)+Y");

        app.WaitForText("MatchCountText", t => t == "Regex too slow", "the Find bar to report a too-slow regex");
        Assert.Null(app.ErrorLogText);
    }

    [UIFact]
    public void ValidRegex_ShowsMatchCount()
    {
        using var app = OpenFindBarOnText(new string('X', 40));

        app.SetText("SearchBox", "X{40}");

        app.WaitForText("MatchCountText", t => t == "1 of 1", "the Find bar to show the match count");
        Assert.Null(app.ErrorLogText);
    }

    #endregion

    #region Private Methods

    // Opens a new tab containing text, then the Find bar in regex mode.
    private static AppSession OpenFindBarOnText(string text)
    {
        var app = AppSession.Launch();
        try
        {
            app.NewTabWithText(text);
            app.SendKeys("^f");
            app.SetToggle("RegexBtn", true);
            return app;
        }
        catch
        {
            app.Dispose();
            throw;
        }
    }

    #endregion
}
