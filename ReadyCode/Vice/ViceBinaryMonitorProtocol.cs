// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace ReadyCode.Vice;

/// <summary>
/// Pure request-building and response-parsing functions for the VICE binary monitor commands
/// used by the BASIC debugger (checkpoints, conditions, memory writes, registers) - kept separate
/// from <see cref="ViceClient"/>'s socket handling so the byte layouts can be unit tested without
/// a live VICE instance. Command IDs and body layouts are verified against VICE's binary monitor
/// protocol documentation (vice-emu.sourceforge.io, section 13).
/// </summary>
public static class ViceBinaryMonitorProtocol
{
    #region Command IDs

    /// <summary>Reads bytes from memory ("Memory get"), with no side effects.</summary>
    public const byte MemoryGetCommand = 0x01;

    /// <summary>Writes bytes to memory ("Memory set").</summary>
    public const byte MemorySetCommand = 0x02;

    /// <summary>Resumes execution ("Exit monitor").</summary>
    public const byte ExitCommand = 0xaa;

    /// <summary>Injects text into the keyboard buffer ("Keyboard feed").</summary>
    public const byte KeyboardFeedCommand = 0x72;

    /// <summary>Reads one checkpoint's current state ("Checkpoint get") - same response shape as the unsolicited checkpoint-hit event.</summary>
    public const byte CheckpointGetCommand = 0x11;

    /// <summary>Creates a checkpoint ("Checkpoint set").</summary>
    public const byte CheckpointSetCommand = 0x12;

    /// <summary>Removes a checkpoint ("Checkpoint delete").</summary>
    public const byte CheckpointDeleteCommand = 0x13;

    /// <summary>Enables/disables a checkpoint without deleting it ("Checkpoint toggle").</summary>
    public const byte CheckpointToggleCommand = 0x15;

    /// <summary>Attaches a condition expression to a checkpoint ("Condition set").</summary>
    public const byte ConditionSetCommand = 0x22;

    /// <summary>Reads current register values ("Registers get").</summary>
    public const byte RegistersGetCommand = 0x31;

    /// <summary>Enumerates available register names/ids for a memspace ("Registers available").</summary>
    public const byte RegistersAvailableCommand = 0x83;

    /// <summary>Response/event type for a checkpoint hit, both as a direct reply to <see cref="CheckpointGetCommand"/>/<see cref="CheckpointSetCommand"/> and as an unsolicited notification (request id 0xffffffff) when a checkpoint fires.</summary>
    public const byte CheckpointInfoResponseType = 0x11;

    /// <summary>Unsolicited event type sent when the CPU stops (e.g. after a checkpoint hit or a step).</summary>
    public const byte StoppedResponseType = 0x62;

    /// <summary>Unsolicited event type sent when the CPU resumes execution.</summary>
    public const byte ResumedResponseType = 0x63;

    /// <summary>CPU operation value for an "exec" checkpoint (fires when execution reaches the address).</summary>
    public const byte ExecOperation = 0x04;

    /// <summary>CPU operation value for a "store" checkpoint (fires when the address is written to).</summary>
    public const byte StoreOperation = 0x02;

    #endregion

    #region Public Methods

    /// <summary>
    /// Builds the request body for <see cref="CheckpointSetCommand"/>.
    /// </summary>
    public static byte[] BuildCheckpointSetRequest(ushort startAddress, ushort endAddress, bool stopWhenHit, bool enabled, byte cpuOperation, bool temporary)
    {
        byte[] body = new byte[8];
        BitConverter.GetBytes(startAddress).CopyTo(body, 0);
        BitConverter.GetBytes(endAddress).CopyTo(body, 2);
        body[4] = stopWhenHit ? (byte)1 : (byte)0;
        body[5] = enabled ? (byte)1 : (byte)0;
        body[6] = cpuOperation;
        body[7] = temporary ? (byte)1 : (byte)0;
        return body;
    }

    /// <summary>
    /// Builds the request body for <see cref="CheckpointDeleteCommand"/>.
    /// </summary>
    public static byte[] BuildCheckpointDeleteRequest(uint checkpointNumber) => BitConverter.GetBytes(checkpointNumber);

    /// <summary>
    /// Builds the request body for <see cref="CheckpointGetCommand"/>.
    /// </summary>
    public static byte[] BuildCheckpointGetRequest(uint checkpointNumber) => BitConverter.GetBytes(checkpointNumber);

    /// <summary>
    /// Builds the request body for <see cref="CheckpointToggleCommand"/>.
    /// </summary>
    public static byte[] BuildCheckpointToggleRequest(uint checkpointNumber, bool enabled)
    {
        byte[] body = new byte[5];
        BitConverter.GetBytes(checkpointNumber).CopyTo(body, 0);
        body[4] = enabled ? (byte)1 : (byte)0;
        return body;
    }

    /// <summary>
    /// Builds the request body for <see cref="ConditionSetCommand"/>. The expression uses VICE's
    /// text-monitor condition syntax (e.g. "@cpu:$39 == $0a &amp;&amp; @cpu:$3a == $00"), sent as
    /// PETSCII/ASCII text, not null-terminated.
    /// </summary>
    public static byte[] BuildConditionSetRequest(uint checkpointNumber, string expression)
    {
        byte[] exprBytes = Encoding.ASCII.GetBytes(expression);
        if (exprBytes.Length > 255)
            throw new ArgumentException("Condition expression is too long (max 255 bytes).", nameof(expression));

        byte[] body = new byte[4 + 1 + exprBytes.Length];
        BitConverter.GetBytes(checkpointNumber).CopyTo(body, 0);
        body[4] = (byte)exprBytes.Length;
        exprBytes.CopyTo(body, 5);
        return body;
    }

    /// <summary>
    /// Builds the request body for <see cref="MemoryGetCommand"/>, reading main memory (bank 0)
    /// from <paramref name="startAddress"/> to <paramref name="endAddress"/> inclusive, with no
    /// side effects.
    /// </summary>
    public static byte[] BuildMemoryGetRequest(ushort startAddress, ushort endAddress)
    {
        byte[] body = new byte[8];
        body[0] = 0x00; // FX: no side effects
        BitConverter.GetBytes(startAddress).CopyTo(body, 1); // SA
        BitConverter.GetBytes(endAddress).CopyTo(body, 3);   // EA
        body[5] = 0x00;                                      // MS: main memory
        BitConverter.GetBytes((ushort)0).CopyTo(body, 6);    // BI: bank 0

        return body;
    }

    /// <summary>
    /// Parses a <see cref="MemoryGetCommand"/> response body into the raw memory bytes read.
    /// </summary>
    public static byte[] ParseMemoryGetResponse(byte[] body)
    {
        ushort length = BitConverter.ToUInt16(body, 0);
        return body[2..(2 + length)];
    }

    /// <summary>
    /// Builds the request body for <see cref="KeyboardFeedCommand"/> - injects
    /// <paramref name="text"/> (plain ASCII, byte-identical to PETSCII for the digits, uppercase
    /// letters, and Return this is normally used for) straight into the keyboard buffer.
    /// </summary>
    public static byte[] BuildKeyboardFeedRequest(string text)
    {
        byte[] textBytes = Encoding.ASCII.GetBytes(text);
        if (textBytes.Length > 255)
            throw new ArgumentException("Text is too long (max 255 bytes).", nameof(text));

        byte[] body = new byte[1 + textBytes.Length];
        body[0] = (byte)textBytes.Length;
        textBytes.CopyTo(body, 1);
        return body;
    }

    /// <summary>
    /// Builds the request body for <see cref="MemorySetCommand"/>, writing <paramref name="data"/>
    /// starting at <paramref name="startAddress"/> in main memory (bank 0), with no side effects.
    /// </summary>
    public static byte[] BuildMemorySetRequest(ushort startAddress, byte[] data)
    {
        if (data.Length == 0)
            throw new ArgumentException("Data must not be empty.", nameof(data));

        int endAddress = startAddress + data.Length - 1;
        if (endAddress > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(data), "The requested range extends past $FFFF.");

        byte[] body = new byte[8 + data.Length];
        body[0] = 0x00; // FX: no side effects
        BitConverter.GetBytes(startAddress).CopyTo(body, 1);       // SA
        BitConverter.GetBytes((ushort)endAddress).CopyTo(body, 3); // EA
        body[5] = 0x00;                                            // MS: main memory
        BitConverter.GetBytes((ushort)0).CopyTo(body, 6);          // BI: bank 0
        data.CopyTo(body, 8);

        return body;
    }

    /// <summary>
    /// Builds the request body for <see cref="RegistersAvailableCommand"/>.
    /// </summary>
    public static byte[] BuildRegistersAvailableRequest(byte memspace = 0) => new[] { memspace };

    /// <summary>
    /// Builds the request body for <see cref="RegistersGetCommand"/>.
    /// </summary>
    public static byte[] BuildRegistersGetRequest(byte memspace = 0) => new[] { memspace };

    /// <summary>
    /// Parses a <see cref="CheckpointInfoResponseType"/> response body, returned both as a direct
    /// reply to Checkpoint set/get and as the unsolicited notification when a checkpoint fires.
    /// </summary>
    public static CheckpointInfo ParseCheckpointResponse(byte[] body)
    {
        uint checkpointNumber = BitConverter.ToUInt32(body, 0);
        bool currentlyHit = body[4] != 0;
        ushort startAddress = BitConverter.ToUInt16(body, 5);
        ushort endAddress = BitConverter.ToUInt16(body, 7);
        bool stopWhenHit = body[9] != 0;
        bool enabled = body[10] != 0;
        byte cpuOperation = body[11];
        bool temporary = body[12] != 0;
        uint hitCount = BitConverter.ToUInt32(body, 13);
        uint ignoreCount = BitConverter.ToUInt32(body, 17);
        bool hasCondition = body[21] != 0;

        return new CheckpointInfo(checkpointNumber, currentlyHit, startAddress, endAddress, stopWhenHit, enabled, cpuOperation, temporary, hitCount, ignoreCount, hasCondition);
    }

    /// <summary>
    /// Parses a <see cref="RegistersAvailableCommand"/> response body into a register name → id map.
    /// </summary>
    public static IReadOnlyDictionary<string, byte> ParseRegistersAvailableResponse(byte[] body)
    {
        var result = new Dictionary<string, byte>();
        ushort count = BitConverter.ToUInt16(body, 0);
        int pos = 2;

        for (int i = 0; i < count; i++)
        {
            byte itemSize = body[pos];
            int itemStart = pos + 1;
            byte registerId = body[itemStart];
            // body[itemStart + 1] is the register's size in bits - not needed for name lookup.
            byte nameLength = body[itemStart + 2];
            string name = Encoding.ASCII.GetString(body, itemStart + 3, nameLength);

            result[name] = registerId;
            pos = itemStart + itemSize; // itemSize excludes itself, per the VICE protocol docs
        }

        return result;
    }

    /// <summary>
    /// Parses a <see cref="RegistersGetCommand"/> response body into a register id → value map.
    /// </summary>
    public static IReadOnlyDictionary<byte, ushort> ParseRegistersGetResponse(byte[] body)
    {
        var result = new Dictionary<byte, ushort>();
        ushort count = BitConverter.ToUInt16(body, 0);
        int pos = 2;

        for (int i = 0; i < count; i++)
        {
            byte itemSize = body[pos];
            int itemStart = pos + 1;
            byte registerId = body[itemStart];
            ushort value = BitConverter.ToUInt16(body, itemStart + 1);

            result[registerId] = value;
            pos = itemStart + itemSize; // itemSize excludes itself, per the VICE protocol docs
        }

        return result;
    }

    /// <summary>
    /// Parses a <see cref="StoppedResponseType"/> event body, returning the program counter VICE
    /// stopped at.
    /// </summary>
    public static ushort ParseStoppedEventProgramCounter(byte[] body) => BitConverter.ToUInt16(body, 0);

    #endregion
}

/// <summary>
/// A checkpoint's state, as returned by Checkpoint set/get or the unsolicited hit notification.
/// </summary>
public sealed record CheckpointInfo(
    uint CheckpointNumber,
    bool CurrentlyHit,
    ushort StartAddress,
    ushort EndAddress,
    bool StopWhenHit,
    bool Enabled,
    byte CpuOperation,
    bool Temporary,
    uint HitCount,
    uint IgnoreCount,
    bool HasCondition);
