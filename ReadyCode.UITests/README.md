# UI smoke tests

End-to-end checks that launch the real READYCode app and drive it through Windows UI Automation -
for catching UI regressions the unit tests in `ReadyCode.Tests` can't see. Run them on your own
machine before committing UI-related changes.

## Running

```powershell
.\ReadyCode.UITests\run-ui-tests.ps1                         # all of them (~45 seconds)
.\ReadyCode.UITests\run-ui-tests.ps1 -Filter FindBarTests    # one class
```

The script builds the app (Debug) and runs the tests. While they run, **don't use the keyboard or
mouse** - the tests send real keystrokes to the app. If the app loses focus, the test fails rather
than typing into another window.

Each test runs the app against its own throwaway settings folder (through the app's
`READYCODE_DATA_DIR` environment variable), so your own settings, recent files, and error log are
never touched. Every test also checks that the app wrote nothing to that folder's `error.log`.

They're skipped everywhere else: a plain `dotnet test`, Visual Studio's Run All, and CI all show
them as skipped (CI still compiles them). To run them from Test Explorer, set the environment
variable `READYCODE_UI_TESTS=1` before starting Visual Studio.

## Adding a test

Mark it `[UIFact]` (not `[Fact]`), launch the app with `AppSession`, and find controls by their
`x:Name`:

```csharp
[UIFact]
public void Something_DoesWhatItShould()
{
    using var app = AppSession.Launch();      // or Launch(settings) to pre-seed settings.json

    app.NewTabWithText("10 PRINT");           // Ctrl+N, type, wait for it to appear
    app.SendKeys("^f");                       // System.Windows.Forms.SendKeys syntax
    app.SetText("SearchBox", "PRINT");        // text boxes
    app.SetToggle("RegexBtn", true);          // toggle buttons / check boxes

    app.WaitForText("MatchCountText", t => t == "1 of 1", "the match count");
    Assert.Null(app.ErrorLogText);            // nothing logged by the app
}
```

Things worth knowing:

- **Open panels the way a user would** (keyboard shortcuts, `SendKeys`). Toggling a side-bar
  button through UI Automation changes its checked state without raising its `Click` handler.
- **Don't depend on the case of typed letters.** It depends on the editor's C64 case mode and
  Caps Lock; compare case-insensitively (as `NewTabWithText` does).
- **Controls only exist once they're visible.** The editor, for example, isn't in the UI
  Automation tree until a tab is open; `Get` waits for a control to appear.
- To test a build other than the Debug one, set `READYCODE_EXE` to its path.
