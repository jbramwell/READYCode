# Change Log

## [v2.2.0] - 2026-08-24

### New Features

- **BASIC Debugger** - full line-level debugging for BASIC programs against both VICE and a C64 Ultimate: breakpoints (gutter click, F9 to toggle, Ctrl+F9 to enable/disable, Ctrl+Shift+F9 to delete all), Step Into/Over/Out, Pause/Continue, Run to Cursor, a live Call Stack panel (VICE only - the C64 Ultimate's REST API has no CPU register access), and a live Variables panel showing every simple variable's current value
- **Live variable editing** - double-click a variable's value in the Variables panel to edit it in place and write the new value straight back to the running VICE or C64 Ultimate session; a string value's PETSCII control codes (cursor movement, color, reverse video) render with their real C64 glyphs, matching the code editor's font, so they're visible and preservable rather than silently lost when editing a string that has them
- **File Compare** - a side-by-side or unified diff view for any two comparable files (`.bas`, `.asm`/`.s`, or `.prg` - a BASIC `.prg` is detokenized and a machine-language one disassembled, the same as opening it directly would show), with an "Ignore Whitespace" toggle and a whole-document change-location strip for quick navigation to the next difference

### Improvements

- The Find bar no longer re-searches the whole document on every keystroke while typing nearby with it open - the search is debounced, and match highlights now track live edits via AvalonEdit's own text anchors instead of drifting onto the wrong text as the document changes

### Bug Fixes

- Fixed `SPC(`/`TAB(` tokenizing with a doubled opening parenthesis when the file was loaded on real hardware or VICE, since those two BASIC tokens bake the parenthesis into the token itself rather than treating it as a separate character
- Fixed a `.prg` file with a few bytes of trailing disk-sector padding past its own end-of-program marker being misclassified as machine language instead of BASIC, opening it in the hex viewer instead of the editor

## [v2.1.0] - 2026-08-02

### New Features

- **Disassembler** - turns 6502 machine code back into address-annotated assembly text in a read-only tab. **Disassemble at...** (now available for both C64U and VICE, previously C64U only) reads live memory from a running C64 Ultimate or VICE instance; **Disassemble file** does the same for a machine-language `.prg`/`.ml` file in either Explorer tree, automatically detecting and skipping a BASIC loader stub so disassembly starts at the real code
- **Load/Run from the file tree** - right-click a `.prg`, `.asm`/`.s`, or machine-language file directly in either Explorer tree for Load ▸ / Run ▸ submenus that send it straight to the C64 Ultimate or VICE without opening it first
- **Drag-and-drop overhaul** - full support in the C64U Explorer tree (previously local-only), dragging a file onto a `.d64`/`.d81` disk image now embeds it directly (assembling `.asm`/`.s` or tokenizing `.bas` along the way), and dragging in from Windows Explorer is now target-aware: drop onto a folder to copy/upload, onto a disk image to embed, or anywhere else to open as new tabs
- Standalone assembly programs (an explicit `.org`, or Assembler Output set to Standalone) now auto-start correctly when run on VICE, matching the C64 Ultimate's existing behavior, by typing a `SYS` command into the keyboard buffer after loading
- **[RFC] BASIC Module System** - a draft spec for splitting a BASIC program across multiple files is up for community feedback; see the README and [the GitHub issue](https://github.com/jbramwell/READYCode/issues/1)

### Improvements

- Context menus across both Explorer trees standardized: consistent "Open in BASIC/Assembly/Hex editor" wording, "Disassemble file" placement, repositioned "Reveal in File Explorer" and "Add File...", and "Download to PC..." renamed for clarity
- "Add File to Disk Image" now correctly assembles `.asm`/`.s` source before embedding it, instead of writing the raw, unassembled text
- Linting, ghost-text completion, and keyword completion are now disabled for read-only disassembly tabs
- Disassembly toolbar polish: improved focus behavior, tab title, button styling, and height-clipping fixes
- Disassembler address fields are now forced to uppercase
- Assembler performance improvements for large source files
- Symbols panel reorganized for assembly files
- Editor tab tooltip now shows the full file path, with a new Copy Path context-menu item
- `.bas` files are no longer tokenized under any circumstance, keeping them byte-for-byte plain PETSCII text
- Expanded documentation for the Disassembler, drag-and-drop, and Assembly editor addressing-mode behavior

### Bug Fixes

- Fixed VICE's Run command not auto-starting programs with an explicit origin (`* = $c000`)
- Fixed a 6502 assembler bug where an absolute-mode operand with a value under 256 written as a 4-digit hex literal (e.g. `$00F0`) was silently narrowed to zero-page addressing, corrupting instruction length and later branch offsets
- Fixed BASIC-loader-stub detection failing on real-world stubs with non-canonical link pointers or no trailing end-of-program marker, so Load/Run and Disassemble file couldn't find the true machine-code origin in some existing files
- Fixed empty `.prg` files being misclassified as machine language, and a crash when opening or restoring them
- Fixed a UTF-8 byte-order mark in `.asm`/`.s` files causing spurious "Unknown mnemonic" errors when loading or running from the file tree
- Fixed dragging a file onto a disk image showing a blocked-drop cursor and refusing to embed it
- Fixed the disassembly view's gutter showing sequential line numbers instead of real memory addresses

## [v2.0.0] - 2026-07-26

### New Features

- **6502 Assembly support** - a full assembly-language editor mode alongside BASIC: mnemonic, label, and directive highlighting, a built-in two-pass assembler covering all 56 official 6502 opcodes and addressing modes, `.org`/`.byte`/`.text`/`.word` directives, inline diagnostics, a Symbols panel, hover tooltips, and an ASM Mnemonics reference panel
- **Hex Editor** - open any file as a raw offset/hex/ASCII grid, with inline byte editing, click-drag selection, and its own undo/redo history
- **.d81 disk image support** - both Explorers can now browse, create, and author `.d81` (1581) disk images in addition to `.d64`
- **Find in Files / Replace in Files** - project-wide search and replace across `.bas`, `.asm`, `.s`, `.txt`, and `.prg` files, with match case, whole word, and regular expression options, and a results tree grouped by file
- **BASIC keyword shortcuts** - recognizes the real C64 keyboard abbreviations for around fifty BASIC keywords (an unshifted letter or two followed by a shifted letter), plus PRINT's `?` shorthand, honored by tokenizing, syntax highlighting, hover tooltips, and GOTO/GOSUB navigation
- **Online documentation** - a full documentation site covering every major feature now lives in [`/docs`](docs/README.md), linked from the README and from Help > View Online Docs

### Improvements

- Minify's line renumbering now starts at line 0 instead of line 1 (thanks for the tip, @jim_64s8-bitprojects)
- Prettify, Minify, and hover tooltips all recognize BASIC keyword shortcuts consistently, so a line using a shortcut like `Da` for `DATA` gets the same treatment as the full keyword

### Bug Fixes

- Fixed the PETSCII Reference pane inserting the wrong character for PETSCII codes in the graphics block (96-126), among a few others
- Fixed assembly-file comments rendering as PETSCII graphics instead of plain text, a regression from the PETSCII fix above
- Fixed Prettify not adding spaces around mathematical and logical operators (`=`, `+`, `-`, `*`, `/`, `^`, `<`, `>`, `<>`, `<=`, `>=`), while correctly leaving a negating `-` unspaced
- Fixed `REM` comments gaining an extra space every time a file was saved and reopened

## [v1.2.0] - 2026-07-18

### New Features

- **Variables window** - new panel showing all variables declared in the current program
- **Static analysis (linting)** - red squiggles flag syntax errors inline before you transfer to the C64U or VICE
- **Code folding** - collapse REM blocks and FOR/NEXT loops to reduce visual noise in longer programs
- **Renumber** - renumbers line numbers across the entire program, automatically updating all GOTO and GOSUB references (Code menu)
- **Go to line from GOTO/GOSUB** - Press F12 on a line number reference to jump directly to that line
- **Reopen closed tab** (Ctrl+Shift+T) - tab history tracks recently closed files so you can reopen them
- **Restore tabs on startup** - previously open files are automatically restored when READYCode launches
- **Drag-and-drop** - drag .prg files from Windows Explorer directly onto READYCode to open them
- **Ctrl+Tab / Shift+Ctrl+Tab** - cycle forward and backward through open editor tabs
- **Tooltips for keywords and variables** - hover over any keyword or variable to see a description
- **Tooltips for special characters** - hover over PETSCII special characters to see their name and PETSCII value
- **Function keys in Quick Keys pane** - F1-F8 key mappings now appear in the Quick Keys panel
- **File > Close Folder** - new menu item to close the current folder

### Improvements

- Syntax highlighting now covers string literals and numeric literals in addition to keywords
- Improved Commodore 64 color theme accuracy
- Tokenized byte count is now displayed in the status bar when saving a file
- BASIC keyword descriptions updated to be clearer and more descriptive
- Code prettify spacing corrected around operators and keywords
- Context menus cleaned up; added option to bring VICE Emulator to the foreground
- Keyboard shortcuts for Transfer and Run commands remapped for consistency

### Bug Fixes

- Fixed Minify incorrectly stripping values from DATA statements (thanks for the tip, @johnginno5671)
- Fixed line number auto-increment generating numbers between existing lines when pressing Enter mid-program

## [v1.1.0] - 2026-07-11

This release of READYCode v1.1 contains a few new features and improvements as well as a few bug fixes. As always, please [open an issue](https://github.com/jbramwell/READYCode/issues/new/choose) if you run into any problems or have feature requests.

### New Features

- **Music Note Panel** - A new SID music note reference panel has been added to the right panel
- **BASIC Keywords Panel** - A new BASIC keywords tab has been added to the right panel, with an option to show or hide it
- **Minify Bytes Saved** - The status bar now displays the number of bytes saved after minifying your code
- **Hide VICE/C64U Menu** - New Settings options allow you to hide the VICE and C64 Ultimate menus

### Improvements

- **Smarter Auto-Numbering** - Pressing Shift+Enter no longer triggers auto line numbering
- **Smarter Auto-Numbering** - Pressing Enter on a line containing only a line number no longer generates the next line number
- **New File Focus** - The editor now automatically receives focus when a new file is created
- **New Help Menu Item** - Added a link to this repo in the Help menu

### Bug Fixes

- **Right Panel State** - Fixed an issue where right panels were not correctly restoring their open/closed state after restarting the app
- **Right Panel Sizing** - Fixed an issue where right panel tabs were resetting their size when switching between tabs

## [v1.0.0] - 2026-06-29

This is the initial release of the READYCode editor designed for the Commodore 64 Ultimate and the VICE emulator. Initial features include:

- PETSCII-aware text editor
- Shortcut keys for entering special characters, such as "CLR", "HOME", etc.
- Syntax highlighting specific to Commodore BASIC
- Ability to "prettify" code by adding whitespace, renumbering lines, etc.
- Ability to "minimize" code by removing whitespace, renumbering lines, etc.
- Ability to tranfser (and run) code directly to the Commodore 64 Ultimate over a local network
- Ability to transfer (and run) code directly to the VICE emulator running on the same machine or another machine over a local network
- A PETSCII reference pane for quickly looking up PETSCII values
- Light/Dark/C64 theme support
- Printer support (with PETSCII graphics)
- Lots more!

## The Installer (MSI)

There is an MSI available for this release; However, the MSI is not signed. You will need to approve the install when prompted. See the following screenshots:

![Microsoft Defender SmartScreen](https://github.com/jbramwell/READYCode/blob/main/images/defender-1.png?raw=true)

![Microsoft Defender SmartScreen](https://github.com/jbramwell/READYCode/blob/main/images/defender-2.png?raw=true)

**NOTE**: We hope to be able to sign the installer at some point in the future to simplify this process.

## Feature Requests and Contributions

If you would like to request new features, please [open an issue](https://github.com/jbramwell/READYCode/issues/new/choose).

If you're interested in contributing to this project, please refer to: [Contributing to READYCode](https://github.com/jbramwell/READYCode/blob/main/CONTRIBUTING.md).
