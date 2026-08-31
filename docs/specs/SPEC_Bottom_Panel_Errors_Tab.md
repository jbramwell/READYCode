# DRAFT: READYCode Bottom Panel Tab Strip + Errors Tab — Feature Specification

**Status:** Draft
**Target:** READYCode (next release)
**Scope:** Convert the existing bottom Debug Panel (Variables/Breakpoints/Call Stack) into one tab of a new, extensible bottom-panel host; add a Visual Studio–style Errors tab alongside it; add a `View > E_rrors Panel` menu toggle with persisted state; close the silent line-drop gaps in the BASIC tokenizer so every issue that would drop or misbehave also produces a squiggle and an Errors-tab row.

---

## 0. How to read this spec

This document is grounded in the actual `D:\ReadyCode` source (inspected directly — file/line references below are real, not assumed). Section 10 ("Decisions made without a further round of questions") lists every judgment call made while writing this that the four clarifying answers didn't already cover. Read that section first — it's the part most likely to need your input before implementation starts.

---

## 1. Overview / Motivation

Today the bottom panel in `MainWindow.xaml` is single-purpose: a `Border` named `DebugPanel` holding a small hand-rolled tab strip (`ToggleButton`s styled as tabs, not a real `TabControl`) that switches between three views — `VARIABLES`, `BREAKPOINTS`, `CALL STACK`. It opens/closes via `ViewModel.IsDebugPanelOpen`, and there's no equivalent surface for the diagnostics that already exist and already draw red squiggles in the editor (`ReadyCode.Diagnostics.BasicDiagnostics` / `AsmDiagnostics`) — a user currently has no way to see "everything wrong with my program" in one list; they have to scroll through the file hunting for squiggles.

This spec does two things:

1. **Restructures the panel** so "Debug" (today's Variables/Breakpoints/Call Stack trio, unchanged internally) becomes one tab in an outer strip, and adds a second "Errors" tab that lists every current diagnostic, VS2026-Error-List-style, with double-click-to-jump.
2. **Closes a real gap** found while investigating: `PrgConverter.ConvertToPrg` (the BASIC → `.prg` tokenizer used on every save/deploy) already detects two classes of malformed line and silently drops them today with zero user-facing feedback — not even a squiggle. Per your answer to the data-source question, this spec extends `BasicDiagnostics` so those same failures become squiggles too, and therefore automatically show up in the new Errors tab.

## 2. Goals

- The bottom panel becomes a two-tab host: **DEBUG** and **ERRORS**, styled like the reference screenshot (Visual Studio 2026's Error List), sharing the same open/close and resize behavior the Debug panel already has.
- The Errors tab lists every diagnostic READYCode currently turns into a squiggly underline, across every open tab (not just the active one) — see §4.3 and §10.1 for the scope decision.
- Double-clicking a row jumps the editor to that file/line, activating that tab if needed.
- `View > E_rrors Panel` toggles the panel open/closed with the Errors tab active, mirrors the existing `View > _Debug Panel` item exactly, and its state persists across restarts.
- Every line `PrgConverter` currently drops silently during tokenization also becomes a real diagnostic (squiggle + Errors-tab row), per your answer to the data-source question.

## 3. Non-Goals (v1)

- No error *codes* (VS's "Code" column, e.g. `CS1002`) — nothing in the current diagnostics model carries one, and inventing a coding scheme is a separate piece of work. The column is simply omitted (see §6.1).
- No "Build + IntelliSense" style output-filter dropdown — that control is specific to MSBuild's output types and has no READYCode analog.
- No scan of files that aren't currently open in a tab (i.e., not a true "Entire Project" scan of the whole folder on disk) — see §10.1 for the reasoning and the easy path to add it later.
- No new keyboard shortcut for the Errors Panel toggle — the existing Debug Panel item has none either (see `MainWindow.xaml` line 1261-1263), so none is invented here to avoid an accidental conflict. Easy to add later if wanted.

## 4. Current Architecture (as found)

This section documents the exact mechanics being extended, since there's no real `TabControl` anywhere in this panel — it's fully hand-rolled.

### 4.1 The Debug Panel today

`Views/MainWindow.xaml`, inside the `Grid.Column="3"` column that also holds the tab strip/editor (so the panel is exactly as wide as the editor, not the whole window):

```xml
<Border x:Name="DebugPanel" Grid.Row="3" ...>
    <DockPanel>
        <Border DockPanel.Dock="Top" ...>            <!-- tab strip header -->
            <StackPanel Orientation="Horizontal">
                <ToggleButton x:Name="DebugVariablesTabToggle" Content="VARIABLES" IsChecked="True"
                              Style="{StaticResource DebugPanelTabToggle}" Click="DebugPanelTab_Click" Tag="Variables"/>
                <ToggleButton x:Name="DebugBreakpointsTabToggle" Content="BREAKPOINTS" .../>
                <ToggleButton x:Name="DebugCallStackTabToggle" Content="CALL STACK" .../>
            </StackPanel>
            <Button Content="&#xE711;" Click="CloseDebugPanel_Click" ToolTip="Close Debug Panel"/>
        </Border>
        <Grid>                                        <!-- content: 3 controls overlaid, Visibility toggled -->
            <DataGrid x:Name="DebugVariablesGrid" .../>
            <DataGrid x:Name="DebugBreakpointsGrid" Visibility="Collapsed" .../>
            <ListView x:Name="DebugCallStackList" Visibility="Collapsed" .../>
        </Grid>
    </DockPanel>
</Border>
```

Code-behind (`Views/MainWindow.xaml.cs`):

- `DebugPanelTabs` — a small array pairing each `ToggleButton` with its content control and a string key (`"Variables"`, `"Breakpoints"`, `"CallStack"`).
- `ActivateDebugPanelTab(key)` — checks the matching toggle, collapses the others, and persists the key to `ViewModel.Settings.ActiveDebugPanelTab`.
- `DebugPanelTab_Click` — reads `Tag` off the clicked `ToggleButton`, calls `ActivateDebugPanelTab`.
- `CloseDebugPanel_Click` — `ViewModel.IsDebugPanelOpen = false`.
- `ApplyDebugPanelOpenState()` — sets `DebugPanelRow`/`DebugPanelSplitterRow` (two `RowDefinition`s in the outer grid) to `ViewModel.Settings.DebugPanelHeight`/`4` when open, `0`/`0` when closed — remembering the last height before collapsing, exactly like the left/right side panels.
- On startup: `ActivateDebugPanelTab(ViewModel.Settings.ActiveDebugPanelTab); if (ViewModel.IsDebugPanelOpen) ApplyDebugPanelOpenState();`

Settings (`Settings/AppSettings.cs`):

```csharp
public bool IsDebugPanelOpen { get; set; } = false;
public double DebugPanelHeight { get; set; } = 200;
public string ActiveDebugPanelTab { get; set; } = "Variables";
```

`MainViewModel.IsDebugPanelOpen` is a plain get/set property (no `ICommand`) that writes straight through to `Settings.IsDebugPanelOpen` and calls `Settings.Save()` — and the View menu item binds `IsChecked` straight to it, `Mode=TwoWay`, no `Command`:

```xml
<MenuItem Header="_Debug Panel" IsCheckable="True" IsChecked="{Binding ViewModel.IsDebugPanelOpen, Mode=TwoWay}"/>
```

### 4.2 Diagnostics today

`Diagnostics/BasicDiagnostics.cs` defines the shared model:

```csharp
public readonly record struct EditorDiagnostic(int Offset, int Length, string Message);
```

No severity field — everything is implicitly "error" (see §10.2). `BasicDiagnostics.Analyze(source)` currently flags: undefined GOTO/GOSUB/THEN targets, unmatched FOR/NEXT, unterminated string literals, duplicate line numbers. `AsmDiagnostics.Analyze(source, result)` runs the source through `Asm6502Assembler` and turns every `AssemblyError` into one line-spanning diagnostic.

Both feed `ErrorSquiggleRenderer` (draws the red wave under editor text) via `MainWindow.xaml.cs`'s `RunDiagnostics()`:

```csharp
private void RunDiagnostics(AssemblyResult? asmResult = null)
{
    bool isAsm = ViewModel.ActiveTab?.Language == EditorLanguage.Asm;
    _currentDiagnostics = Editor.Document != null && ViewModel.Settings.EnableLinting && !Editor.IsReadOnly
        ? (isAsm ? AsmDiagnostics.Analyze(Editor.Document.Text, asmResult) : BasicDiagnostics.Analyze(Editor.Document.Text))
        : Array.Empty<EditorDiagnostic>();
    _errorSquiggleRenderer.SetDiagnostics(_currentDiagnostics);
    Editor.TextArea.TextView.Redraw();
}
```

**Critically, `_currentDiagnostics` is a single field scoped to the *active* tab only.** It's recomputed by a debounce timer as the user types and directly by `ActivateTab` right after switching tabs (via `RunDocumentAnalysis()`, which also drives folding and variable/symbol indexing off the same pass). Diagnostics for a tab you're not looking at simply don't exist anywhere right now — see §5.1, this is the main structural change needed for a cross-tab Errors list.

### 4.3 The silent tokenizer gap (the reason Q1's answer matters)

`Tokenizer/PrgConverter.cs`, `ConvertToPrg`, per source line:

```csharp
var parts = ParseLineNumberAndCode(trimmedLine);
if (parts == null)
{
    debugLines.Add($"  ERROR: Failed to parse line number");
    continue;                                    // <-- line silently dropped from the .prg
}
...
var tokenResult = tokenizer.TokenizeLine(code);
if (!tokenResult.Success)
{
    debugLines.Add($"  ERROR: Tokenization failed - {tokenResult.ErrorMessage}");
    continue;                                    // <-- line silently dropped from the .prg
}
```

`debugLines` only ever feeds `LastDebugInfo`, a debug-only string nothing in the UI displays today. So right now: **a line READYCode can't parse or tokenize just vanishes from the saved/deployed `.prg`, with no error, no squiggle, no dialog.** Two concrete ways this happens:

- `ParseLineNumberAndCode` (in `PrgConverter`) parses the line number into a **`ushort`** (`ushort.TryParse`, range 0–65535) and returns `null` — silently dropping the line — if that fails. `BasicDiagnostics`' own leading-number parser (`TryParseLineNumber`, used for duplicate-line-number detection) parses into a plain **`int`** with no upper bound at all. So today, a line numbered e.g. `70000` is treated as perfectly valid by the live diagnostics/squiggle pass, and then silently deleted when you save or deploy — a real, currently-invisible divergence between what the editor thinks is fine and what actually reaches the `.prg`.
- `BasicTokenizer.TokenizeLine`'s `catch (Exception ex)` path (`Success = false`) — reachable but rare in practice, since the tokenizer is deliberately permissive (any character it doesn't recognize as a keyword is just emitted as a literal byte, so there's no real "unknown keyword" failure mode — everything that isn't a keyword just becomes a variable-name/expression byte).

Per your answer, this spec closes both: any line `PrgConverter` would currently skip becomes a squiggle (and therefore an Errors-tab row) *before* the user ever hits Save/Deploy, not after.

## 5. Target Architecture

### 5.1 Diagnostics: from "active tab only" to "cached per open tab"

Add a property to the tab model (`Models/EditorTab.cs`):

```csharp
public IReadOnlyList<EditorDiagnostic> Diagnostics { get; set; } = Array.Empty<EditorDiagnostic>();
```

This works cleanly with the existing lifecycle because each `EditorTab` already owns its own permanent `TextDocument` (`public TextDocument Document { get; } = new();`) — a background tab's text can't change while it isn't the one loaded into the `Editor` control, so its cached diagnostics can't go stale while backgrounded. They're only ever recomputed when that tab becomes active again (which already re-runs `RunDocumentAnalysis` today) or when it's first opened.

`RunDiagnostics()` gets one line added: after computing `_currentDiagnostics` for the active tab as it does today (this keeps the live-typing squiggle behavior identical — no regression there), also stash the same list onto the tab:

```csharp
if (ViewModel.ActiveTab != null) ViewModel.ActiveTab.Diagnostics = _currentDiagnostics;
RefreshErrorsPanel();   // new — see 5.3
```

A newly-opened tab that's never been activated yet (e.g. a background tab restored on startup) needs its diagnostics computed once so it isn't silently missing from the Errors list until the user happens to click into it — call `BasicDiagnostics.Analyze`/`AsmDiagnostics.Analyze` once against its `Document.Text` right after it's added to `OpenTabs`, same rules (respects `Settings.EnableLinting`).

### 5.2 Extending `BasicDiagnostics` to cover the tokenizer gap (§4.3)

Add a check to `BasicDiagnostics.Analyze` (or a small sibling pass called from it) that, per source line: parses the leading line number the same way `PrgConverter.ParseLineNumberAndCode` does — **including its `ushort` range check**, so the two now agree — and, for the code portion, calls `BasicTokenizer.TokenizeLine` and turns any `Success == false` result into a diagnostic using `TokenizeLineResult.ErrorMessage`. Suggested messages, matching the existing terse style (`"Duplicate line number {number}."`, `"NEXT without a matching FOR."`):

- `"Line number {n} is out of range (must be 0-65535)."` — for the `ushort` overflow case.
- Whatever `TokenizeLineResult.ErrorMessage` already contains, for the rare tokenizer-exception case.

This is deliberately implemented once, inside `BasicDiagnostics`, rather than as separate wiring from `PrgConverter` into the Errors panel — that way it automatically gets a squiggle, automatically shows up in the Errors tab, and automatically stays in sync with the live-typing debounce, with no second code path to keep consistent.

### 5.3 Errors-tab aggregation

A small view-model-side row type and an `ObservableCollection` the DataGrid binds to:

```csharp
public sealed class ErrorListRow
{
    public string Severity { get; init; } = "Error";     // see §10.2 — always "Error" in v1
    public string Message { get; init; } = "";
    public string FileName { get; init; } = "";            // tab.FileName, for display
    public string? FilePath { get; init; }                  // tab.FilePath, for jump-to
    public int Line { get; init; }                           // 1-based
    public EditorTab Tab { get; init; } = null!;             // for double-click navigation
    public int Offset { get; init; }                          // for double-click navigation
}
```

`RefreshErrorsPanel()` (new, in `MainWindow.xaml.cs` alongside the other `Run*`/`Refresh*` helpers) rebuilds this collection from every tab in `ViewModel.OpenTabs`:

```csharp
private void RefreshErrorsPanel()
{
    ErrorListRows.Clear();
    foreach (var tab in ViewModel.OpenTabs)
        foreach (var diag in tab.Diagnostics)
        {
            var line = tab.Document.GetLineByOffset(Math.Min(diag.Offset, tab.Document.TextLength)).LineNumber;
            ErrorListRows.Add(new ErrorListRow
            {
                Message = diag.Message, FileName = tab.FileName, FilePath = tab.FilePath,
                Line = line, Tab = tab, Offset = diag.Offset
            });
        }
}
```

Called from: `RunDiagnostics()` (every debounced re-analysis and every tab activation, per §5.1), and whenever a tab is added to or removed from `OpenTabs` (open/close/close-all).

## 6. Errors Tab UI

### 6.1 Layout

Same visual language as the reference screenshot, scoped to what READYCode's diagnostics actually carry:

```
[Open Files ▾]  [⛔ N Errors] [⚠ 0 Warnings] [ⓘ 0 Messages]              [Search...  🔍]
──────────────────────────────────────────────────────────────────────────────────────
 ⛔  Line 70000 is out of range (must be 0-65535).      myprogram.bas        Line 45
 ⛔  Duplicate line number 100.                          myprogram.bas        Line 12
 ⛔  FOR I has no matching NEXT.                          sprites.bas         Line 8
```

Columns: **severity icon**, **Description**, **File**, **Line** — no **Code** column (§3 — nothing produces an error code today) and no **Project** column (READYCode has no multi-project concept; a file's containing folder is implicit from the open-folder tree, not worth a column).

- **Scope dropdown**: v1 offers a single option, "Open Files" — see §10.1 for why full-folder scanning isn't in v1, and why the dropdown is still present now (so adding a second option later is a small change, not a redesign).
- **Severity filter buttons**: present for visual/future-proofing parity with the reference screenshot; Warnings and Messages will read "0" and stay non-interactive until something actually produces those severities (§10.2). Clicking "Errors" toggles whether error rows are shown, same as VS.
- **Search box**: simple substring filter across the Message/File columns, client-side over `ErrorListRows` — no backing feature needed beyond a `CollectionView` filter predicate.

### 6.2 Row behavior

- **Double-click** a row: activates that row's `Tab` (via the existing `ActivateTab` helper, same one `DebugCallStackList_MouseDoubleClick` already uses for an identical "jump to a place in a possibly-inactive tab" job), then moves the caret to `Offset` and scrolls it into view — mirroring `MoveCaretToDocumentLine`'s existing scroll behavior. This matches the app's existing double-click-to-navigate convention (`DebugBreakpointsGrid_MouseDoubleClick`, `DebugCallStackList_MouseDoubleClick`) rather than introducing a new single-click-navigates pattern nothing else in the app uses.
- **Empty states**: "No issues found." when `ErrorListRows` is empty and linting is on; "Linting is disabled — enable it in Settings to see errors here." when `Settings.EnableLinting == false` (mirrors how `RunDiagnostics` already goes empty in that case).

### 6.3 Auto-activating the tab (refining your answer)

Your answer said yes to auto-activating the Errors tab when new errors appear, matching VS. One nuance worth flagging before building it: READYCode's diagnostics recompute **on a debounce timer as you type** (§4.2) — not just on an explicit build, the way VS's Error List does. Auto-switching the visible bottom-panel tab on every keystroke that happens to leave an unmatched `FOR` mid-edit (completely normal while typing) would be disruptive — the panel would flicker open/switch under the user constantly.

**Recommended default:** auto-activate (open panel if closed, switch to Errors tab) only at explicit tokenize-triggering actions — Save-to-`.prg`, Load/Run on C64U, Load/Run on VICE — i.e., exactly the moments `PrgConverter` runs today, and only when that produces at least one diagnostic. Live-typing squiggles keep updating the Errors tab's *contents* and its Errors-count badge continuously, same as they update squiggles today, but never yank focus away from the editor while the user is mid-edit. See §10.3 — flagged for confirmation, this is a judgment call, not directly answered by "yes to both."

## 7. Settings & Persistence

`Settings/AppSettings.cs` — new properties, replacing the Debug-specific ones (see §10.4 for the migration note):

```csharp
/// <summary>Whether the bottom panel (Debug/Errors) is open.</summary>
public bool IsBottomPanelOpen { get; set; } = false;

/// <summary>Remembers the height of the bottom panel.</summary>
public double BottomPanelHeight { get; set; } = 200;

/// <summary>Remembers which outer bottom-panel tab was last active: "Debug" or "Errors".</summary>
public string ActiveBottomPanelTab { get; set; } = "Debug";

/// <summary>Remembers which debug-panel tab was last active: "Variables", "Breakpoints", or "CallStack". Unchanged.</summary>
public string ActiveDebugPanelTab { get; set; } = "Variables";
```

`MainViewModel` — two new computed properties alongside the existing `IsDebugPanelOpen`, following the exact same no-`ICommand`, straight-through-to-`Settings.Save()` pattern:

```csharp
public bool IsDebugPanelActive
{
    get => Settings.IsBottomPanelOpen && Settings.ActiveBottomPanelTab == "Debug";
    set
    {
        if (value) { Settings.ActiveBottomPanelTab = "Debug"; Settings.IsBottomPanelOpen = true; }
        else if (IsDebugPanelActive) { Settings.IsBottomPanelOpen = false; }
        OnPropertyChanged(); OnPropertyChanged(nameof(IsErrorsPanelActive)); Settings.Save();
    }
}

public bool IsErrorsPanelActive
{
    get => Settings.IsBottomPanelOpen && Settings.ActiveBottomPanelTab == "Errors";
    set
    {
        if (value) { Settings.ActiveBottomPanelTab = "Errors"; Settings.IsBottomPanelOpen = true; }
        else if (IsErrorsPanelActive) { Settings.IsBottomPanelOpen = false; }
        OnPropertyChanged(); OnPropertyChanged(nameof(IsDebugPanelActive)); Settings.Save();
    }
}
```

This gives exactly the semantics a two-tab shared panel needs: checking either View-menu item opens the panel *and* switches to that tab; unchecking it closes the panel *only if that tab was the one showing* (unchecking "Debug Panel" while looking at Errors is a no-op, matching how you'd expect a checkbox for something not currently in view to behave).

The panel's own header `X` button keeps its current unconditional-close behavior, just renamed and re-targeted:

```csharp
private void CloseBottomPanel_Click(object sender, RoutedEventArgs e) => ViewModel.Settings.IsBottomPanelOpen = false;
```

## 8. XAML Changes

### 8.1 Outer tab strip (wraps the existing Debug Panel content)

`DebugPanel` `Border` gains an outer header row above the existing `VARIABLES`/`BREAKPOINTS`/`CALL STACK` strip, reusing the existing `DebugPanelTabToggle` style so it's visually consistent with zero new styling work:

```xml
<Border x:Name="BottomPanel" Grid.Row="3" ...>
    <DockPanel>
        <Border DockPanel.Dock="Top" ...>                 <!-- OUTER strip: Debug | Errors -->
            <Grid>
                <StackPanel Orientation="Horizontal">
                    <ToggleButton x:Name="BottomPanelDebugTabToggle" Content="DEBUG" IsChecked="True"
                                  Style="{StaticResource DebugPanelTabToggle}" Click="BottomPanelTab_Click" Tag="Debug"/>
                    <ToggleButton x:Name="BottomPanelErrorsTabToggle" Content="ERRORS"
                                  Style="{StaticResource DebugPanelTabToggle}" Click="BottomPanelTab_Click" Tag="Errors"/>
                </StackPanel>
                <Button Grid.Column="1" Content="&#xE711;" Click="CloseBottomPanel_Click" ToolTip="Close Panel"/>
            </Grid>
        </Border>

        <Grid>
            <!-- Debug content: today's exact DockPanel (inner Variables/Breakpoints/CallStack
                 strip + its own content Grid), moved here unchanged, x:Name="DebugPanelContent" -->
            <DockPanel x:Name="DebugPanelContent"> ... today's existing markup, verbatim ... </DockPanel>

            <!-- Errors content, new, Visibility="Collapsed" initially -->
            <Grid x:Name="ErrorsPanelContent" Visibility="Collapsed">
                <!-- toolbar row: scope dropdown, severity filter buttons, search box -->
                <!-- DataGrid x:Name="ErrorsGrid", ItemsSource bound to ErrorListRows,
                     MouseDoubleClick="ErrorsGrid_MouseDoubleClick" -->
            </Grid>
        </Grid>
    </DockPanel>
</Border>
```

`BottomPanelTab_Click` mirrors `DebugPanelTab_Click` exactly, toggling `DebugPanelContent`/`ErrorsPanelContent` visibility off the clicked button's `Tag`, and writing `Settings.ActiveBottomPanelTab`.

### 8.2 View menu

`View` menu already reserves accelerator letters `C`olumn Guide, `P`rimary/`S`econdary Side Bar, `E`xplorer, `V`ariables, `D`ebug Panel, `W`ord Wrap, S`t`atistics — your `E_rrors Panel` phrasing already avoids the `E`xplorer collision by underlining the `r`, so:

```xml
<MenuItem Header="_Debug Panel"
          IsCheckable="True"
          IsChecked="{Binding ViewModel.IsDebugPanelActive, Mode=TwoWay}"/>
<MenuItem Header="E_rrors Panel"
          IsCheckable="True"
          IsChecked="{Binding ViewModel.IsErrorsPanelActive, Mode=TwoWay}"/>
```

(Note the existing `_Debug Panel` item's binding changes from `IsDebugPanelOpen` to the new `IsDebugPanelActive` — see §10.4.)

## 9. Interaction with `EnableLinting`

No change needed to the setting itself — `RunDiagnostics()` already goes empty when `EnableLinting` is off, and the Errors tab just reflects that (§6.2's empty-state message). Toggling the setting in `SettingsWindow` already calls `ApplyCodeAnalysisSettings()` → `RunDiagnostics()`; add `RefreshErrorsPanel()` there too so the Errors tab clears/repopulates immediately, matching how the squiggles already do.

## 10. Decisions made without a further round of questions

Flagging these explicitly since they weren't covered by the four clarifying answers, but meaningfully shape the implementation. Say the word on any of these and the spec gets revised before implementation starts.

### 10.1 Scope: open tabs, not the whole folder

VS's reference screenshot defaults to "Entire Solution." READYCode has no solution/build-graph concept — just an optionally-opened folder tree — and diagnostics today are computed from each tab's in-memory `TextDocument`, never by reading files fresh off disk. Scanning every `.bas`/`.prg` file in the open folder (not just open tabs) is *possible* — `BasicDiagnostics.Analyze`/`AsmDiagnostics.Analyze` are cheap, stateless, and don't need an open `TextEditor` — but it's a bigger, separate piece of work (reading files off disk, keeping an unopened file's result from getting stale if it changes on disk, deciding what "the whole folder" even means when nothing marks a file as "not part of the program"). **Recommendation: ship "Open Files" scope in v1** (§6.1's dropdown is scaffolded for a second option so this isn't a redesign later) unless you'd rather have full-folder scanning from day one.

### 10.2 Severity: everything is "Error" for now

`EditorDiagnostic` has no severity field today, and none of the current checks (dangling GOTO target, duplicate line number, unmatched FOR/NEXT, tokenizer-drop) are "soft" — they're all "this won't work as written." The Errors tab still gets the three-way Errors/Warnings/Messages filter row for visual parity and so a future softer check (a style nit, say) has somewhere to go, but Warnings/Messages will read "0" until something actually produces one. Didn't seem worth a whole new severity taxonomy just to leave two buckets permanently empty — flag if you'd rather introduce a real `Severity` enum now even with only `Error` populated.

### 10.3 Auto-activation trigger

Covered in §6.3 — recommending "on tokenize-triggering actions" (Save/Deploy) rather than literally every debounced keystroke recompute, to avoid the panel stealing focus while typing. This is a narrower reading of "auto-activate when new errors appear" than the literal words — flag if constant live auto-activation is actually what you want.

### 10.4 Settings rename, not a migration shim

`IsDebugPanelOpen`/`DebugPanelHeight` become `IsBottomPanelOpen`/`BottomPanelHeight` (§7) rather than being kept around as deserialization-only shims that get migrated forward. Since `AppSettings` round-trips through plain `JsonSerializer.Deserialize<AppSettings>` (`Settings/AppSettings.cs` line ~414) with no custom converter, a renamed property is simply absent from an existing user's settings file the first time they launch the updated build, and just falls back to its new default (`IsBottomPanelOpen = false`, panel starts closed, `BottomPanelHeight = 200`). That's a one-time, harmless reset of a pure UI preference (not data loss) — flag if you'd rather I spec an explicit migration step in `AppSettings.Load` that reads the old key names first.

## 11. File-by-File Change List

| File | Change |
|---|---|
| `Diagnostics/BasicDiagnostics.cs` | Add the tokenizer-line-parse check described in §5.2 (mirrors `PrgConverter.ParseLineNumberAndCode`'s `ushort` bound + calls `BasicTokenizer.TokenizeLine`). |
| `Models/EditorTab.cs` | Add `Diagnostics` property (§5.1). |
| `Settings/AppSettings.cs` | Rename `IsDebugPanelOpen`→`IsBottomPanelOpen`, `DebugPanelHeight`→`BottomPanelHeight`; add `ActiveBottomPanelTab` (§7). |
| `ViewModels/MainViewModel.cs` | Replace `IsDebugPanelOpen` with `IsDebugPanelActive`/`IsErrorsPanelActive` (§7); add `ErrorListRows` collection. |
| `Views/MainWindow.xaml` | Wrap existing Debug Panel content in outer tab strip; add Errors tab content (DataGrid + toolbar) (§8). |
| `Views/MainWindow.xaml.cs` | Rename `ActivateDebugPanelTab`'s outer-toggle plumbing → `BottomPanelTab_Click`; add `RefreshErrorsPanel()`, `ErrorsGrid_MouseDoubleClick`; call `RefreshErrorsPanel()` from `RunDiagnostics()`, tab open/close, and `ApplyCodeAnalysisSettings()` (§5.3, §9). |

## 12. Testing Plan

- **Unit** (`ReadyCode.Tests`, mirroring existing `DebugConfigStoreTests.cs` conventions): `BasicDiagnostics` tests for the new tokenizer-drop check — a line with a line number > 65535, and (if reachable) a line that trips `BasicTokenizer`'s exception path.
- **Manual**:
  - Open two `.bas` tabs, put a dangling-GOTO error in each; confirm both rows appear in the Errors tab simultaneously (validates §5.1's cross-tab caching, the main structural change).
  - Double-click a row for a background tab; confirm it activates that tab and lands the caret on the right line.
  - Toggle `View > Debug Panel` and `View > Errors Panel` back and forth; confirm each opens the panel on the right tab, and unchecking the *inactive* one is a no-op (§7).
  - Close and relaunch the app; confirm panel open/closed state, height, and active outer+inner tab all restore.
  - Turn `Settings > Linting` off; confirm the Errors tab shows the "disabled" empty state and the count badge reads 0.
  - Type an invalid line (e.g. leave a `FOR` unmatched) without saving; confirm the squiggle and the Errors-tab row both appear/update live, but the panel does **not** steal focus mid-edit (§6.3/§10.3).
  - Save a `.bas` with a line numbered `70000`; confirm a squiggle + Errors row appear *before* saving, and that the line is (still, as today) dropped from the `.prg` — now with warning instead of silently.

## 13. Out of Scope / Future Considerations

- Full open-folder ("Entire Project") diagnostic scanning (§10.1).
- A real `Severity` enum with an actual Warning-producing check (§10.2).
- Error codes / a "Code" column.
- A status-bar error/warning count summary (VS shows one; READYCode's status bar currently has no such indicator at all — easy follow-on, not requested here).
- A keyboard shortcut for `View > Errors Panel`.
