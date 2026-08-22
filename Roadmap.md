# READYCode Roadmap

*Updated:* August 2026

---

## Commodore 64 Ultimate REST API Coverage

| Category | Feature | C64U Roadmap | VICE Roadmap |
|---|---|---|---|
| **About** | | | |
| About | Device info &amp; version | ✓ Done | ✓ Done |
| About | API version check | ✓ Done | ✓ Done |
| **Runners** | | | |
| Runners | Load PRG (no run) | ✓ Done | ✓ Done |
| Runners | Load and run PRG | ✓ Done | ✓ Done |
| Runners | Play SID file | Planned | Planned |
| Runners | Run cartridge image (.crt) | Planned | Planned |
| **Machine Control** | | | |
| Machine | Reset C64 | ✓ Done | ✓ Done |
| Machine | Reboot | ✓ Done | ✓ Done |
| Machine | Pause / Resume CPU | ✓ Done | ✓ Done |
| Machine | Read C64 memory | ✓ Done | ✓ Done |
| Machine | Write to C64 memory | Planned | Planned |
| Machine | Power off | ✓ Done | ✓ Done |
| Machine | Simulate menu button | Planned | N/A |
| **Configuration** | | | |
| Config | Read configuration | Planned | N/A |
| Config | Write configuration | Planned | N/A |
| Config | Save / restore config | Planned | N/A |
| **Floppy Drive Management** | | | |
| Drives | List drives &amp; mounted images | Partial / Planned | Unplanned |
| Drives | Mount disk image | ✓ Done | Unplanned |
| Drives | Unmount / eject disk | ✓ Done | Unplanned |
| Drives | Reset drive | Planned | Planned |
| Drives | Enable / disable drive | Possible | Unplanned |
| Drives | Set drive type (1541/1571/1581) | Planned | Unplanned |
| Drives | Load custom drive ROM | Possible | Unplanned |
| **Data Streams (U64 only)** | | | |
| Streams | Start/stop video stream | Not Yet Possible | Unplanned |
| Streams | Start/stop audio stream | Not Yet Possible | Unplanned |
| Streams | Start/stop debug stream | Not Yet Possible | Unplanned |
| **File Operations** | | | |
| Files | Browse C64U file system | ✓ Done | N/A |
| Files | Upload files to C64U | ✓ Done | N/A |
| Files | Download files from C64U | ✓ Done | N/A |
| Files | Create D64 disk image | ✓ Done | ✓ Done |
| Files | Create D81 disk image | ✓ Done | ✓ Done |

---

## Editor Capabilities

| Category | Feature | C64U Roadmap | VICE Roadmap |
|---|---|---|---|
| **Syntax &amp; Editing** | | | |
| Editing | Syntax highlighting | ✓ Done | ✓ Done |
| Editing | Auto-indent | ✓ Done | ✓ Done |
| Editing | Undo / Redo | ✓ Done | ✓ Done |
| Editing | Tab-based multi-file editing | ✓ Done | ✓ Done |
| Editing | Code folding | ✓ Done | ✓ Done |
| Editing | Multi-cursor editing | Planned | Planned |
| Editing | Bracket / delimiter matching | Planned | Planned |
| Editing | Column / ruler guides | Planned | Planned |
| Editing | Split editor panes | Planned | Planned |
| Editing | Code snippets | Planned | Planned |
| Editing | Minimap / code overview | Planned | Planned |
| **Intelligence &amp; Navigation** | | | |
| Intelligence | Inline diagnostics (squiggles) | ✓ Done | ✓ Done |
| Intelligence | Hover tooltips | ✓ Done | ✓ Done |
| Intelligence | Go to definition / label | ✓ Done | ✓ Done |
| Intelligence | Symbols / outline panel | ✓ Done | ✓ Done |
| Intelligence | Keyword / mnemonic autocomplete | Partial / High Priority | Partial / High Priority |
| Intelligence | Find all references | Planned | Planned |
| Intelligence | Rename symbol (refactor) | Partial / Planned | Partial / Planned |
| Intelligence | Command palette | Planned | Planned |
| **Search &amp; Replace** | | | |
| Search | Find / Replace in file | ✓ Done | ✓ Done |
| Search | Find in Files (project-wide) | ✓ Done | ✓ Done |
| Search | Replace in Files (project-wide) | ✓ Done | ✓ Done |
| Search | Regex search | ✓ Done | ✓ Done |
| Search | Replace with preview / diff | Planned | Planned |
| **Debugging: BASIC** | | | |
| Debug | Pause / resume execution | Next Release | Next Release |
| Debug | variable watch | Next Release | Next Release |
| Debug | Live variable editor (write) | High Priority | High Priority |
| Debug | Breakpoints | Next Release | Next Release |
| Debug | Live screen preview (VICE Only) | Not Possible (U64 Only) | Planned |
| Debug | Step into / over | Next Release | Next Release |
| **Debugging: Assembly** | | | |
| Debug | Live memory viewer (read) | ✓ Done | ✓ Done |
| Debug | Pause / resume execution | Planned | Planned |
| Debug | Live memory editor (write) | Planned | Planned |
| Debug | Breakpoints | Planned | Planned |
| Debug | Register | Planned | Planned |
| Debug | Live screen preview (VICE Only) | Not Possible (U64 Only) | Planned |
| Debug | Step into / over | Planned | Planned |
| **Build &amp; Deploy** | | | |
| Build | Built-in two-pass assembler | ✓ Done | ✓ Done |
| Build | BASIC tokenizer | ✓ Done | ✓ Done |
| Build | BASIC loader stub auto-generation | ✓ Done | ✓ Done |
| Build | Deploy to C64 Ultimate (Wi-Fi) | ✓ Done | ✓ Done |
| Build | Deploy to VICE emulator | ✓ Done | ✓ Done |
| Build | Build output / error panel | ✓ Done | ✓ Done |
| Build | BASIC module system (multi-file) | Planned | Planned |
| **C64-Specific Tools** | | | |
| C64 Tools | Hex editor | ✓ Done | ✓ Done |
| C64 Tools | 6502 Disassembler | ✓ Done | ✓ Done |
| C64 Tools | BASIC keyword abbreviations | ✓ Done | ✓ Done |
| C64 Tools | ASM mnemonics reference panel | ✓ Done | ✓ Done |
| C64 Tools | D64 / D81 disk image browser | ✓ Done | ✓ Done |
| C64 Tools | PETSCII / screen code viewer | Planned | Planned |
| C64 Tools | Sprite editor | Planned | Planned |
| C64 Tools | SID file player (in-app) | Planned | Planned |
| C64 Tools | Cartridge image (.crt) support | Planned | Planned |
| C64 Tools | Tape image (.tap / .t64) support | Planned | Planned |
| **Source Control &amp; Project Management** | | | |
| Source Control | File diff viewer | In Next Release | In Next Release |
| Source Control | Git integration | Planned | Planned |
| Project | Folder / project explorer | ✓ Done | ✓ Done |
| Project | Project file / workspace settings | Planned | Planned |
| **Customization** | | | |
| Customize | Editor settings (font, size, etc.) | ✓ Done | ✓ Done |
| Customize | Color themes | Planned | Planned |
| Customize | Keyboard shortcut customization | Planned | Planned |
| Customize | Extension / plugin system | Possible | Possible |