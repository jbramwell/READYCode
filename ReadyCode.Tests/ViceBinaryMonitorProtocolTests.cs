// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Vice;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="ViceBinaryMonitorProtocol"/>.
/// </summary>
public class ViceBinaryMonitorProtocolTests
{
    #region Public Methods

    // ── BuildMemoryGetRequest / ParseMemoryGetResponse ────────────────────────

    [Fact]
    public void BuildMemoryGetRequest_EncodesRangeWithNoSideEffects()
    {
        byte[] body = ViceBinaryMonitorProtocol.BuildMemoryGetRequest(0x0039, 0x003A);

        Assert.Equal(0x00, body[0]);                          // no side effects
        Assert.Equal(0x0039, BitConverter.ToUInt16(body, 1)); // start address
        Assert.Equal(0x003A, BitConverter.ToUInt16(body, 3)); // end address
        Assert.Equal(0x00, body[5]);                          // main memory
        Assert.Equal(0, BitConverter.ToUInt16(body, 6));      // bank 0
    }

    [Fact]
    public void ParseMemoryGetResponse_ExtractsDataAfterLengthPrefix()
    {
        byte[] body = { 0x03, 0x00, 0xAA, 0xBB, 0xCC };
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, ViceBinaryMonitorProtocol.ParseMemoryGetResponse(body));
    }

    // ── BuildCheckpointSetRequest ────────────────────────────────────────────

    [Fact]
    public void BuildCheckpointSetRequest_EncodesAllFieldsInOrder()
    {
        byte[] body = ViceBinaryMonitorProtocol.BuildCheckpointSetRequest(
            startAddress: 0xA7E4, endAddress: 0xA7E4, stopWhenHit: true, enabled: true,
            cpuOperation: ViceBinaryMonitorProtocol.ExecOperation, temporary: false);

        Assert.Equal(new byte[] { 0xE4, 0xA7, 0xE4, 0xA7, 0x01, 0x01, 0x04, 0x00 }, body);
    }

    // ── BuildCheckpointDeleteRequest / ToggleRequest / GetRequest ────────────

    [Fact]
    public void BuildCheckpointDeleteRequest_EncodesCheckpointNumberLittleEndian()
    {
        byte[] body = ViceBinaryMonitorProtocol.BuildCheckpointDeleteRequest(0x00000102);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x00, 0x00 }, body);
    }

    [Fact]
    public void BuildCheckpointToggleRequest_EncodesNumberThenEnabledFlag()
    {
        byte[] body = ViceBinaryMonitorProtocol.BuildCheckpointToggleRequest(5, enabled: false);
        Assert.Equal(new byte[] { 0x05, 0x00, 0x00, 0x00, 0x00 }, body);
    }

    // ── BuildConditionSetRequest ──────────────────────────────────────────────

    [Fact]
    public void BuildConditionSetRequest_EncodesNumberLengthThenAsciiExpression()
    {
        byte[] body = ViceBinaryMonitorProtocol.BuildConditionSetRequest(1, "X==$0a");

        Assert.Equal(1u, BitConverter.ToUInt32(body, 0));
        Assert.Equal(6, body[4]);
        Assert.Equal("X==$0a", System.Text.Encoding.ASCII.GetString(body, 5, 6));
        Assert.Equal(11, body.Length);
    }

    [Fact]
    public void BuildConditionSetRequest_TooLongExpression_Throws()
    {
        string expr = new string('X', 256);
        Assert.Throws<ArgumentException>(() => ViceBinaryMonitorProtocol.BuildConditionSetRequest(1, expr));
    }

    // ── BuildMemorySetRequest ─────────────────────────────────────────────────

    [Fact]
    public void BuildMemorySetRequest_EncodesHeaderThenData()
    {
        byte[] data = { 0xAA, 0xBB, 0xCC };
        byte[] body = ViceBinaryMonitorProtocol.BuildMemorySetRequest(0x1000, data);

        Assert.Equal(0x00, body[0]);                                  // side effects: none
        Assert.Equal(0x1000, BitConverter.ToUInt16(body, 1));         // start address
        Assert.Equal(0x1002, BitConverter.ToUInt16(body, 3));         // end address
        Assert.Equal(0x00, body[5]);                                  // memspace: main memory
        Assert.Equal(0, BitConverter.ToUInt16(body, 6));              // bank 0
        Assert.Equal(data, body[8..]);
    }

    [Fact]
    public void BuildMemorySetRequest_RangePastFfff_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViceBinaryMonitorProtocol.BuildMemorySetRequest(0xFFFE, new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void BuildMemorySetRequest_EmptyData_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ViceBinaryMonitorProtocol.BuildMemorySetRequest(0x1000, Array.Empty<byte>()));
    }

    // ── ParseCheckpointResponse ───────────────────────────────────────────────

    [Fact]
    public void ParseCheckpointResponse_DecodesAllFieldsInOrder()
    {
        byte[] body =
        [
            0x2A, 0x00, 0x00, 0x00, // checkpoint number = 42
            0x01,                   // currently hit
            0xE4, 0xA7,             // start address
            0xE4, 0xA7,             // end address
            0x01,                   // stop when hit
            0x01,                   // enabled
            0x04,                   // cpu operation: exec
            0x00,                   // temporary: false
            0x05, 0x00, 0x00, 0x00, // hit count = 5
            0x00, 0x00, 0x00, 0x00, // ignore count = 0
            0x01,                   // has condition
            0x00,                   // memspace
        ];

        CheckpointInfo info = ViceBinaryMonitorProtocol.ParseCheckpointResponse(body);

        Assert.Equal(42u, info.CheckpointNumber);
        Assert.True(info.CurrentlyHit);
        Assert.Equal(0xA7E4, info.StartAddress);
        Assert.Equal(0xA7E4, info.EndAddress);
        Assert.True(info.StopWhenHit);
        Assert.True(info.Enabled);
        Assert.Equal(ViceBinaryMonitorProtocol.ExecOperation, info.CpuOperation);
        Assert.False(info.Temporary);
        Assert.Equal(5u, info.HitCount);
        Assert.Equal(0u, info.IgnoreCount);
        Assert.True(info.HasCondition);
    }

    // ── ParseRegistersAvailableResponse ───────────────────────────────────────

    [Fact]
    public void ParseRegistersAvailableResponse_DecodesNameToIdMap()
    {
        // Two registers: id=0 "A" (8 bits), id=3 "PC" (16 bits).
        byte[] body =
        [
            0x02, 0x00,             // count = 2
            0x04, 0x00, 0x08, 0x01, (byte)'A',                                   // item 1
            0x05, 0x03, 0x10, 0x02, (byte)'P', (byte)'C',                        // item 2
        ];

        var registers = ViceBinaryMonitorProtocol.ParseRegistersAvailableResponse(body);

        Assert.Equal(2, registers.Count);
        Assert.Equal((byte)0, registers["A"]);
        Assert.Equal((byte)3, registers["PC"]);
    }

    // ── ParseRegistersGetResponse ─────────────────────────────────────────────

    [Fact]
    public void ParseRegistersGetResponse_DecodesIdToValueMap()
    {
        byte[] body =
        [
            0x02, 0x00,             // count = 2
            0x03, 0x00, 0x2A, 0x00, // item 1: id=0, value=0x002A
            0x03, 0x03, 0xE4, 0xA7, // item 2: id=3, value=0xA7E4
        ];

        var registers = ViceBinaryMonitorProtocol.ParseRegistersGetResponse(body);

        Assert.Equal(2, registers.Count);
        Assert.Equal((ushort)0x002A, registers[0]);
        Assert.Equal((ushort)0xA7E4, registers[3]);
    }

    // ── ParseStoppedEventProgramCounter ───────────────────────────────────────

    [Fact]
    public void ParseStoppedEventProgramCounter_DecodesLittleEndianPc()
    {
        byte[] body = { 0xE4, 0xA7 };
        Assert.Equal(0xA7E4, ViceBinaryMonitorProtocol.ParseStoppedEventProgramCounter(body));
    }

    #endregion
}
