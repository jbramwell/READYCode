// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.C64U;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="C64UDebugStub"/>. This is hand-written 6502 machine code that will run
/// directly on real C64 Ultimate hardware, patched into a live interpreter vector - these tests
/// exist to catch an assembly-level mistake (a bad forward reference, a wrong opcode, a
/// mis-sized data area) before it ever gets uploaded to a real device.
/// </summary>
public class C64UDebugStubTests
{
    #region Public Methods

    [Fact]
    public void Assemble_Succeeds()
    {
        var (bytes, labels) = C64UDebugStub.Assemble(0xCF00);

        Assert.NotEmpty(bytes);
        Assert.NotEmpty(labels);
    }

    // (name, size in bytes) for every data-area label, in declared order - BREAK_LINES_LO/HI are
    // MaxBreakpoints-byte arrays, everything else is a single byte.
    private static (string Name, int Size)[] DataLayout() =>
    [
        ("ORIG_GONE_LO", 1), ("ORIG_GONE_HI", 1), ("LAST_LINE_LO", 1), ("LAST_LINE_HI", 1),
        ("BREAK_COUNT", 1), ("BREAK_LINES_LO", C64UDebugStub.MaxBreakpoints), ("BREAK_LINES_HI", C64UDebugStub.MaxBreakpoints),
        ("STEP_MODE", 1), ("HALTED_FLAG", 1), ("RESUME_FLAG", 1), ("HALT_COUNT", 1),
    ];

    [Fact]
    public void Assemble_EveryDataLabel_IsPresent()
    {
        var (_, labels) = C64UDebugStub.Assemble(0xCF00);

        foreach (var (name, _) in DataLayout())
            Assert.True(labels.ContainsKey(name), $"Missing label '{name}'.");
    }

    [Fact]
    public void Assemble_DataLabels_AreConsecutiveInDeclaredOrder()
    {
        var (bytes, labels) = C64UDebugStub.Assemble(0xCF00);
        var layout = DataLayout();

        for (int i = 1; i < layout.Length; i++)
        {
            var (previousName, previousSize) = layout[i - 1];
            Assert.Equal(labels[previousName] + previousSize, labels[layout[i].Name]);
        }

        // The data area is the last thing in the file, so it must end exactly at the end of the
        // assembled bytes (base + bytes.Length), or something after it isn't accounted for.
        var (lastName, lastSize) = layout[^1];
        ushort baseAddress = 0xCF00;
        Assert.Equal(baseAddress + bytes.Length, labels[lastName] + lastSize);
    }

    [Fact]
    public void Assemble_EveryDataByte_IsZeroInitialized()
    {
        var (bytes, labels) = C64UDebugStub.Assemble(0xCF00);
        ushort baseAddress = 0xCF00;

        // Only the data labels - unlike the code labels (START, NEW_LINE, etc.), which point at
        // real instruction opcodes and are never expected to be zero.
        foreach (var (name, size) in DataLayout())
        {
            for (int offset = 0; offset < size; offset++)
                Assert.Equal(0, bytes[labels[name] - baseAddress + offset]);
        }
    }

    [Fact]
    public void Assemble_LastInstructionBeforeDataArea_IsIndirectJumpThroughOrigGone()
    {
        var (bytes, labels) = C64UDebugStub.Assemble(0xCF00);
        ushort baseAddress = 0xCF00;

        // JMP (nnnn) is opcode $6C followed by the little-endian pointer address - the data area
        // starts immediately after, so this is the last 3 bytes before ORIG_GONE_LO's own address.
        int jmpOffset = labels["ORIG_GONE_LO"] - baseAddress - 3;

        Assert.Equal(0x6C, bytes[jmpOffset]);
        ushort operand = (ushort)(bytes[jmpOffset + 1] | (bytes[jmpOffset + 2] << 8));
        Assert.Equal(labels["ORIG_GONE_LO"], operand);
    }

    [Fact]
    public void Assemble_TotalSize_FitsInOneWritememCall()
    {
        // C64UltimateClient.WriteMemoryAsync caps at 128 bytes per call - if the stub ever grows
        // past that, C64UDebugSession's upload logic needs to start chunking it.
        var (bytes, _) = C64UDebugStub.Assemble(0xCF00);
        Assert.True(bytes.Length <= 128, $"Stub is {bytes.Length} bytes - update C64UDebugSession's upload to chunk if this is intentional.");
    }

    [Fact]
    public void Assemble_DifferentBaseAddress_ShiftsEveryLabelByTheSameDelta()
    {
        var (_, labelsAtCf00) = C64UDebugStub.Assemble(0xCF00);
        var (_, labelsAtD000) = C64UDebugStub.Assemble(0xD000);

        foreach (var (name, address) in labelsAtCf00)
            Assert.Equal(address + 0x100, labelsAtD000[name]);
    }

    #endregion
}
