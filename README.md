<div align="center">
  <img src="images/READYCode-Home-Screen.png" alt="READYCode Logo" width="600" />
</div>

# READYCode

A Windows 10/11 desktop code editor for writing Commodore 64 BASIC and 6502 assembly programs, built around the [Commodore 64 Ultimate](https://commodore.net/computer/)'s local [network API](https://1541u-documentation.readthedocs.io/en/latest/api/api_calls.html). READYCode brings both languages into a modern editor - C64-accurate PETSCII rendering, syntax highlighting, keyword completion, and line-number tooling for BASIC, plus mnemonic highlighting and a built-in assembler for 6502 - then produces a real .prg you can save to disk (for VICE or any other C64 emulator) or push straight to a C64 Ultimate or VICE emulator over the network and run immediately. Beyond writing code, READYCode can also browse and manage the C64 Ultimate's own storage over FTP - including looking inside `.d64` disk images and opening the programs stored on them - right alongside your local project files.

## [RFC] BASIC Module System

For anyone interested in providing feedback into a BASIC Module System (i.e., splitting a BASIC program across multiple files), please [find the spec here](https://github.com/jbramwell/READYCode/issues/1) and provide your feedback. I truly appreciate it. Thanks!

## READYCode Roadmap

If you want to see the features being considered and/or planned for READYCode, check out the [roadmap](Roadmap.md). If you would like to suggest additional features - or comment on existing roadmap items - please [open a new issue](https://github.com/jbramwell/READYCode/issues/new/choose).

## Why this exists

Writing BASIC for the C64 the "authentic" way means typing into the C64's own line editor: no syntax
highlighting, no find/replace, no undo. ReadyCode keeps the *target* authentic (real tokenized `.prg`
files, real PETSCII characters, real C64 Ultimate hardware) while making the *writing* experience
modern. The editor renders PETSCII control characters using the actual C64 character-ROM glyphs (the
same mapping the KERNAL uses), so what you see in the editor - and on a printed page - matches what
the real machine would show.

## Documentation

Full documentation, covering every feature in depth, lives in [docs/](docs/README.md). The summary below is a quick overview; head there for the details.

## Features

- **BASIC editor** - AvalonEdit-based editor with BASIC keyword highlighting, `REM` comment
  highlighting, line-number-aware editing, a configurable column-wrap guide, and ghost-text keyword
  completion.
- **BASIC keyword shortcuts** - recognizes the same keyboard abbreviations a real C64 keyboard produces
  for around fifty BASIC keywords (an unshifted letter or two followed by one shifted letter, inserted
  as the correct PETSCII graphic), honored everywhere a keyword is recognized: tokenizing, syntax
  highlighting, and hover tooltips. `PRINT`'s `?` shorthand is recognized the same way.
- **Diagnostics** - inline squiggle warnings for common mistakes as you type: duplicate line numbers,
  `GOTO`/`GOSUB`/`THEN` targets that don't exist, and unmatched `FOR`/`NEXT` pairs in BASIC; undefined
  labels, bad addressing modes, and out-of-range branches in assembly. An **Errors panel** (`View >
  Errors Panel`) lists every current diagnostic across all open tabs, VS-Error-List style, with
  double-click-to-jump and a search/filter box; it opens automatically after a Save, or a Load/Run on
  the C64 Ultimate or VICE, if that action produced any diagnostics.
- **Code folding** - collapse `REM` blocks and `FOR`/`NEXT` loops in BASIC, or runs of comment lines in
  assembly, to cut down on visual noise in longer programs.
- **Variables / Symbols panel** - lists every variable in a BASIC program, or every label and constant
  in an assembly program, with click-to-jump navigation to each occurrence and one-step rename (F2).
- **Reference panels** - BASIC Keywords, ASM Mnemonics, PETSCII Reference, Quick Keys, and Music Notes
  panels for looking things up without leaving the editor, each with descriptions on hover and
  click-to-insert where it makes sense.
- **Assembly editor** - a full 6502 assembly mode alongside BASIC: mnemonic/label/directive
  highlighting, a built-in two-pass assembler covering all 56 official opcodes and addressing modes,
  `.org`/`.byte`/`.text`/`.word` directives, and inline diagnostics. Assembling without `.org` produces
  a directly runnable `.prg` with an auto-generated BASIC loader stub; assembling with `.org` writes a
  standard load-address header instead.
- **Disassembler** - turns 6502 machine code back into address-annotated assembly text in a read-only
  tab. **Disassemble at...** (C64U and VICE menus) reads live memory directly from a running C64
  Ultimate or VICE instance starting at a given address; **Disassemble file** (either Explorer's
  right-click menu) does the same for a machine-language `.prg`/`.ml` file on disk, automatically
  detecting and skipping any BASIC loader stub so disassembly starts at the real machine code.
- **Hex Editor** - open any file - not just recognized BASIC/assembly/disk-image types - as a raw
  offset/hex/ASCII grid, with inline byte editing, its own undo/redo history, and full support for
  files inside a mounted `.d64`/`.d81` disk image.
- **Accurate PETSCII rendering** - control and high-byte characters are remapped at render time to the
  matching C64 character-ROM glyph (via the embedded "Pet Me 64" font), without altering the
  underlying text, so existing text-based features (tokenizing, search, etc.) keep working unchanged.
- **Upper/Lower Case Mode** - toggles a BASIC tab's C64 keyboard emulation between the default "Upper
  Active" charset (unshifted letters render upper case; Shift/Caps Lock produces the PETSCII graphic)
  and "Upper Inactive" (unshifted renders lower case; Shift/Caps Lock produces upper case) - `Edit >
  Lower Case Mode` (Ctrl+Shift+L), or click the Shift Badge in the status bar. The status bar also
  shows a Caps Lock indicator that reflects, and can toggle, the real Windows Caps Lock state.
- **C64 Ultimate integration** - Transfer (load) or Run a program directly on a real C64 Ultimate over
  its REST API, plus machine controls (reset, reboot, pause, resume, power off) and a device info
  dialog. The Ultimate's URL is configured once in Preferences.
- **C64U Explorer panel** - a second Explorer tab (alongside the local Folder Explorer) that connects to
  the C64 Ultimate's FTP file service and browses its storage - USB drives, internal Flash, Temp -
  without leaving the editor. Right-click a `.d64`/`.d81` disk image to mount it to Drive A or B (a
  status footer shows what's currently mounted on each drive, with a one-click eject), or expand a
  `.d64`/`.d81` image in place to see the individual programs stored on it and open any of them
  directly in the editor. Enable it on the device under Ultimate menu -> Network Services -> FTP file
  service; READYCode never auto-connects on its own, so nothing happens on the network until you click
  Connect.
- **VICE integration** - a counterpart to the C64 Ultimate integration for the VICE emulator: READYCode
  launches and manages the VICE process directly, and talks to its binary monitor protocol over TCP to
  load and run programs and issue machine controls (reset, reboot, pause, resume, power off) without
  restarting the emulator each time. The emulator path and monitor host/port are configured once in
  Preferences, with an option to bring VICE to the foreground automatically when loading or running.
- **BASIC debugger** - source-level debugging against a live C64 Ultimate or VICE session: gutter
  breakpoints, Step Into and Run to Cursor, a current-line highlight while paused, and a Debug panel with
  live Variables (editable), Breakpoints, and GOSUB Call Stack views. Step Over, Step Out, and the Call
  Stack view all need to track the call stack, which isn't possible on the C64 Ultimate's REST API - a
  limitation of the device, not VICE's richer binary monitor protocol, which supports all of it.
- **Tokenizing / `.prg` conversion** - converts BASIC source to/from the real tokenized `.prg` binary
  format (including the `$0801` load address), compatible with VICE and other emulators, not just the
  Ultimate. The same converter can also tell a real BASIC program apart from a raw machine-language
  `.prg` by validating its tokenized line structure, rather than just trusting the file's extension or
  type byte - used to decide which files inside a `.d64`/`.d81` (in either Explorer) are safe to open as text.
- **File Explorer panels** - both the local Folder Explorer and the C64U Explorer share the same tree
  UI: inline (VS Code-style) new file/folder creation and rename, drag-and-drop (move within or between
  either tree; drop a file onto a disk image to embed it directly, assembling `.asm`/`.s` source or
  tokenizing `.bas` source along the way), cut/copy/paste/delete/reveal-in-Explorer, right-click-to-select,
  and a color-coded file-type badge and icon (folder, floppy disk, or document) for
  BASIC/machine-language/disk-image files. Dragging files in from Windows Explorer copies them into a
  hovered folder, embeds them into a hovered disk image, or - dropped anywhere else - opens them as new
  tabs (`.prg` files only). Both trees can expand a `.d64`/`.d81` disk image in place to browse - and
  open - the programs stored inside it. Right-click a `.prg`, `.asm`/`.s`, or machine-language file in
  either tree for Load ▸ / Run ▸ submenus that send it straight to the C64 Ultimate or VICE without
  opening it first; machine-language files get a Disassemble file option in place of Open in
  BASIC/Assembly editor.
- **Disk image authoring** - both Explorers can create a new, blank `.d64`/`.d81` right from the tree,
  and add, replace, rename, or delete individual programs inside an existing one - maintaining a valid
  BAM and directory chain so the result loads correctly in VICE or on real hardware. Saving a program
  that was opened from inside a mounted disk image writes the edit straight back into it. The C64U
  Explorer's version works the same way over FTP (download, modify, re-upload) as the local Explorer
  does directly on disk.
- **Find in Files / Replace in Files** - project-wide search across every `.bas`, `.asm`, `.s`, `.txt`,
  and `.prg` file under an open folder (a `.prg` is decoded to text for matching and re-tokenized on
  write-back), with match-case, whole-word, and regular-expression options, a results tree grouped by
  file, and a project-wide Replace All.
- **File Compare** - right-click two files in either Explorer (Select file for comparison, then Compare
  file) for a line- and word-level diff, in Split or Unified view, with an Ignore Whitespace toggle.
  Either file can be BASIC or assembly source, `.prg` (decoded to text) or already-plain-text - the two
  sides don't need to be the same kind.
- **Minify / Prettify** - reformat BASIC source for either compactness (token packing, optional line
  renumbering) or readability.
- **Printing** - Print and Print Preview render the active tab through the same PETSCII-accurate
  font/glyph pipeline as the editor, with a standard Windows Page Setup dialog for margins/orientation.
- **Themes** - Light, Dark, and a Commodore-64-palette theme, swappable at runtime.
- **Session and tab management** - restores the tabs you had open (including which ones were in Hex
  Editor mode) the next time you launch READYCode, and keeps a history of recently closed tabs to
  reopen.
- **Code Statistics** - a dialog showing character/word/line counts for the active document, plus its
  tokenized (BASIC) or assembled (assembly) byte count.
- **Import/Export** - read/write plain-text BASIC alongside native `.prg` files. **File > New > BASIC
  File** (Ctrl+Shift+N) starts a blank tab that saves natively as plain-text `.bas` from the start,
  alongside the existing **New > Program File** (`.prg`, Ctrl+N) and **New > Assembly File** (Ctrl+Alt+N).

## Architecture overview

ReadyCode is a single-window WPF (.NET 8) desktop app using a hybrid MVVM-ish pattern: `MainViewModel`
holds bindable state (open tabs, settings, status bar text, the folder tree), while `MainWindow.xaml.cs`
owns most commands and talks directly to the AvalonEdit control, since a text editor control doesn't
lend itself to pure MVVM. Commands are implemented with a small custom `RelayCommand` (`ICommand`
wrapping an `Action` + an optional `CanExecute` predicate, wired into WPF's `CommandManager` so menu
items enable/disable automatically).

Everything that doesn't need a UI toolkit - the tokenizer, the assembler, diagnostics, minify and
prettify, the diff engine, the debugger, and the VICE and C64 Ultimate clients - lives in
`ReadyCode.Core`, a plain `net8.0` class library that the WPF application references. It builds and
its tests run on Windows, macOS, and Linux, which keeps that logic unit-testable without a window
and lets other front ends share it.

```text
ReadyCode.sln
├── ReadyCode/                  # The WPF application (Windows)
│   ├── Views/                  # MainWindow + dialogs (About, Settings, Go to Line, Licenses, ...)
│   ├── ViewModels/              # MainViewModel and small per-dialog view models
│   ├── Models/                  # EditorTab (one per open tab) and FileTreeItem (local Explorer
│   │                            #   tree node), which shares its file-kind/badge/icon and virtual
│   │                            #   (inside-a-.d64) entry model with the core's C64UFileItem
│   ├── Editor/                  # AvalonEdit extensions: keyword/comment/find colorizers,
│   │                            #   PetsciiGlyphGenerator (PETSCII -> C64 ROM glyph at render time),
│   │                            #   ghost-text completion, current-line highlighting, and thin
│   │                            #   adapters over the core's folding and completion analysis
│   ├── Printing/                 # Print / Print Preview (FlowDocument over the XPS pipeline)
│   ├── Converters/                # WPF value converters used by bindings in MainWindow.xaml
│   │                            #   (e.g. cross-referencing a tree item's path against drive-mount
│   │                            #   state to highlight what's mounted on Drive A/B)
│   ├── Resources/Themes/         # Light/Dark/C64 ResourceDictionaries
│   └── Assets/                   # App icon/logo, the embedded "Pet Me 64" font + its license
├── ReadyCode.Core/             # UI-framework-free class library (net8.0), referenced by the app
│   ├── Tokenizer/                # BASIC keyword table, the BASIC <-> tokenized .prg converter
│   │                            #   (including BASIC-vs-machine-language detection), and the
│   │                            #   PETSCII byte -> C64 screen-code map (shared by the editor's
│   │                            #   renderer and by printing)
│   ├── Assembler/                # 6502 assembler, disassembler, and opcode table
│   ├── Diagnostics/              # BASIC and assembly analyzers behind the Errors panel
│   ├── Minify/, Prettify/        # BASIC source-to-source transforms
│   ├── Formatting/               # Assembly source formatter
│   ├── Editor/                   # Toolkit-neutral editor analysis: fold-region finders over a
│   │                            #   SourceLine model, and the keyword completion providers
│   ├── C64U/                     # REST client for the C64 Ultimate's local HTTP API, an FTP client
│   │                            #   (FluentFTP) for its file service, and a .d64/.d81 disk image parser
│   ├── Vice/, Debugger/          # VICE binary monitor client and the BASIC debugger built on it
│   ├── Diff/, Search/            # File compare engine and project-wide search
│   ├── Models/                   # Plain model types (C64UFileItem, VariableInfo, ...)
│   ├── Settings/                 # JSON-persisted user preferences (C64U URL, wrap column, etc.)
│   ├── Sid/                      # SID note frequency table used by the Music Notes panel
│   └── Assets/Data/              # The embedded SID note table
├── ReadyCode.Tests/             # xUnit tests over ReadyCode.Core - run on any OS
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
| List drive status | `GET /v1/drives` |
| Mount an image to a drive | `PUT /v1/drives/{id}:mount?image=<path>` |
| Eject a drive | `PUT /v1/drives/{id}:remove` |

The base URL is stored in `Settings/AppSettings.cs` and configured via Preferences in the app.

### The C64U Explorer (FTP file browsing)

Separately from the REST API above, `C64U/C64UFtpClient.cs` wraps [FluentFTP](https://github.com/robinrodricks/FluentFTP)
to browse the Ultimate's own storage - USB drives, internal Flash, and Temp - directly in the app, in a
tree that mirrors the local Folder Explorer. It logs in as `admin` with a blank password on port 21,
matching the Ultimate's built-in FTP file service; enable that service on the device itself under the
Ultimate menu -> Network Services -> FTP file service before connecting. READYCode never connects on
its own - nothing happens on the network until you open the C64U Explorer tab and click Connect.

`C64U/DiskImage.cs` parses standard `.d64` (35-track 1541) and `.d81` (80-track 1581) disk images
directly from bytes, using the track/sector layout supplied by `C64U/DiskGeometry.cs` - reading the
BAM/directory chain and following each file's own track/sector chain - so a disk image can be expanded
in the tree to reveal the individual programs stored on it, without needing to mount it first. This
works the same way in both the C64U Explorer (parsing bytes downloaded over FTP) and the local Folder
Explorer (parsing bytes read straight from disk).

## Getting started

### Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022+ **or** VS Code with the C# Dev Kit extension

> Windows is required to build and run the application itself, which is built on WPF.
> `ReadyCode.Core` and `ReadyCode.Tests` need only the .NET 8 SDK, so that half of the repository
> builds and its tests run on macOS and Linux as well.

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

The tests cover `ReadyCode.Core` and reference only that project, so they run on any operating
system with the .NET 8 SDK installed. To check from a non-Windows machine that a core change hasn't
broken the Windows front end, compile it without running it:

```bash
dotnet build ReadyCode/ReadyCode.csproj -c Debug -p:EnableWindowsTargeting=true
```

### Dependencies

- [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) (NuGet) - the underlying text editor control.
- [FluentFTP](https://github.com/robinrodricks/FluentFTP) (NuGet) - the FTP client used by the C64U
  Explorer to browse the C64 Ultimate's storage.
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
