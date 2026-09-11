# The BASIC Editor

READYCode's BASIC editor is built to feel like a modern code editor while staying true to what a real Commodore 64 actually stores and displays. This page walks through the editing features available on a BASIC (`.prg`) tab.

## Syntax highlighting

Keywords, `REM` comments, string literals, numeric literals, and line numbers are each colored separately, so a program's structure is easy to scan at a glance. Highlighting respects context: text inside a string literal or after `REM` is never mistaken for code, and a `DATA` statement's contents are treated as literal data rather than executable code, matching how a real C64 would interpret them.

## Keyword completion

As you type, READYCode suggests the rest of a BASIC keyword as inline "ghost text" ahead of your cursor, rather than a dropdown list. Press Tab to accept the suggestion, or keep typing to ignore it. Hovering over any keyword or variable also shows a short description in a tooltip.

## Keyword shortcuts

READYCode recognizes the same keyboard shortcuts a real C64 keyboard produces for many BASIC keywords: type the first letter or two of a keyword normally, then hold Shift for the final letter, and the abbreviation is inserted using the correct PETSCII graphic character, exactly as it would appear on real hardware. For example, `GOTO` can be typed as an unshifted `G` followed by a shifted `O`, and `POKE` as an unshifted `P` followed by a shifted `O`.

These abbreviations are recognized everywhere a keyword would be: by the tokenizer when you save or run your program, by syntax highlighting, and by hover tooltips. About fifty keywords have a shortcut; the rest need to be typed in full. If you are not sure which keywords have one, PRINT's is the easiest to remember: a bare `?` always works as a shorthand for `PRINT`.

## PETSCII-accurate rendering

![A BASIC listing with REM comments rendered as PETSCII graphics](../images/READYCode-LEGO-Batman-Easter-Egg.png)

The C64's character set (PETSCII) does not map cleanly onto ASCII: control codes like clear-screen or cursor-down, and the graphic characters produced by holding Shift, all have specific glyphs on a real machine. READYCode renders these using the actual C64 character ROM glyphs (via an embedded "Pet Me 64" font), so a `REM` comment or `PRINT` statement containing control characters looks in the editor exactly as it would look when listed on a real C64, or on a printed page. The underlying text is never altered by this, so features like search and tokenizing keep working normally.

## Upper/Lower Case Mode

The C64 keyboard has two charsets: the default, where unshifted letters type as upper case and Shift or Caps Lock produces the PETSCII graphic character for that key ("Upper Active"), and an alternate one where unshifted letters type as lower case and Shift or Caps Lock gives you upper case instead ("Upper Inactive"). **Edit > Lower Case Mode** (Ctrl+Shift+L) toggles between them for the active tab; so does clicking the Shift Badge (an arrow icon) in the status bar - filled for the default Upper Active charset, outlined for Upper Inactive. The setting is per-tab and only applies to an ordinary editable BASIC tab, dimming out for a Hex Editor tab, a read-only File Compare tab, or an assembly tab, where it has no effect. The status bar also shows a Caps Lock indicator next to the Shift Badge; clicking it toggles the real Windows Caps Lock state, since Caps Lock (like Shift) affects which PETSCII character a key produces.

## The PETSCII Reference panel

Open the PETSCII Reference panel from the right activity bar to browse every PETSCII control character, the sixteen color codes, and the function-key codes. Clicking an entry inserts it at the cursor, and hovering over one shows its name and PETSCII value. The **Quick Keys** panel next to it offers the same set of characters as keyboard shortcuts (Ctrl+1 through Ctrl+8 for screen controls, Ctrl+Shift+1 through Ctrl+Shift+Alt+8 for colors, Shift+F1 through Shift+F8 for function keys) and as clickable buttons, for whichever workflow you prefer.

## Line numbers

READYCode can automatically manage BASIC line numbers so you do not have to keep track of them by hand:

- **Auto-numbering** inserts the next line number automatically when you press Enter at the end of a line. The increment is configurable in Preferences.
- **Zero-padding** keeps line numbers a consistent width (for example `0010` instead of `10`) if you prefer that style.
- **Renumber Code** (Ctrl+R, or Edit menu) renumbers every line in the program sequentially and automatically fixes up every `GOTO`, `GOSUB`, `THEN`, `RESTORE`, and `RUN` reference to match.

See [Minify and Prettify](minify-and-prettify.md) for the fuller renumbering options available in those dialogs, including a configurable start and increment.

## Code folding

Multi-line `REM` comment blocks and `FOR`/`NEXT` loops can be collapsed to reduce visual clutter in longer programs. Folding can be turned off entirely in **Preferences > Settings... > BASIC > Code Analysis**.

## Diagnostics

READYCode checks your program for common mistakes as you type and flags them with a squiggle underline, the same way a modern IDE flags a syntax error. It catches duplicate line numbers, `GOTO`/`GOSUB`/`THEN` targets that do not exist, unmatched `FOR`/`NEXT` pairs, and unterminated string literals. Hover over a squiggle to see the specific problem. This can be turned off in **Preferences > Settings... > BASIC > Code Analysis** if you would rather not see it.

## The Errors panel

**View > Errors Panel** opens a bottom panel listing every current diagnostic across every open tab (not just the active one), styled like a modern IDE's error list: a severity icon, description, file name, and line number per row, with a live count and a search box to filter by message or file. Double-click a row to jump straight to that file and line, opening the tab if it is not already active. The panel opens automatically - switching to its Errors tab if needed - right after a Save, or a Load/Run on the C64 Ultimate or VICE, whenever that action produced at least one diagnostic; it does not interrupt you by popping open on every keystroke while you are mid-edit. It shares the same bottom-panel space as the Debug panel (**View > Debug Panel**) - opening one switches to it without losing the other's contents, and both remember the height you last resized the panel to.

## The Variables panel

The Variables panel, below the primary side bar, lists every variable used in the active BASIC program. Expanding a variable shows every line where it is read or written, and clicking an occurrence jumps straight to it. Press F2 on a variable to rename it everywhere it appears in one step.

## The BASIC Keywords panel

A reference panel, available from the right activity bar, listing every BASIC keyword along with a description of what it does. Useful for browsing what is available without leaving the editor.

## The Music Notes panel

A SID music note reference grid, also available from the right activity bar, for looking up note values when writing music or sound routines that POKE the SID chip directly.

## Editing commands

A few commands specific to working with BASIC source:

- **Comment Selection** / **Uncomment Selection** (Ctrl+K Ctrl+C / Ctrl+K Ctrl+U) wraps or unwraps the selected lines in `REM`.
- **Make Uppercase** / **Make Lowercase** (Ctrl+Shift+U and its counterpart) converts the case of selected text in bulk.
- **Go to Line...** (Ctrl+G) jumps to a specific line number.
- **Go to Definition** (F12), with your cursor on a line-number reference inside a `GOTO`, `GOSUB`, or `THEN`, jumps directly to that line.

## Code Statistics

![The Code Statistics dialog](../images/READYCode-Code-Statistics.png)

**View > Code Statistics** opens a dialog showing character, word, and line counts for the active document, along with the tokenized byte count and how much of the C64's roughly 38,911-byte BASIC memory it would use.

---

[Back to Documentation Home](README.md)
