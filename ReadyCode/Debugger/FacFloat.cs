// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Debugger;

/// <summary>
/// Converts between <see cref="double"/> and the Commodore 5-byte floating point format C64
/// BASIC stores float variables in: a 1-byte biased exponent followed by a 4-byte mantissa whose
/// first byte's bit 7 holds the sign (the mantissa's own leading "1" bit is implied/not stored).
/// An all-zero exponent byte is the dedicated encoding for zero.
/// </summary>
public static class FacFloat
{
    #region Public Methods

    /// <summary>
    /// Decodes a 5-byte C64 BASIC float value into a <see cref="double"/>.
    /// </summary>
    public static double Decode(byte exponentByte, byte m1, byte m2, byte m3, byte m4)
    {
        if (exponentByte == 0)
            return 0.0;

        int sign = (m1 & 0x80) != 0 ? -1 : 1;
        uint mantissa = ((uint)((m1 & 0x7F) | 0x80) << 24) | ((uint)m2 << 16) | ((uint)m3 << 8) | m4;
        int exponent = exponentByte - 160;

        return sign * mantissa * Math.Pow(2, exponent);
    }

    /// <summary>
    /// Encodes a <see cref="double"/> into the 5-byte C64 BASIC float format.
    /// </summary>
    /// <exception cref="OverflowException">
    /// The value's magnitude is outside the range a C64 BASIC float can represent.
    /// </exception>
    public static (byte ExponentByte, byte M1, byte M2, byte M3, byte M4) Encode(double value)
    {
        if (value == 0.0)
            return (0, 0, 0, 0, 0);

        bool negative = value < 0;
        double absValue = Math.Abs(value);

        int exponent = (int)Math.Floor(Math.Log2(absValue));
        double normalized = absValue / Math.Pow(2, exponent); // intended to land in [1, 2)

        // Log2/Pow are floating point operations, so guard against rounding pushing the
        // normalized value just outside the [1, 2) range they're meant to land in.
        if (normalized >= 2.0) { normalized /= 2.0; exponent++; }
        else if (normalized < 1.0) { normalized *= 2.0; exponent--; }

        long mantissa = (long)Math.Round(normalized * 0x80000000L);
        if (mantissa >= 0x1_0000_0000L) // rounded up into the next power of two
        {
            mantissa = 0x8000_0000L;
            exponent++;
        }

        int exponentByte = exponent + 129;
        if (exponentByte is < 1 or > 255)
            throw new OverflowException($"The value {value} is outside the range a C64 BASIC float can represent.");

        uint mantissaBits = (uint)mantissa;
        byte m1 = (byte)(((mantissaBits >> 24) & 0x7F) | (negative ? 0x80u : 0x00u));
        byte m2 = (byte)(mantissaBits >> 16);
        byte m3 = (byte)(mantissaBits >> 8);
        byte m4 = (byte)mantissaBits;

        return ((byte)exponentByte, m1, m2, m3, m4);
    }

    #endregion
}
