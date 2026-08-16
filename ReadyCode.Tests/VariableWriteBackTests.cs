// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Debugger;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="VariableWriteBack"/>.
/// </summary>
public class VariableWriteBackTests
{
    #region Public Methods

    // ── Float ────────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_Float_ProducesFacFloatBytesAtValueAddress()
    {
        var variable = new BasicVariable("X", BasicVariableType.Float, 0.0, ValueAddress: 0x1234);
        var (address, bytes) = VariableWriteBack.Encode(variable, "3");

        Assert.Equal(0x1234, address);
        Assert.Equal(FacFloat.Encode(3.0), (bytes[0], bytes[1], bytes[2], bytes[3], bytes[4]));
    }

    [Fact]
    public void Encode_Float_InvalidText_Throws()
    {
        var variable = new BasicVariable("X", BasicVariableType.Float, 0.0, ValueAddress: 0x1234);
        Assert.Throws<FormatException>(() => VariableWriteBack.Encode(variable, "not a number"));
    }

    // ── Integer ──────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_Integer_ProducesBigEndianBytes()
    {
        var variable = new BasicVariable("N%", BasicVariableType.Integer, (short)0, ValueAddress: 0x2000);
        var (address, bytes) = VariableWriteBack.Encode(variable, "1234");

        Assert.Equal(0x2000, address);
        Assert.Equal(new byte[] { 0x04, 0xD2 }, bytes);
    }

    [Fact]
    public void Encode_Integer_NegativeValue_ProducesTwosComplement()
    {
        var variable = new BasicVariable("N%", BasicVariableType.Integer, (short)0, ValueAddress: 0x2000);
        var (_, bytes) = VariableWriteBack.Encode(variable, "-1");

        Assert.Equal(new byte[] { 0xFF, 0xFF }, bytes);
    }

    [Fact]
    public void Encode_Integer_OutOfRange_Throws()
    {
        var variable = new BasicVariable("N%", BasicVariableType.Integer, (short)0, ValueAddress: 0x2000);
        Assert.Throws<FormatException>(() => VariableWriteBack.Encode(variable, "99999"));
    }

    // ── String ───────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_String_SameLength_WritesToHeapPointer()
    {
        var current = new ResolvedStringValue("HELLO", HeapPointer: 0x3000);
        var variable = new BasicVariable("A$", BasicVariableType.String, current, ValueAddress: 0x2500);

        var (address, bytes) = VariableWriteBack.Encode(variable, "WORLD");

        Assert.Equal(0x3000, address); // the heap pointer, not the descriptor's own address
        Assert.Equal("WORLD"u8.ToArray(), bytes);
    }

    [Fact]
    public void Encode_String_Shorter_PadsWithSpaces()
    {
        var current = new ResolvedStringValue("HELLO", HeapPointer: 0x3000);
        var variable = new BasicVariable("A$", BasicVariableType.String, current, ValueAddress: 0x2500);

        var (_, bytes) = VariableWriteBack.Encode(variable, "HI");

        Assert.Equal("HI   "u8.ToArray(), bytes);
    }

    [Fact]
    public void Encode_String_Longer_Throws()
    {
        var current = new ResolvedStringValue("HI", HeapPointer: 0x3000);
        var variable = new BasicVariable("A$", BasicVariableType.String, current, ValueAddress: 0x2500);

        Assert.Throws<InvalidOperationException>(() => VariableWriteBack.Encode(variable, "HELLO"));
    }

    [Fact]
    public void Encode_String_StripsSurroundingQuotesFromDisplayFormat()
    {
        var current = new ResolvedStringValue("HELLO", HeapPointer: 0x3000);
        var variable = new BasicVariable("A$", BasicVariableType.String, current, ValueAddress: 0x2500);

        var (_, bytes) = VariableWriteBack.Encode(variable, "\"WORLD\"");

        Assert.Equal("WORLD"u8.ToArray(), bytes);
    }

    [Fact]
    public void Encode_String_NotYetResolved_Throws()
    {
        var variable = new BasicVariable("A$", BasicVariableType.String, new StringDescriptor(5, 0x3000), ValueAddress: 0x2500);
        Assert.Throws<InvalidOperationException>(() => VariableWriteBack.Encode(variable, "HI"));
    }

    #endregion
}
