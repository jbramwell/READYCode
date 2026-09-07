// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Linq;
using ReadyCode.Assembler;

namespace ReadyCode.C64U;

/// <summary>
/// Assembles the small 6502 machine-code stub <see cref="ReadyCode.C64U.C64UDebugSession"/>
/// injects into a running C64 Ultimate to hook the BASIC interpreter's GONE vector
/// ($0308-$0309), since the C64U's REST API has no native breakpoint/memory-watch capability of
/// its own (unlike VICE's binary monitor) - the debugger has to build that capability into the
/// running machine itself.
///
/// The stub runs on every GONE call (once per BASIC statement, per the debugger spec's BASIC
/// indirect vectors table). Two independent checks share that one entry point, both keyed off
/// CURLIN ($39/$3A) - the only piece of this the project has actually verified against a real ROM
/// disassembly (see <see cref="ReadyCode.Vice.ViceDebugSession"/>'s own remarks) - but gated
/// differently:
///
/// - STEP_MODE (Pause/Step Line, which cares about *any* new line) only checks once CURLIN has
///   actually changed value since the last time the stub ran - the "only a genuine new-line
///   dispatch, never a same-line ':' continuation" insight. Its one known blind spot is a program
///   whose entire loop body lives on a single self-looping line (CURLIN never appears to "change"
///   between visits) - an inherently degenerate case for "step to the next line," since there is
///   no other line to step to.
/// - The breakpoint check (which cares about a whole set of specific lines, up to
///   <see cref="MaxBreakpoints"/> of them) instead runs unconditionally on every GONE call,
///   looping over the armed table and comparing CURLIN against each entry regardless of whether
///   CURLIN "changed." This is deliberate: a GOTO back to the very same line rewrites CURLIN with
///   an unchanged value, which the STEP_MODE-style change-detection above can't see at all -
///   gating the breakpoint check the same way silently defeated it on exactly that pattern (a
///   tight `line: ...: GOTO line` main loop, a very common one in real BASIC programs). The
///   trade-off: a breakpoint line containing multiple ':'-separated statements now halts once per
///   statement on that line instead of once per visit, since CURLIN is identical across all of
///   them - an accepted, documented limitation, not a wrong mechanism. (An address-based check
///   against the BASIC execution pointer was tried instead, to avoid that trade-off entirely, but
///   depended on an assumption about that pointer's exact value at the GONE call site that was
///   never independently verified and turned out to be wrong on real hardware - reverted.)
///
/// Unlike an earlier version of this stub, which tracked only a single active breakpoint line and
/// relied on <see cref="C64UDebugSession"/> to guess which one to re-arm after each hit, every
/// enabled breakpoint (up to <see cref="MaxBreakpoints"/>) is now armed simultaneously, matching
/// VICE's per-breakpoint checkpoints. The single-active-breakpoint design's "guess the next line"
/// heuristic broke down as soon as a second breakpoint existed on a line the program didn't
/// reliably revisit before looping back to the first one - it would arm the unreachable one next
/// and just never stop again.
/// </summary>
public static class C64UDebugStub
{
    #region Public Fields

    /// <summary>
    /// The number of breakpoint slots the stub's data area reserves. A fixed-size table keeps the
    /// stub's loop trivial 6502 code (index with X, compare, next) - enabled breakpoints beyond
    /// this many simply aren't armed on the device (see <see cref="C64UDebugSession"/>).
    /// </summary>
    public const int MaxBreakpoints = 8;

    #endregion

    #region Private Fields

    // Written as pure 6502 source (not hand-encoded bytes) so it can be assembled by ReadyCode's
    // own Asm6502Assembler - readable, and any bug is a source-level bug rather than a
    // hand-computed-opcode bug. Labels double as this class's map of data-area addresses (see
    // Assemble's return value) - nothing here is a hardcoded offset.
    //
    // Every RESUME_FLAG write happens twice for a reason: once here at the very top, unconditional
    // on every single GONE call, and once in the WAIT_RESUME epilogue below. The top-of-routine
    // clear exists because ContinueAsync/PauseAsync/StepLineAsync all write RESUME_FLAG=1 from
    // the REST API side, at a moment when the CPU might not even be halted yet (e.g. Pause is
    // sent while the program is running freely, well before any halt condition is reached) - if
    // that byte were left sitting at 1 until the *next* real halt, WAIT_RESUME would see it
    // already set and fall straight through without ever actually giving the poller a chance to
    // observe HALTED_FLAG=$FF, silently defeating Pause/Step/Continue. Clearing it unconditionally
    // at the top of every GONE call means a stale write is wiped out within one statement, long
    // before it could ever reach a real WAIT_RESUME loop.
    private static readonly string _source = $@"
; ── C64U BASIC debug stub ──────────────────────────────────────────────
; Hooked into the GONE vector ($0308-$0309), which the BASIC interpreter jumps through
; indirectly before executing every statement.

START:
    LDA #$00
    STA RESUME_FLAG

    LDA $39
    CMP LAST_LINE_LO
    BNE NEW_LINE
    LDA $3A
    CMP LAST_LINE_HI
    BEQ CHECK_BREAK

NEW_LINE:
    LDA $39
    STA LAST_LINE_LO
    LDA $3A
    STA LAST_LINE_HI

    LDA STEP_MODE
    BNE DO_HALT

CHECK_BREAK:
    ; Unconditional (runs whether or not the CURLIN-change check above just fired) - see class
    ; remarks for why the breakpoint check can't reuse that gate. Loops over every armed slot
    ; (X = 0..BREAK_COUNT-1) instead of checking a single line, so every enabled breakpoint stays
    ; watched at once - see class remarks for why a single watched line wasn't enough.
    LDX #$00

CHECK_BREAK_LOOP:
    CPX BREAK_COUNT
    BCS NOT_NEW_LINE

    LDA $39
    CMP BREAK_LINES_LO,X
    BNE CHECK_BREAK_NEXT
    LDA $3A
    CMP BREAK_LINES_HI,X
    BEQ DO_HALT

CHECK_BREAK_NEXT:
    INX
    JMP CHECK_BREAK_LOOP

DO_HALT:
    LDA #$FF
    STA HALTED_FLAG
    INC HALT_COUNT

WAIT_RESUME:
    LDA RESUME_FLAG
    BEQ WAIT_RESUME
    LDA #$00
    STA RESUME_FLAG
    STA HALTED_FLAG

NOT_NEW_LINE:
    JMP (ORIG_GONE_LO)

; ── Data area ────────────────────────────────────────────────────────────
ORIG_GONE_LO:   .byte 0   ; original $0308 value, saved before the vector is patched
ORIG_GONE_HI:   .byte 0   ; original $0309 value
LAST_LINE_LO:   .byte 0   ; last-seen CURLIN low byte - also the halted line once HALTED_FLAG is set
LAST_LINE_HI:   .byte 0   ; last-seen CURLIN high byte
BREAK_COUNT:    .byte 0   ; number of active entries in BREAK_LINES_LO/HI (0..{MaxBreakpoints})
BREAK_LINES_LO: .byte {ZeroList()} ; armed breakpoint line numbers, low bytes (only the first BREAK_COUNT matter)
BREAK_LINES_HI: .byte {ZeroList()} ; ditto, high bytes
STEP_MODE:      .byte 0   ; $01 = halt at the very next line boundary, ignoring BREAK_LINES
HALTED_FLAG:    .byte 0   ; $FF once halted; C64UDebugSession polls this
RESUME_FLAG:    .byte 0   ; C64UDebugSession writes $01 here to resume
HALT_COUNT:     .byte 0   ; bumped on every halt, even ones a 250ms poll tick could otherwise miss
                          ; entirely if resume-to-next-halt completes faster than one poll interval
";

    #endregion

    #region Private Methods

    // Generates "0,0,0,..." (MaxBreakpoints times) for the BREAK_LINES_LO/HI .byte directives -
    // computed rather than hand-typed so the table can never silently drift out of sync with
    // MaxBreakpoints.
    private static string ZeroList() => string.Join(",", Enumerable.Repeat("0", MaxBreakpoints));

    #endregion

    #region Public Methods

    /// <summary>
    /// Assembles the stub for the given base address.
    /// </summary>
    /// <param name="baseAddress">The address the stub will be uploaded to.</param>
    /// <returns>
    /// The raw code+data bytes to upload (no load-address header - this is written directly via
    /// a memory-write call, not loaded as a runnable .prg), and every named data address
    /// (ORIG_GONE_LO, LAST_LINE_LO, etc.), resolved to real memory addresses relative to
    /// <paramref name="baseAddress"/>.
    /// </returns>
    public static (byte[] Bytes, IReadOnlyDictionary<string, ushort> Labels) Assemble(ushort baseAddress)
    {
        var result = new Asm6502Assembler().Assemble(_source, standaloneOutput: true, defaultOriginAddress: baseAddress);

        if (!result.Success)
        {
            string errors = string.Join("; ", result.Errors.Select(e => $"line {e.LineNumber}: {e.Message}"));
            throw new InvalidOperationException($"Internal error: the C64U debug stub failed to assemble ({errors}).");
        }

        // PrgBytes carries a raw 2-byte load-address header (standaloneOutput's own convention,
        // since there's no BASIC loader stub to imply the origin) - strip it, since the stub is
        // written directly to memory, not loaded as a .prg.
        byte[] bytes = result.PrgBytes![2..];
        return (bytes, result.Labels);
    }

    #endregion
}
