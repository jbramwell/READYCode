<div align="center">
  <img src="ReadyCode/Assets/READYCode-Open Graph (1280x640).png" alt="READYCode Logo" width="600" />
</div>

# ReadyCode

A Windows desktop code editor for writing Commodore 64 BASIC programs, built around the Commodore 64
Ultimate's local network API. ReadyCode lets you write BASIC in a modern editor - with C64-accurate
PETSCII rendering, syntax highlighting, keyword completion, and line-number tooling - then tokenize it
to a real `.prg` and either save it to disk (for VICE or any other C64 emulator) or push it straight to
a C64 Ultimate over the network and run it immediately.

## Why this exists

Writing BASIC for the C64 the "authentic" way means typing into the C64's own line editor: no syntax
highlighting, no find/replace, no undo. ReadyCode keeps the *target* authentic (real tokenized `.prg`
files, real PETSCII characters, real C64 Ultimate hardware) while making the *writing* experience
modern. The editor renders PETSCII control characters using the actual C64 character-ROM glyphs (the
same mapping the KERNAL uses), so what you see in the editor - and on a printed page - matches what
the real machine would show.

## Features

- **BASIC editor** - AvalonEdit-based editor with BASIC keyword highlighting, `REM` comment
  highlighting, line-number-aware editing, a configurable column-wrap guide, and ghost-text keyword
  completion.
- **Accurate PETSCII rendering** - control and high-byte characters are remapped at render time to the
  matching C64 character-ROM glyph (via the embedded "Pet Me 64" font), without altering the
  underlying text, so existing text-based features (tokenizing, search, etc.) keep working unchanged.
- **C64 Ultimate integration** - Transfer (load) or Run a program directly on a real C64 Ultimate over
  its REST API, plus machine controls (reset, reboot, pause, resume, power off) and a device info
  dialog. The Ultimate's URL is configured once in Preferences.
- **Tokenizing / `.prg` conversion** - converts BASIC source to/from the real tokenized `.prg` binary
  format (including the `$0801` load address), compatible with VICE and other emulators, not just the
  Ultimate.
- **File Explorer panel** - a folder tree alongside the editor with inline (VS Code-style) new
  file/folder creation and rename, drag-and-drop, and the usual cut/copy/paste/delete/reveal-in-Explorer
  context menu.
- **Minify / Prettify** - reformat BASIC source for either compactness (token packing, optional line
  renumbering) or readability.
- **Printing** - Print and Print Preview render the active tab through the same PETSCII-accurate
  font/glyph pipeline as the editor, with a standard Windows Page Setup dialog for margins/orientation.
- **Themes** - Light, Dark, and a Commodore-64-palette theme, swappable at runtime.
- **Import/Export** - read/write plain-text BASIC alongside native `.prg` files.

## Architecture overview

ReadyCode is a single-window WPF (.NET 8) desktop app using a hybrid MVVM-ish pattern: `MainViewModel`
holds bindable state (open tabs, settings, status bar text, the folder tree), while `MainWindow.xaml.cs`
owns most commands and talks directly to the AvalonEdit control, since a text editor control doesn't
lend itself to pure MVVM. Commands are implemented with a small custom `RelayCommand` (`ICommand`
wrapping an `Action` + an optional `CanExecute` predicate, wired into WPF's `CommandManager` so menu
items enable/disable automatically).

```text
ReadyCode.sln
├── ReadyCode/                  # The WPF application
│   ├── Views/                  # MainWindow + dialogs (About, Settings, Go to Line, Licenses, ...)
│   ├── ViewModels/              # MainViewModel and small per-dialog view models
│   ├── Models/                  # EditorTab (one per open tab), FileTreeItem (Explorer tree node)
│   ├── Editor/                  # AvalonEdit extensions: keyword/comment/find colorizers,
│   │                            #   PetsciiGlyphGenerator (PETSCII -> C64 ROM glyph at render time),
│   │                            #   ghost-text completion, current-line highlighting
│   ├── Tokenizer/                # BASIC keyword table, the BASIC <-> tokenized .prg converter,
│   │                            #   and the PETSCII byte -> C64 screen-code map (shared by the
│   │                            #   editor's renderer and by printing)
│   ├── Minify/, Prettify/        # BASIC source-to-source transforms
│   ├── Printing/                 # Print / Print Preview (FlowDocument over the XPS pipeline)
│   ├── C64U/                     # Thin REST client for the C64 Ultimate's local HTTP API
│   ├── Settings/                 # JSON-persisted user preferences (C64U URL, wrap column, etc.)
│   ├── Resources/Themes/         # Light/Dark/C64 ResourceDictionaries
│   └── Assets/                   # App icon/logo, the embedded "Pet Me 64" font + its license
├── ReadyCode.Tests/             # xUnit tests for Tokenizer/Minify/Prettify
└── ReadyCode.Packaging/         # MSIX packaging project (.wapproj) for Store submission -
                                  #   requires Visual Studio's packaging tooling, see note below
```

### The C64 Ultimate integration

`C64U/C64UltimateClient.cs` is a thin wrapper around the Ultimate's local REST API:

| Action | Endpoint |
| --- | --- |
| Transfer (load without running) | `POST /v1/runners:load_prg` |
| Run (load and execute) | `POST /v1/runners:run_prg` |
| Device info | `GET /v1/info` |
| Machine control (reset/reboot/pause/resume/poweroff) | `PUT /v1/machine:{action}` |

The base URL is stored in `Settings/AppSettings.cs` and configured via Preferences in the app.

## Getting started

### Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022+ **or** VS Code with the C# Dev Kit extension

### Clone

```bash
git clone <this-repository-url>
cd ReadyCode
```

### Build

```bash
dotnet build ReadyCode/ReadyCode.csproj -c Debug
```

> Building the whole solution (`dotnet build ReadyCode.sln`) will also try to build
> `ReadyCode.Packaging` (a `.wapproj` MSIX packaging project), which only builds inside Visual Studio
> with its packaging tooling installed - under the plain SDK CLI it fails with an MSB4019 error about a
> missing `Microsoft.DesktopBridge.props`. That's expected outside Visual Studio; building the app
> project directly (above) avoids it.

### Run

```bash
dotnet run --project ReadyCode/ReadyCode.csproj -c Debug
```

Or in VS Code: `Ctrl+Shift+B` to build, `F5` to debug (see `.vscode/launch.json` and `tasks.json`).
In Visual Studio: open `ReadyCode.sln` and press F5.

### Run the tests

```bash
dotnet test ReadyCode.Tests/ReadyCode.Tests.csproj
```

(Running `dotnet test` from the repo root works too - it picks up the test project fine - but, like
the solution-wide build, it will also print the same `ReadyCode.Packaging` error along the way. The
test results themselves aren't affected by it.)

### Dependencies

- [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) (NuGet) - the underlying text editor control.
- `Microsoft.WindowsDesktop.App.WindowsForms` (`FrameworkReference`) - used only to reach a handful of
  classic Win32 dialogs WPF doesn't have (`ColorDialog`, `PageSetupDialog`, the classic `PrintDialog`),
  without pulling in full WinForms implicit usings.
- xUnit (`ReadyCode.Tests` only).

No external services are required to build or run the app. The C64 Ultimate integration is optional -
it only activates when you configure a device URL in Preferences.

### Packaging

There are two MSIX-related pieces in this repo:

- `ReadyCode/ReadyCode.csproj` itself is configured for a self-contained (`win-x64`) Release build with
  `WindowsPackageType=MSIX` - the `Publish MSIX (Store)` task in `.vscode/tasks.json` drives this via
  `dotnet publish`.
- `ReadyCode.Packaging/ReadyCode.Packaging.wapproj` is a separate Windows Application Packaging
  Project. As noted above, it requires Visual Studio's MSIX/packaging workload - open `ReadyCode.sln`
  in Visual Studio and build/publish that project from there.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for coding conventions, the PR workflow, and how to run the test
suite before submitting changes.

## License

© 2026 Moonspace Labs, LLC

Licensed under the MIT License. See [LICENSE](LICENSE) infor license information.

The embedded "Pet Me 64" font is third-party software, used under the terms in
[`ReadyCode/Assets/Fonts/LICENSE-PetMe64.txt`](ReadyCode/Assets/Fonts/LICENSE-PetMe64.txt) (Kreative Software Relay Fonts Free Use License) - also viewable from the app's Help > About > Licenses dialog.
