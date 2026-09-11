# READYCode Documentation

![A BASIC listing with REM comments rendered as PETSCII graphics](../images/READYCode-Sprite-Editor.png)

READYCode is a Windows 10/11 desktop editor for writing Commodore 64 BASIC and 6502 assembly programs, built around real hardware and emulator workflows: what you write is the same tokenized `.prg` a C64 would produce itself, and it can be sent straight to a [C64 Ultimate](https://commodore.net/computer/) or [VICE](https://vice-emu.sourceforge.io/) emulator and run. This documentation covers what the application can do, organized by feature area.

For installation and a build-from-source guide, see the [project README](../README.md).

## Contents

### Start here

- [Getting Started](getting-started.md) - installing READYCode, launching it for the first time, and a tour of the interface.

### Editing

- [The BASIC Editor](basic-editor.md) - syntax highlighting, keyword completion and shortcuts, PETSCII-accurate rendering, Upper/Lower Case Mode, line numbering, diagnostics, the Errors panel, and the reference panels.
- [The Assembly Editor](assembly-editor.md) - the built-in 6502 assembler, supported mnemonics and directives, labels, and diagnostics.
- [Minify and Prettify](minify-and-prettify.md) - reshaping BASIC source for compactness or readability.
- [The Hex Editor](hex-editor.md) - viewing and editing the raw bytes of any file.

### Files and projects

- [File Management and Printing](file-management.md) - the Folder Explorer, tabs, comparing files, import/export, and printing.
- [Disk Images (.d64 and .d81)](disk-images.md) - browsing, creating, and editing Commodore disk images.
- [Find in Files](find-in-files.md) - searching and replacing across an entire project.

### Running your program

- [Transferring to Hardware and Emulators](c64-ultimate-and-vice.md) - loading and running programs on a real C64 Ultimate or in VICE.
- [Debugging](debugging.md) - breakpoints, stepping, the Variables and Call Stack panels, on a real C64 Ultimate or in VICE.

### Reference

- [Preferences](preferences.md) - every setting in the Settings window, explained.
- [Keyboard Shortcuts](keyboard-shortcuts.md) - the full shortcut list in one place.

## Getting help

Found a bug, or have a feature request? [Open an issue](https://github.com/jbramwell/READYCode/issues/new/choose) on GitHub. If you would like to contribute code, see [CONTRIBUTING.md](../CONTRIBUTING.md) for coding conventions and the PR workflow.
