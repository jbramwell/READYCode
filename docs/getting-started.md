# Getting Started

![READYCode's main window](../images/READYCode-Home-Screen.png)

READYCode is a Windows 10/11 desktop editor for writing Commodore 64 BASIC and 6502 assembly programs. This page covers installing it, launching it for the first time, and finding your way around the interface. If you are looking for a specific feature instead, start from the [documentation home](README.md).

## Installing

READYCode runs on Windows 10 or 11. You have two options:

- **Microsoft Store** - the simplest path, and it keeps itself updated automatically. [Get it here](https://apps.microsoft.com/detail/9N70DV3X3C6S?hl=en-us&gl=US&ocid=pdpshare) on the Microsoft Store.
- **MSI installer** - available from the [Releases](https://github.com/jbramwell/READYCode/releases) page on GitHub. The installer is not currently signed, so Windows will show a Microsoft Defender SmartScreen warning the first time you run it. Choose "More info" and then "Run anyway" to proceed.

If you would rather build from source, see the [Contributing guide](../CONTRIBUTING.md).

## Launching READYCode for the first time

The first time you start READYCode, it opens with an empty workspace and no folder loaded. From here you can:

- Start a new BASIC program with **File > New > Program File** (Ctrl+N) - saves natively as a
  tokenized `.prg`.
- Start a new plain-text BASIC file with **File > New > BASIC File** (Ctrl+Shift+N) - saves natively
  as `.bas` instead, without needing a Save As.
- Start a new assembly program with **File > New > Assembly File** (Ctrl+Alt+N).
- Open a single file with **File > Open File...** (Ctrl+O). This works for `.prg`, `.bas`, `.asm`, `.s`, and `.txt` files.
- Open a whole project folder with **File > Open Folder...** (Ctrl+K Ctrl+O), which populates the Explorer panel on the left.

On later launches, READYCode can restore the tabs you had open last time (including which ones were in Hex Editor mode). This is controlled by the "Restore previous session" option in **Preferences > Settings... > Application > General**.

## A tour of the interface

READYCode's layout will feel familiar if you have used a modern code editor before:

- **Menu bar** - File, Edit, View, C64U, VICE, Preferences, and Help. The C64U and VICE menus only appear if you have not hidden them in Preferences.
- **Activity bar (left edge)** - icon buttons that switch what the primary side bar shows: the local file Explorer, the C64 Ultimate Explorer, and Search (Find in Files).
- **Primary side bar** - shows whichever panel is selected in the left activity bar. Toggle it with Ctrl+B.
- **Editor area** - a tabbed group of open documents. Each tab shows the file name and a modified indicator (a dot) when there are unsaved changes.
- **Secondary side bar (right edge)** - reference panels: BASIC Keywords, ASM Mnemonics, PETSCII Reference, Quick Keys, and Music Notes. Their own activity bar, on the right edge, switches between them. Toggle the panel with Ctrl+Alt+B.
- **Variables / Symbols panel** - a small panel below the primary side bar that lists every variable in a BASIC file, or every label and constant in an assembly file, with click-to-jump navigation.
- **Status bar** - shows things like cursor position, connection status for C64U/VICE, and a clickable line count that opens Code Statistics. On a BASIC tab it also shows the tokenized byte count, a Shift Badge for the active tab's [Upper/Lower Case Mode](basic-editor.md#upperlower-case-mode), and a Caps Lock indicator that reflects - and can toggle - the real Windows Caps Lock state. Can be hidden from **Preferences > Show Status Bar**.

## Program files versus assembly files

READYCode edits two kinds of source, and the editor behaves differently depending on which one is active:

- **BASIC programs** use the `.prg` extension when saved in their native tokenized format (the same binary format a real C64 loads and runs), matching what you would get by typing a program on real hardware and using `SAVE`. BASIC-specific features apply here: keyword highlighting and completion, PETSCII-accurate rendering, minify/prettify, and the Variables panel.
- **Assembly programs** use `.asm` or `.s` and are edited as plain text, with mnemonic highlighting, label/constant tracking, and the Symbols panel. See [The Assembly Editor](assembly-editor.md).

You can tell READYCode to treat a file as plain text instead of a tokenized `.prg` by using **File > Export...**/**Import...**, which read and write plain `.txt`. This is separate from **Save**/**Save As**, which always round-trips through the real binary format. See [File Management](file-management.md) for the difference.

## Where to go next

- [The BASIC Editor](basic-editor.md) - syntax highlighting, keyword shortcuts, PETSCII, line numbering, and more.
- [The Assembly Editor](assembly-editor.md) - the 6502 assembler and its editing features.
- [Transferring to Hardware and Emulators](c64-ultimate-and-vice.md) - running your program on a real C64 Ultimate or in VICE.
- [Preferences](preferences.md) - configuring READYCode to your liking.

---

[Back to Documentation Home](README.md)
