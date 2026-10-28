// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using Xunit;

namespace ReadyCode.UITests;

/// <summary>
/// Smoke tests for the project-wide Search panel: it searches in the background (the window stays
/// responsive and Replace All is disabled until it finishes), a newer search replaces one still
/// running, and an invalid regex is reported.
/// </summary>
public class ProjectSearchTests : IClassFixture<ProjectSearchTests.SearchProjectFolder>
{
    #region Private Fields

    // Matches every .bas file's "20 GOTO 10", and backtracks catastrophically (timing out) on
    // each slow*.txt file's run of X's with no Y.
    private const string SlowQuery = "(X+X+)+Y|GOTO";

    private const string Searching = "Searching…";

    private readonly SearchProjectFolder _project;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchTests"/> class.
    /// </summary>
    public ProjectSearchTests(SearchProjectFolder project) => _project = project;

    #endregion

    #region Public Methods

    [UIFact]
    public void SlowSearch_RunsInBackground_AndDisablesReplaceAllUntilDone()
    {
        using var app = OpenSearchPanel();
        app.SetToggle("SearchRegexBtn", true);
        app.SetText("SearchQueryBox", SlowQuery);

        app.PressEnterIn("SearchQueryBox");

        // Reading the status at all mid-search proves the UI thread isn't blocked by the scan.
        Assert.Equal(Searching, app.GetText("SearchStatusText"));
        Assert.False(app.IsEnabled("SearchReplaceAllBtn"));

        string summary = app.WaitForText("SearchStatusText", t => t != Searching, "the search to finish", TimeSpan.FromSeconds(60));
        Assert.Equal(
            $"{SearchProjectFolder.BasFileCount} results in {SearchProjectFolder.BasFileCount} files. " +
            $"{SearchProjectFolder.SlowFileCount} files were skipped because the regular expression took too long to evaluate.",
            summary);
        Assert.True(app.IsEnabled("SearchReplaceAllBtn"));
        Assert.Null(app.ErrorLogText);
    }

    [UIFact]
    public void NewSearch_ReplacesOneStillRunning()
    {
        using var app = OpenSearchPanel();
        app.SetToggle("SearchRegexBtn", true);
        app.SetText("SearchQueryBox", SlowQuery);
        app.PressEnterIn("SearchQueryBox");
        Assert.Equal(Searching, app.GetText("SearchStatusText"));

        app.SetToggle("SearchRegexBtn", false);
        app.SetText("SearchQueryBox", "PRINT \"7\"");
        app.PressEnterIn("SearchQueryBox");

        app.WaitForText("SearchStatusText", t => t != Searching, "the new search to finish", TimeSpan.FromSeconds(60));
        // Let the replaced slow search run out too - its results must never appear.
        Thread.Sleep(3000);
        Assert.Equal("1 result in 1 file.", app.GetText("SearchStatusText"));
        Assert.True(app.IsEnabled("SearchReplaceAllBtn"));
        Assert.Null(app.ErrorLogText);
    }

    [UIFact]
    public void InvalidRegex_ShowsMessage()
    {
        using var app = OpenSearchPanel();
        app.SetToggle("SearchRegexBtn", true);
        app.SetText("SearchQueryBox", "[unclosed");

        app.PressEnterIn("SearchQueryBox");

        string status = app.WaitForText("SearchStatusText", t => t.Length > 0, "the search status");
        Assert.StartsWith("Invalid regular expression:", status);
        Assert.Null(app.ErrorLogText);
    }

    #endregion

    #region Private Methods

    // Launches the app with the test project open, and opens the Search panel the way a user
    // would (Ctrl+Shift+F, Find in Files) with its Replace row expanded, so Replace All is
    // visible to UI Automation.
    private AppSession OpenSearchPanel()
    {
        var app = AppSession.Launch(new Dictionary<string, object?> { ["LastFolderPath"] = _project.Path });
        try
        {
            app.SendKeys("^+f");
            app.Get("SearchQueryBox");
            app.SetToggle("SearchReplaceExpandBtn", true);
            app.Get("SearchReplaceAllBtn");
            return app;
        }
        catch
        {
            app.Dispose();
            throw;
        }
    }

    #endregion

    #region Public Classes

    /// <summary>
    /// A throwaway project folder shared by the tests in <see cref="ProjectSearchTests"/>:
    /// <see cref="BasFileCount"/> small BASIC files, plus <see cref="SlowFileCount"/> files that
    /// make <see cref="SlowQuery"/>'s regex time out - so a search takes long enough to observe
    /// while it runs.
    /// </summary>
    public sealed class SearchProjectFolder : IDisposable
    {
        /// <summary>The number of .bas files (each containing "20 GOTO 10" and a unique PRINT).</summary>
        public const int BasFileCount = 300;

        /// <summary>The number of files the slow regex times out on.</summary>
        public const int SlowFileCount = 6;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchProjectFolder"/> class.
        /// </summary>
        public SearchProjectFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReadyCodeUITests_Project_" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
            for (int i = 0; i < BasFileCount; i++)
                File.WriteAllText(System.IO.Path.Combine(Path, $"p{i:0000}.bas"), $"10 PRINT \"{i}\"\n20 GOTO 10\n");
            for (int i = 0; i < SlowFileCount; i++)
                File.WriteAllText(System.IO.Path.Combine(Path, $"slow{i}.txt"), new string('X', 40));
        }

        /// <summary>Gets the folder's full path.</summary>
        public string Path { get; }

        /// <summary>Deletes the folder.</summary>
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    #endregion
}
