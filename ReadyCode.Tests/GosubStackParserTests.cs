// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Debugger;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="GosubStackParser"/>. Fixture bytes encode a GOSUB frame as marker $8D +
/// line number (little-endian) + a 2-byte return text pointer (value irrelevant to parsing), and
/// a FOR frame as marker $81 followed by 17 filler bytes (best-available, unverified frame size -
/// see the NEEDS VERIFICATION comment in GosubStackParser.cs).
/// </summary>
public class GosubStackParserTests
{
    #region Public Methods

    [Fact]
    public void Parse_SingleGosubFrame_ReturnsOneFrame()
    {
        byte[] stack = new byte[256];
        // Stack pointer $FA -> topmost pushed byte at $FA+1 = offset 0xFB.
        stack[0xFB] = 0x8D;       // marker
        stack[0xFC] = 0x64;       // line 100 low byte
        stack[0xFD] = 0x00;       // line 100 high byte
        stack[0xFE] = 0x00;       // return text pointer (unused)
        stack[0xFF] = 0x08;

        var frames = GosubStackParser.Parse(stack, stackPointer: 0xFA, lineTable: null);

        var frame = Assert.Single(frames);
        Assert.Equal((ushort)100, frame.ReturnLineNumber);
        Assert.Null(frame.DocumentLine);
    }

    [Fact]
    public void Parse_NestedGosubFrames_ReturnsInnermostFirst()
    {
        byte[] stack = new byte[256];
        int offset = 0xF0;

        // Innermost (most recently called) frame is closest to SP, so it's found first.
        stack[offset++] = 0x8D;
        stack[offset++] = 0xC8; stack[offset++] = 0x00; // line 200
        stack[offset++] = 0; stack[offset++] = 0;

        stack[offset++] = 0x8D;
        stack[offset++] = 0x64; stack[offset++] = 0x00; // line 100
        stack[offset++] = 0; stack[offset++] = 0;

        var frames = GosubStackParser.Parse(stack, stackPointer: 0xEF, lineTable: null);

        Assert.Equal(2, frames.Count);
        Assert.Equal((ushort)200, frames[0].ReturnLineNumber);
        Assert.Equal((ushort)100, frames[1].ReturnLineNumber);
    }

    [Fact]
    public void Parse_ForFrameInterleaved_SkipsItWithoutProducingAFrame()
    {
        byte[] stack = new byte[256];
        int offset = 0xE0;

        // A GOSUB called from inside a FOR loop: GOSUB frame closest to SP, FOR frame below it.
        stack[offset++] = 0x8D;
        stack[offset++] = 0x64; stack[offset++] = 0x00; // line 100
        stack[offset++] = 0; stack[offset++] = 0;

        int forFrameStart = offset;
        stack[offset] = 0x81; // FOR marker
        offset = forFrameStart + 18; // skip the (unverified) 18-byte FOR frame

        stack[offset++] = 0x8D;
        stack[offset++] = 0x32; stack[offset++] = 0x00; // line 50
        stack[offset++] = 0; stack[offset++] = 0;

        var frames = GosubStackParser.Parse(stack, stackPointer: 0xDF, lineTable: null);

        Assert.Equal(2, frames.Count);
        Assert.Equal((ushort)100, frames[0].ReturnLineNumber);
        Assert.Equal((ushort)50, frames[1].ReturnLineNumber);
    }

    [Fact]
    public void Parse_ResolvesDocumentLineFromLineTable()
    {
        byte[] stack = new byte[256];
        stack[0xFB] = 0x8D;
        stack[0xFC] = 0x64; stack[0xFD] = 0x00; // line 100
        stack[0xFE] = 0; stack[0xFF] = 0;

        var lineTable = ReadyCode.Debugger.BasicLineAddressTable.Build("100 PRINT \"HI\"");
        var frames = GosubStackParser.Parse(stack, stackPointer: 0xFA, lineTable);

        Assert.Equal(1, Assert.Single(frames).DocumentLine);
    }

    [Fact]
    public void Parse_EmptyStack_ReturnsNoFrames()
    {
        byte[] stack = new byte[256]; // all zero - not a recognized marker
        var frames = GosubStackParser.Parse(stack, stackPointer: 0xFF, lineTable: null);
        Assert.Empty(frames);
    }

    [Fact]
    public void Parse_UnrecognizedMarker_StopsWithoutThrowing()
    {
        byte[] stack = new byte[256];
        stack[0xFB] = 0x99; // not a GOSUB or FOR marker

        var frames = GosubStackParser.Parse(stack, stackPointer: 0xFA, lineTable: null);
        Assert.Empty(frames);
    }

    [Fact]
    public void Parse_TruncatedGosubFrame_StopsWithoutThrowing()
    {
        byte[] stack = new byte[256];
        stack[255] = 0x8D; // marker present, but no room for the rest of the frame

        var frames = GosubStackParser.Parse(stack, stackPointer: 254, lineTable: null);
        Assert.Empty(frames);
    }

    #endregion
}
