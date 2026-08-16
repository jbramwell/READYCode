// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Debugger;

/// <summary>
/// A single GOSUB frame on the call stack: the line number execution will resume at once the
/// matching RETURN executes, and (if resolvable) the editor line to jump to for it.
/// </summary>
public sealed record GosubFrame(ushort ReturnLineNumber, int? DocumentLine);

/// <summary>
/// Reads C64 BASIC's GOSUB call stack from a snapshot of the 6502 stack page ($0100-$01FF).
/// BASIC pushes a self-describing frame for both GOSUB and FOR onto the same hardware stack,
/// each ending (i.e. pushed last, so it's the first byte encountered walking up from the stack
/// pointer) with a single marker byte equal to that statement's own BASIC token - $8D for GOSUB,
/// $81 for FOR - which is how RETURN/NEXT (and this parser) tell the two kinds of frame apart
/// without needing a separate discriminator field.
/// </summary>
public static class GosubStackParser
{
    #region Private Fields

    // GOSUB's own BASIC token byte, confirmed as the frame marker: RETURN checks for exactly
    // this byte on top of stack before popping the rest of the frame, erroring
    // "RETURN WITHOUT GOSUB" if it's anything else.
    private const byte _gosubMarker = 0x8D;

    // marker(1) + return line number(2) + return text pointer(2), marker on top (pushed last).
    private const int _gosubFrameSize = 5;

    // FOR's own BASIC token byte, by the same marker-is-the-statement's-own-token convention as
    // GOSUB above.
    private const byte _forMarker = 0x81;

    // NEEDS VERIFICATION: this is the best available estimate (marker + loop variable pointer +
    // TO/STEP as 5-byte floats + a return text pointer + line number), not confirmed against a
    // ROM disassembly or a live system - unlike the GOSUB frame layout above, which is. If this
    // is wrong, any stack with an active FOR loop will misalign the walk from that point on and
    // produce garbage for every frame above it. Verify against a live VICE session with a
    // GOSUB-inside-FOR-loop test program before relying on this in production.
    private const int _forFrameSizeUnverified = 18;

    #endregion

    #region Public Methods

    /// <summary>
    /// Parses every GOSUB frame currently on the stack, innermost (most recently called) first.
    /// </summary>
    /// <param name="stackPageBytes">
    /// The full 256-byte stack page ($0100-$01FF), with <c>stackPageBytes[0]</c> corresponding
    /// to address $0100.
    /// </param>
    /// <param name="stackPointer">The 6502 stack pointer (SP) at the moment of the snapshot.</param>
    /// <param name="lineTable">
    /// Used to resolve each frame's return line number to an editor line, or null to leave
    /// every frame's <see cref="GosubFrame.DocumentLine"/> unset.
    /// </param>
    /// <returns>
    /// The GOSUB frames found. Stops at the first byte that isn't a recognized GOSUB/FOR marker
    /// (the natural end of BASIC's stack usage, below which lies unrelated KERNAL/interrupt
    /// stack content) rather than risk misinterpreting further bytes.
    /// </returns>
    public static IReadOnlyList<GosubFrame> Parse(byte[] stackPageBytes, byte stackPointer, BasicLineAddressTable? lineTable)
    {
        var frames = new List<GosubFrame>();

        // The stack pointer holds the address of the next free slot; the topmost pushed byte
        // (each frame's marker, since it's pushed last) is one above that.
        int offset = stackPointer + 1;

        while (offset < stackPageBytes.Length)
        {
            byte marker = stackPageBytes[offset];

            if (marker == _gosubMarker)
            {
                if (offset + _gosubFrameSize > stackPageBytes.Length) break;

                ushort lineNumber = (ushort)(stackPageBytes[offset + 1] | (stackPageBytes[offset + 2] << 8));
                int? documentLine = lineTable != null && lineTable.BasicLineToDocumentLine.TryGetValue(lineNumber, out int line)
                    ? line
                    : null;

                frames.Add(new GosubFrame(lineNumber, documentLine));
                offset += _gosubFrameSize;
            }
            else if (marker == _forMarker)
            {
                offset += _forFrameSizeUnverified;
            }
            else
            {
                break;
            }
        }

        return frames;
    }

    #endregion
}
