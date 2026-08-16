// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Debugger;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="FacFloat"/>.
/// </summary>
public class FacFloatTests
{
    #region Public Methods

    [Fact]
    public void Decode_CanonicalPiEncoding_ProducesPi()
    {
        // The well-documented Commodore encoding of pi. Only accurate to the ~9.3 decimal
        // digits a 32-bit mantissa can hold for a value of this magnitude (the stored ROM
        // constant and a fresh nearest-rounding of IEEE double pi can differ in their very
        // last mantissa bit - that's expected, not a bug, so this checks 8 decimal places
        // rather than demanding bit-for-bit agreement with a fresh re-derivation).
        double value = FacFloat.Decode(0x82, 0x49, 0x0F, 0xDA, 0xA1);
        Assert.Equal(Math.PI, value, precision: 8);
    }

    [Fact]
    public void Decode_AllZeroBytes_IsZero()
    {
        Assert.Equal(0.0, FacFloat.Decode(0, 0, 0, 0, 0));
    }

    [Fact]
    public void Decode_NegativeExample_SignBitFlipsResult()
    {
        double positive = FacFloat.Decode(0x82, 0x49, 0x0F, 0xDA, 0xA1);
        double negative = FacFloat.Decode(0x82, 0xC9, 0x0F, 0xDA, 0xA1); // m1 bit 7 set
        Assert.Equal(-positive, negative, precision: 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(Math.PI)]
    [InlineData(-Math.PI)]
    [InlineData(3.0)]
    [InlineData(0.5)]
    [InlineData(1250.0)]
    [InlineData(-32768.0)]
    [InlineData(0.1)]
    [InlineData(123456789.0)]
    [InlineData(1e-10)]
    [InlineData(1e10)]
    public void EncodeThenDecode_RoundTrips(double value)
    {
        var (exp, m1, m2, m3, m4) = FacFloat.Encode(value);
        double result = FacFloat.Decode(exp, m1, m2, m3, m4);
        Assert.Equal(value, result, precision: 6);
    }

    [Fact]
    public void Encode_ExactlyRepresentableValue_MatchesHandComputedBytes()
    {
        // 3.0 has no rounding ambiguity (unlike an irrational value such as pi, where a fresh
        // nearest-rounding of the IEEE double can legitimately differ from a decades-old ROM
        // constant in its final mantissa bit), so this is safe to assert byte-for-byte.
        var (exp, m1, m2, m3, m4) = FacFloat.Encode(3.0);
        Assert.Equal((0x82, 0x40, 0x00, 0x00, 0x00), (exp, m1, m2, m3, m4));
    }

    [Fact]
    public void Encode_Zero_ProducesAllZeroBytes()
    {
        var (exp, m1, m2, m3, m4) = FacFloat.Encode(0.0);
        Assert.Equal((0, 0, 0, 0, 0), (exp, m1, m2, m3, m4));
    }

    [Fact]
    public void Encode_NegativeValue_SetsSignBit()
    {
        var (_, m1, _, _, _) = FacFloat.Encode(-3.0);
        Assert.Equal(0x80, m1 & 0x80);
    }

    [Fact]
    public void Encode_MagnitudeTooLarge_Throws()
    {
        Assert.Throws<OverflowException>(() => FacFloat.Encode(1e60));
    }

    #endregion
}
