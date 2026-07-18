# Change Log

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
