# Debugging BASIC Programs

READYCode has a real source-level debugger for BASIC programs, working against a live C64 Ultimate or VICE: set breakpoints in the editor gutter, step through your program line by line, and inspect variables and the GOSUB call stack while it's paused - all without leaving the editor. This page covers how it works; see [Transferring to Hardware and Emulators](c64-ultimate-and-vice.md) for setting up a C64 Ultimate or VICE target in the first place.

## Starting a debug session

Both the **C64U** and **VICE** menus separate plain running from debugging:

- **Run Without Debugging** (Ctrl+F5 for C64U, Ctrl+Alt+F5 for VICE) transfers and runs the program with no debugger attached, exactly as before the debugger existed.
- **Start Debugging / Continue** (F5 for C64U, Alt+F5 for VICE) transfers the program, arms every currently-enabled breakpoint, and starts it under the debugger. If a debug session is already running and paused, the same shortcut resumes it instead of starting a new one.

Starting a debug session does not require a connection to already be open - it creates its own connection using whatever C64 Ultimate URL or VICE path/port is configured in Preferences, the same as a plain Run. Your program is transferred exactly as written: automatic minification and line renumbering are skipped for a debug session specifically so your breakpoints stay lined up with the source you're looking at.

## Breakpoints

Click in the narrow margin to the left of the line numbers on any BASIC line to set a breakpoint there - click again to remove it. A solid red dot marks an enabled breakpoint; a hollow grey ring marks a disabled one.

Keyboard shortcuts act on whichever line the caret is on:

| Shortcut (C64U / VICE) | Action |
| --- | --- |
| F9 / Alt+F9 | Toggle Breakpoint (set or remove) |
| Ctrl+F9 / Ctrl+Alt+F9 | Enable/Disable Breakpoint (keeps it, without removing it) |
| Ctrl+Shift+F9 / Ctrl+Alt+Shift+F9 | Delete All Breakpoints |

Breakpoints are stored per file and line, independent of which tabs currently happen to be open, so they survive closing and reopening a file.

## The Debug Panel

**View > Debug Panel** opens a bottom panel with three sub-tabs:

- **Variables** - every variable in the running program, with its name, type, and current value. Double-click a value to edit it directly in the machine's live memory; the grid re-reads the true resulting value afterward rather than just showing back what you typed. String values render through the same PETSCII glyph mapping as the editor, so control characters display correctly instead of as garbage. This grid only reflects the paused state - it's refreshed when execution stops and cleared again when you resume, not updated live while the program is running.
- **Breakpoints** - every breakpoint across every file, with an "On" checkbox, file name, and line number. Double-click a row to jump to that breakpoint's location in the editor.
- **Call Stack** - the current GOSUB nesting, each frame showing which line it will return to. Double-click a frame to jump to its return line. (Not available on the C64 Ultimate - see below.)

## Stepping and running

BASIC doesn't have function calls in the modern sense, only `GOSUB`, but READYCode models stepping the same way a modern debugger would:

| Shortcut (C64U / VICE) | Action |
| --- | --- |
| F5 / Alt+F5 | Start Debugging / Continue - resumes a paused session |
| F6 / Alt+F6 | Pause |
| F10 / Alt+F10 | Step Over - runs a `GOSUB` to completion without stopping inside it (VICE only) |
| F11 / Alt+F11 | Step Into - stops at the next line, entering a `GOSUB` if one is called |
| Shift+F11 / Alt+Shift+F11 | Step Out - runs until the current `GOSUB` returns (VICE only) |
| Ctrl+F10 / Ctrl+Alt+F10 | Run to Cursor - sets a one-time breakpoint at the caret's line |
| Shift+F5 / Alt+Shift+F5 | Stop Debugging |
| Ctrl+Shift+F5 / Ctrl+Alt+Shift+F5 | Restart Debugging (Stop, then Start) |

Stopping a debug session detaches cleanly without resetting the machine, so whatever was on screen stays there.

## Visual feedback while debugging

The line execution is currently paused on is highlighted with a translucent gold bar across the whole line, and the status bar shows messages like "Stopped at line 100 (breakpoint)." while paused and "Running…" once resumed, so you always know the debugger's state at a glance.

## C64 Ultimate vs VICE

Debugging works on both targets, but the C64 Ultimate's local REST API has no way to read CPU registers (including the stack pointer), so a few things aren't available there:

- The **Call Stack** panel stays empty on a C64 Ultimate session.
- **Step Over** and **Step Out** are both disabled - both need to track the call stack to know when a `GOSUB` has returned. Only **Step Into** works on the C64 Ultimate; on VICE, all three are available.

Under the hood the two targets implement breakpoints completely differently: VICE uses its real binary monitor protocol to watch memory for BASIC's own "current line" variable changing to one of your breakpoint lines; the C64 Ultimate's REST API has no such capability, so READYCode instead installs a small debug stub in the running BASIC interpreter and polls its status every 250ms. Both are transparent from the UI - breakpoints, stepping, and the Variables panel behave the same way regardless of which one you're using.

---

[Back to Documentation Home](README.md)
