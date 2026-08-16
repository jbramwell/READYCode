// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Debugger;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="VariableTableParser"/>. Fixture bytes follow the verified format: every
/// entry is a uniform 7 bytes (2 name + 5 data), integer = both name bytes | $80, string = only
/// the second name byte | $80, float = neither flagged.
/// </summary>
public class VariableTableParserTests
{
    #region Public Methods

    [Fact]
    public void ParseSimpleVariables_FloatEntry_DecodesNameAndValue()
    {
        // "X" = 3.0 -> name 'X' (0x58), second name byte 0x00 (single-char, no flags),
        // then FacFloat.Encode(3.0) = 82 40 00 00 00.
        byte[] memory = [0x58, 0x00, 0x82, 0x40, 0x00, 0x00, 0x00];

        var variables = VariableTableParser.ParseSimpleVariables(memory, vartabAddress: 0x1000, arytabAddress: 0x1007);

        var v = Assert.Single(variables);
        Assert.Equal("X", v.Name);
        Assert.Equal(BasicVariableType.Float, v.Type);
        Assert.Equal(3.0, (double)v.Value, precision: 9);
        Assert.Equal(0x1002, v.ValueAddress);
    }

    [Fact]
    public void ParseSimpleVariables_IntegerEntry_DecodesBigEndianValue()
    {
        // "NL%" = 1234 -> name 'N'|$80 (0xCE), 'L'|$80 (0xCC), value 1234 = 0x04D2 big-endian,
        // padded to the full 5-byte data area.
        byte[] memory = [0xCE, 0xCC, 0x04, 0xD2, 0x00, 0x00, 0x00];

        var variables = VariableTableParser.ParseSimpleVariables(memory, vartabAddress: 0x2000, arytabAddress: 0x2007);

        var v = Assert.Single(variables);
        Assert.Equal("NL%", v.Name);
        Assert.Equal(BasicVariableType.Integer, v.Type);
        Assert.Equal((short)1234, (short)v.Value);
    }

    [Fact]
    public void ParseSimpleVariables_NegativeIntegerEntry_DecodesTwosComplement()
    {
        byte[] memory = [0xCE, 0xCC, 0xFF, 0xFF, 0x00, 0x00, 0x00]; // -1 as big-endian two's complement

        var variables = VariableTableParser.ParseSimpleVariables(memory, vartabAddress: 0x2000, arytabAddress: 0x2007);

        Assert.Equal((short)(-1), (short)Assert.Single(variables).Value);
    }

    [Fact]
    public void ParseSimpleVariables_StringEntry_DecodesLengthAndPointer()
    {
        // "A$" -> name 'A' (0x41, no flag - only the SECOND byte flags a string), $80 (no
        // second character), length 5, pointer $1234 (LE), padded to the full 5-byte data area.
        byte[] memory = [0x41, 0x80, 0x05, 0x34, 0x12, 0x00, 0x00];

        var variables = VariableTableParser.ParseSimpleVariables(memory, vartabAddress: 0x3000, arytabAddress: 0x3007);

        var v = Assert.Single(variables);
        Assert.Equal("A$", v.Name);
        Assert.Equal(BasicVariableType.String, v.Type);
        var descriptor = Assert.IsType<StringDescriptor>(v.Value);
        Assert.Equal(5, descriptor.Length);
        Assert.Equal(0x1234, descriptor.HeapPointer);
    }

    [Fact]
    public void ParseSimpleVariables_MixOfAllThreeTypes_ParsesEachAtCorrectOffset()
    {
        byte[] memory =
        [
            0x58, 0x00, 0x82, 0x40, 0x00, 0x00, 0x00, // X (float)
            0xCE, 0xCC, 0x04, 0xD2, 0x00, 0x00, 0x00, // NL% (integer)
            0x41, 0x80, 0x05, 0x34, 0x12, 0x00, 0x00, // A$ (string)
        ];

        var variables = VariableTableParser.ParseSimpleVariables(memory, vartabAddress: 0x1000, arytabAddress: (ushort)(0x1000 + memory.Length));

        Assert.Equal(3, variables.Count);
        Assert.Equal("X", variables[0].Name);
        Assert.Equal("NL%", variables[1].Name);
        Assert.Equal("A$", variables[2].Name);
    }

    [Fact]
    public void ParseSimpleVariables_TwoCharacterFloatName_ConcatenatesBothCharacters()
    {
        // "SC" (float) -> 'S' (0x53), 'C' (0x43) - neither byte flagged, even though 'S' and
        // 'C' both happen to have bit 6 set in their own ASCII codes (only bit 7 is a type flag).
        byte[] memory = [0x53, 0x43, 0x82, 0x40, 0x00, 0x00, 0x00];

        var v = Assert.Single(VariableTableParser.ParseSimpleVariables(memory, 0x1000, 0x1007));
        Assert.Equal("SC", v.Name);
    }

    [Fact]
    public void ParseSimpleVariables_TwoCharacterIntegerName_StripsFlagBitsFromBothCharacters()
    {
        // "AB%" -> 'A'|$80 (0xC1), 'B'|$80 (0xC2).
        byte[] memory = [0xC1, 0xC2, 0x00, 0x01, 0x00, 0x00, 0x00];

        var v = Assert.Single(VariableTableParser.ParseSimpleVariables(memory, 0x1000, 0x1007));
        Assert.Equal("AB%", v.Name);
    }

    [Fact]
    public void ParseSimpleVariables_EmptyTable_ReturnsNoVariables()
    {
        var variables = VariableTableParser.ParseSimpleVariables(Array.Empty<byte>(), vartabAddress: 0x1000, arytabAddress: 0x1000);
        Assert.Empty(variables);
    }

    [Fact]
    public void ParseSimpleVariables_TruncatedSnapshot_StopsWithoutThrowing()
    {
        // Claims one 7-byte entry, but the snapshot only has 4 bytes.
        byte[] memory = [0x58, 0x00, 0x82, 0x40];

        var variables = VariableTableParser.ParseSimpleVariables(memory, vartabAddress: 0x1000, arytabAddress: 0x1007);

        Assert.Empty(variables); // the incomplete entry can't be parsed, and there's nothing before it
    }

    #endregion
}
