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

    // ── TryParseArrayHeader ──────────────────────────────────────────────────
    // Fixture bytes follow the array table format: 2 name bytes (same type-flag convention as a
    // simple variable), a 2-byte BIG-endian total entry length, a 1-byte dimension count, then
    // that many 2-byte BIG-endian dimension sizes (element count along that axis - DIM N -> N+1) -
    // big-endian, not little-endian like a pointer, because these are BASIC-computed 16-bit
    // numeric values, the same convention ParseSimpleVariables' own integer-value tests already
    // use (confirmed against real hardware: a 30x30 DIM originally misread as 7936 here before
    // this was corrected). TryParseArrayHeader stops there - see ParseArrayElements for the
    // element data after it.

    [Fact]
    public void TryParseArrayHeader_OneDimensionalFloatArray_ParsesShapeAndDataOffset()
    {
        // DIM A(2) -> dimension size 3. Header = 2 name + 2 length + 1 dimcount + 2 dim-size = 7
        // bytes; entry length = 7-byte header + 3*5-byte data = 22.
        byte[] memory = [0x41, 0x00, 0x00, 0x16, 0x01, 0x00, 0x03];

        var header = VariableTableParser.TryParseArrayHeader(memory, 0);

        Assert.NotNull(header);
        Assert.Equal("A", header.Name);
        Assert.Equal(BasicVariableType.Float, header.ElementType);
        Assert.Equal([3], header.DimensionSizes);
        Assert.Equal(22, header.EntryLength);
        Assert.Equal(7, header.DataOffset);
    }

    [Fact]
    public void TryParseArrayHeader_TwoDimensionalIntegerArray_ParsesBothDimensionSizes()
    {
        // DIM B%(1,2) -> dimension sizes 2 and 3.
        byte[] memory = [0xC2, 0x80, 0x00, 0x15, 0x02, 0x00, 0x02, 0x00, 0x03];

        var header = VariableTableParser.TryParseArrayHeader(memory, 0);

        Assert.NotNull(header);
        Assert.Equal("B%", header.Name);
        Assert.Equal(BasicVariableType.Integer, header.ElementType);
        Assert.Equal([2, 3], header.DimensionSizes);
        Assert.Equal(9, header.DataOffset); // 2 name + 2 length + 1 dimcount + 2*2 dim-sizes
    }

    [Fact]
    public void TryParseArrayHeader_StringArray_ParsesElementTypeString()
    {
        byte[] memory = [0x43, 0x80, 0x00, 0x0D, 0x01, 0x00, 0x02];

        var header = VariableTableParser.TryParseArrayHeader(memory, 0);

        Assert.NotNull(header);
        Assert.Equal("C$", header.Name);
        Assert.Equal(BasicVariableType.String, header.ElementType);
    }

    [Fact]
    public void TryParseArrayHeader_AtNonZeroOffset_ParsesTheEntryThere()
    {
        // A second array entry starting partway through the buffer - the offset a real caller
        // would pass while walking several arrays back to back within one read.
        byte[] memory =
        [
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // unrelated leading bytes
            0x41, 0x00, 0x00, 0x16, 0x01, 0x00, 0x03,  // "A" array header
        ];

        var header = VariableTableParser.TryParseArrayHeader(memory, 7);

        Assert.NotNull(header);
        Assert.Equal("A", header.Name);
        Assert.Equal(7, header.DataOffset); // relative to offset 7, not the buffer start
    }

    [Fact]
    public void TryParseArrayHeader_ZeroDimensionCount_ReturnsNull()
    {
        byte[] memory = [0x41, 0x00, 0x00, 0x16, 0x00];
        Assert.Null(VariableTableParser.TryParseArrayHeader(memory, 0));
    }

    [Fact]
    public void TryParseArrayHeader_TruncatedBeforeDimensionSizes_ReturnsNull()
    {
        byte[] memory = [0x41, 0x00, 0x00, 0x16, 0x02, 0x03, 0x00]; // claims 2 dimensions, only 1 present
        Assert.Null(VariableTableParser.TryParseArrayHeader(memory, 0));
    }

    [Fact]
    public void TryParseArrayHeader_TruncatedFixedHeader_ReturnsNull()
    {
        byte[] memory = [0x41, 0x00, 0x00, 0x16]; // missing the dimension-count byte
        Assert.Null(VariableTableParser.TryParseArrayHeader(memory, 0));
    }

    // ── ParseArrayElements ───────────────────────────────────────────────────
    // Unlike TryParseArrayHeader, `data` here is the element data ONLY - i.e. what a caller would
    // read separately, starting exactly at the array's own DataOffset, once it actually wants that
    // array's contents (see LoadArrayChildrenAsync in MainViewModel for why that's deferred).

    [Fact]
    public void ParseArrayElements_OneDimensionalFloatArray_ProducesOneElementPerIndex()
    {
        byte[] data =
        [
            0x00, 0x00, 0x00, 0x00, 0x00,  // A(0) = 0.0
            0x82, 0x40, 0x00, 0x00, 0x00,  // A(1) = 3.0
            0x00, 0x00, 0x00, 0x00, 0x00,  // A(2) = 0.0
        ];

        var elements = VariableTableParser.ParseArrayElements(data, dataAddress: 0x1007, "A", BasicVariableType.Float, [3]);

        Assert.Equal(3, elements.Count);
        Assert.Equal("A(0)", elements[0].Name);
        Assert.Equal("A(1)", elements[1].Name);
        Assert.Equal("A(2)", elements[2].Name);
        Assert.All(elements, v => Assert.Equal(BasicVariableType.Float, v.Type));
        Assert.Equal(3.0, (double)elements[1].Value, precision: 9);
        Assert.Equal(0x100C, elements[1].ValueAddress); // 0x1007 + 1 * 5-byte element
    }

    [Fact]
    public void ParseArrayElements_TwoDimensionalIntegerArray_WalksRowMajorLastSubscriptFastest()
    {
        byte[] data =
        [
            0x00, 0x0A, // B%(0,0) = 10
            0x00, 0x0B, // B%(0,1) = 11
            0x00, 0x0C, // B%(0,2) = 12
            0x00, 0x0D, // B%(1,0) = 13
            0x00, 0x0E, // B%(1,1) = 14
            0x00, 0x0F, // B%(1,2) = 15
        ];

        var elements = VariableTableParser.ParseArrayElements(data, dataAddress: 0x2000, "B%", BasicVariableType.Integer, [2, 3]);

        Assert.Equal(6, elements.Count);
        string[] expectedNames = ["B%(0,0)", "B%(0,1)", "B%(0,2)", "B%(1,0)", "B%(1,1)", "B%(1,2)"];
        short[] expectedValues = [10, 11, 12, 13, 14, 15];
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(expectedNames[i], elements[i].Name);
            Assert.Equal(BasicVariableType.Integer, elements[i].Type);
            Assert.Equal(expectedValues[i], (short)elements[i].Value);
        }
    }

    [Fact]
    public void ParseArrayElements_StringArray_DecodesEachElementsDescriptor()
    {
        byte[] data =
        [
            0x03, 0x00, 0x20, // C$(0): length 3, pointer 0x2000
            0x00, 0x00, 0x00, // C$(1): length 0, pointer 0x0000
        ];

        var elements = VariableTableParser.ParseArrayElements(data, dataAddress: 0x3007, "C$", BasicVariableType.String, [2]);

        Assert.Equal(2, elements.Count);

        Assert.Equal("C$(0)", elements[0].Name);
        var d0 = Assert.IsType<StringDescriptor>(elements[0].Value);
        Assert.Equal(3, d0.Length);
        Assert.Equal(0x2000, d0.HeapPointer);

        Assert.Equal("C$(1)", elements[1].Name);
        var d1 = Assert.IsType<StringDescriptor>(elements[1].Value);
        Assert.Equal(0, d1.Length);
    }

    [Fact]
    public void ParseArrayElements_TruncatedData_StopsWithoutThrowing()
    {
        // Claims a 3-element array, but the snapshot cuts off partway through the second element.
        byte[] data =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, // A(0)
            0x82, 0x40,                   // A(1) cut off here
        ];

        var elements = VariableTableParser.ParseArrayElements(data, dataAddress: 0x1007, "A", BasicVariableType.Float, [3]);

        Assert.Single(elements); // only the fully-readable A(0) comes back
    }

    [Fact]
    public void ParseArrayElements_EmptyData_ReturnsNoElements()
    {
        var elements = VariableTableParser.ParseArrayElements(Array.Empty<byte>(), dataAddress: 0x1000, "A", BasicVariableType.Float, [3]);
        Assert.Empty(elements);
    }

    // ── TryParseArrayHeader + ParseArrayElements together ───────────────────
    // Mirrors how MainViewModel actually uses the two: walk headers first (cheap - LoadArrayHeadersAsync),
    // then decode one specific array's elements only once it's needed (LoadArrayChildrenAsync).

    [Fact]
    public void HeaderThenElements_TwoArraysInTable_WalksBothViaEntryLength()
    {
        byte[] firstArray =
        [
            0x41, 0x00,                    // name "A"
            0x00, 0x0C,                    // entry length = 12
            0x01,                          // 1 dimension
            0x00, 0x01,                    // dimension size = 1
            0x00, 0x00, 0x00, 0x00, 0x00,  // A(0) = 0.0
        ];
        byte[] secondArray =
        [
            0xC2, 0xC1,        // name "BA%"
            0x00, 0x0B,        // entry length = 11
            0x01,              // 1 dimension
            0x00, 0x02,        // dimension size = 2
            0x00, 0x2A,        // BA%(0) = 42
            0xFF, 0xFF,        // BA%(1) = -1
        ];
        byte[] memory = [.. firstArray, .. secondArray];
        const ushort arytab = 0x4000;

        var firstHeader = VariableTableParser.TryParseArrayHeader(memory, 0);
        Assert.NotNull(firstHeader);
        Assert.Equal("A", firstHeader.Name);

        var secondHeader = VariableTableParser.TryParseArrayHeader(memory, firstHeader.EntryLength);
        Assert.NotNull(secondHeader);
        Assert.Equal("BA%", secondHeader.Name);

        var secondData = memory[(firstHeader.EntryLength + secondHeader.DataOffset)..];
        var secondElements = VariableTableParser.ParseArrayElements(
            secondData, (ushort)(arytab + firstHeader.EntryLength + secondHeader.DataOffset),
            secondHeader.Name, secondHeader.ElementType, secondHeader.DimensionSizes);

        Assert.Equal(2, secondElements.Count);
        Assert.Equal("BA%(0)", secondElements[0].Name);
        Assert.Equal((short)42, (short)secondElements[0].Value);
        Assert.Equal("BA%(1)", secondElements[1].Name);
        Assert.Equal((short)(-1), (short)secondElements[1].Value);
    }

    #endregion
}
