// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Sid;

/// <summary>
/// A single row of the SID chip's oscillator frequency table for a musical note,
/// giving the 16-bit oscillator frequency value (as decimal and hi/lo bytes) for
/// both NTSC and PAL C64s.
/// </summary>
public class SidNote
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SidNote"/> class.
    /// </summary>
    /// <param name="note">The note index (0-based, skipping unused codes within each octave).</param>
    /// <param name="octave">The note name and octave, e.g. "C#-0".</param>
    /// <param name="decimalNtsc">The 16-bit oscillator frequency value on NTSC systems.</param>
    /// <param name="hiNtsc">The high byte of the NTSC oscillator frequency.</param>
    /// <param name="lowNtsc">The low byte of the NTSC oscillator frequency.</param>
    /// <param name="decimalPal">The 16-bit oscillator frequency value on PAL systems, or null if the note
    /// is out of PAL's representable range.</param>
    /// <param name="hiPal">The high byte of the PAL oscillator frequency, or null if not applicable.</param>
    /// <param name="lowPal">The low byte of the PAL oscillator frequency, or null if not applicable.</param>
    public SidNote(int note, string octave, int decimalNtsc, int hiNtsc, int lowNtsc,
        int? decimalPal, int? hiPal, int? lowPal)
    {
        Note = note;
        Octave = octave;
        DecimalNtsc = decimalNtsc;
        HiNtsc = hiNtsc;
        LowNtsc = lowNtsc;
        DecimalPal = decimalPal;
        HiPal = hiPal;
        LowPal = lowPal;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the note index.
    /// </summary>
    public int Note { get; }

    /// <summary>
    /// Gets the note name and octave, e.g. "C#-0".
    /// </summary>
    public string Octave { get; }

    /// <summary>
    /// Gets the 16-bit oscillator frequency value on NTSC systems.
    /// </summary>
    public int DecimalNtsc { get; }

    /// <summary>
    /// Gets the high byte of the NTSC oscillator frequency.
    /// </summary>
    public int HiNtsc { get; }

    /// <summary>
    /// Gets the low byte of the NTSC oscillator frequency.
    /// </summary>
    public int LowNtsc { get; }

    /// <summary>
    /// Gets the 16-bit oscillator frequency value on PAL systems, or null if the note is out
    /// of PAL's representable range.
    /// </summary>
    public int? DecimalPal { get; }

    /// <summary>
    /// Gets the high byte of the PAL oscillator frequency, or null if not applicable.
    /// </summary>
    public int? HiPal { get; }

    /// <summary>
    /// Gets the low byte of the PAL oscillator frequency, or null if not applicable.
    /// </summary>
    public int? LowPal { get; }

    #endregion
}
