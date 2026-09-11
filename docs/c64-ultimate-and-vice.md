# Transferring to Hardware and Emulators

Writing a program is only half the point. READYCode can send it straight to a real Commodore 64 Ultimate over your local network, or to the VICE emulator running on your own machine (or another one on the network), and run it immediately, without ever touching a physical disk.

Both integrations are optional. Neither one activates on its own; you configure a target in Preferences and READYCode only talks to it when you explicitly choose to.

## The C64 Ultimate

### Setting it up

![A BASIC listing with REM comments rendered as PETSCII graphics](../images/READYCode-Preferences-Commodore.png)

Enter your C64 Ultimate's URL once in **Preferences > Settings... > Commodore > Commodore 64 Ultimate**. Once set, the **C64U** menu (and the matching options in the editor's right-click menu) become active.

### Loading and running

- **Load** transfers the active program to the C64 Ultimate without running it (no keyboard shortcut of its own - see below).
- **Run Without Debugging** (Ctrl+F5) transfers it and starts it immediately, with no debug session attached.
- **Start Debugging / Continue** (F5) transfers it, arms every enabled breakpoint, and starts it under the debugger - or, if a debug session is already running, resumes it. See [Debugging](debugging.md).

If automatic minification is enabled in Preferences, your program is minified before it is sent, keeping your working copy untouched while sending a compact version to the machine - except when starting a debug session, where minification and line renumbering are skipped so your breakpoints stay lined up with the source you're looking at. See [Minify and Prettify](minify-and-prettify.md).

For a standalone assembly program (Assembly > Assembler's **Output** set to Standalone, or any source with its own `.org`), "starts it immediately" doesn't just mean the device's own load-and-run - there's no BASIC program in memory for that to run. Instead, READYCode loads the program without running it, waits briefly for the machine to finish resetting, then simulates typing `SYS <origin>` and Enter directly into the keyboard buffer, the same trick real loader hardware uses to launch non-BASIC code after a DMA load.

### Machine control

The C64U menu also offers direct machine control: Reset, Reboot, Pause, Resume, and Power Off, plus an **About My C64U** dialog showing device information. These all talk to the Ultimate's own local [REST API](https://1541u-documentation.readthedocs.io/en/latest/api/api_calls.html):

| Action | Endpoint |
| --- | --- |
| Load without running | `POST /v1/runners:load_prg` |
| Load and run | `POST /v1/runners:run_prg` |
| Type a "SYS" command for standalone output (see above) | `PUT /v1/machine:writemem` |
| Device info | `GET /v1/info` |
| Machine control | `PUT /v1/machine:{action}` |
| List drive status | `GET /v1/drives` |
| Mount an image to a drive | `PUT /v1/drives/{id}:mount?image=<path>` |
| Eject a drive | `PUT /v1/drives/{id}:remove` |

### The C64U Explorer

![A BASIC listing with REM comments rendered as PETSCII graphics](../images/READYCode-C64U-Explorer.png)

Alongside the local Folder Explorer, the C64U Explorer browses the Ultimate's own storage (USB drives, internal Flash, and Temp) directly from within READYCode, using the Ultimate's built-in FTP file service. Switch to it from the left activity bar.

Before connecting, enable the FTP file service on the Ultimate itself: **Ultimate menu > Network Services > FTP file service**. READYCode never connects on its own; nothing happens on the network until you open the C64U Explorer and click Connect.

The C64U Explorer supports the same file management, disk image browsing, and disk image authoring as the local Folder Explorer. See [Disk Images](disk-images.md) and [File Management](file-management.md).

### Mounting disk images

Right-click a `.d64` or `.d81` image in the C64U Explorer to mount it to Drive A or Drive B on the Ultimate. A status footer shows what is currently mounted on each drive, with a one-click eject when you are done. You can also expand a disk image in place to see the individual programs on it without mounting it, and open any of them directly in the editor.

## VICE

### Setting it up

![A BASIC listing with REM comments rendered as PETSCII graphics](../images/READYCode-Preferences-VICE.png)

Set the path to your VICE emulator executable (for example `x64sc.exe`) in **Preferences > Settings... > Commodore > VICE Emulator**, along with the binary monitor host and port (default `127.0.0.1:6502`). Once set, the **VICE** menu becomes active.

VICE integration works differently from the C64 Ultimate's REST API: READYCode both launches and manages the VICE process directly, and talks to VICE's binary monitor protocol over TCP to load and run programs without restarting the emulator each time.

### Loading and running

- **Load** transfers the active program to VICE without running it (no keyboard shortcut of its own - see below).
- **Run Without Debugging** (Ctrl+Alt+F5) transfers it and starts it immediately, with no debug session attached.
- **Start Debugging / Continue** (Alt+F5) transfers it, arms every enabled breakpoint, and starts it under the debugger - or, if a debug session is already running, resumes it. See [Debugging](debugging.md).

The same standalone-program problem described above for the C64 Ultimate applies to VICE too: its autostart just runs whatever BASIC program ends up in memory, and a standalone assembly program has none. READYCode works around it the same way here - loading the program without autostarting, waiting briefly, then feeding `SYS <origin>` and Enter directly into VICE's keyboard buffer through its binary monitor protocol, rather than VICE's own autostart command.

An option in Preferences can bring the VICE window to the foreground automatically whenever you load or run a program.

### Machine control

The VICE menu mirrors the C64U menu: Reset, Reboot, Pause, Resume, Power Off, and an **About VICE** dialog with version information.

## Choosing a target from the editor

Right-click inside the editor to load or run on either target without using the menu bar: the context menu's Load and Run submenus list C64U and VICE side by side. The keyboard shortcuts follow the same pattern throughout the app, including [debugging](debugging.md): the unmodified/Ctrl-modified F-keys (F5, Ctrl+F5, F9, F10, F11, ...) target the C64 Ultimate, and the same keys with Alt added target VICE.

The same Load and Run submenus are also available by right-clicking a `.prg`, `.asm`/`.s`, or machine-language file directly in either Explorer tree - the local Folder Explorer or the C64U Explorer - so you can send a file to hardware or an emulator without opening it first. READYCode works out on its own whether the file needs a typed `SYS` command (as described above) or can autostart normally.

## Disassembling live memory

The **C64U** and **VICE** menus each include a **Disassemble at...** command that reads a block of memory directly from the running machine or emulator, starting at an address you provide, and opens it as a read-only, address-annotated 6502 disassembly tab - useful for inspecting code you don't have the source for, or checking what actually ended up in memory after a POKE. To disassemble a file on disk instead of live memory, see [Disassembling machine code](assembly-editor.md#disassembling-machine-code).

---

[Back to Documentation Home](README.md)
