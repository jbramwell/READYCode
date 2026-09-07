// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Assembler;

/// <summary>
/// The addressing mode of a single 6502 instruction, determining its operand encoding and
/// total instruction length.
/// </summary>
public enum AddressingMode
{
    /// <summary>No operand (e.g. NOP, RTS).</summary>
    Implied,

    /// <summary>Operates on the accumulator (e.g. ASL A).</summary>
    Accumulator,

    /// <summary>An immediate constant operand (e.g. LDA #$00).</summary>
    Immediate,

    /// <summary>A one-byte zero-page address (e.g. LDA $00).</summary>
    ZeroPage,

    /// <summary>A one-byte zero-page address indexed by X (e.g. LDA $00,X).</summary>
    ZeroPageX,

    /// <summary>A one-byte zero-page address indexed by Y (e.g. LDX $00,Y).</summary>
    ZeroPageY,

    /// <summary>A two-byte absolute address (e.g. LDA $0000).</summary>
    Absolute,

    /// <summary>A two-byte absolute address indexed by X (e.g. LDA $0000,X).</summary>
    AbsoluteX,

    /// <summary>A two-byte absolute address indexed by Y (e.g. LDA $0000,Y).</summary>
    AbsoluteY,

    /// <summary>A two-byte indirect absolute address (JMP only, e.g. JMP ($0000)).</summary>
    Indirect,

    /// <summary>A zero-page indirect address indexed by X before dereferencing (e.g. LDA ($00,X)).</summary>
    IndirectX,

    /// <summary>A zero-page indirect address indexed by Y after dereferencing (e.g. LDA ($00),Y).</summary>
    IndirectY,

    /// <summary>A signed branch offset relative to the instruction following it.</summary>
    Relative,
}

/// <summary>
/// Extension methods for <see cref="AddressingMode"/>.
/// </summary>
public static class AddressingModeExtensions
{
    #region Public Methods

    /// <summary>
    /// Gets the total instruction length in bytes (opcode + operand) for the given addressing mode.
    /// </summary>
    public static int InstructionLength(this AddressingMode mode) => mode switch
    {
        AddressingMode.Implied => 1,
        AddressingMode.Accumulator => 1,
        AddressingMode.Immediate => 2,
        AddressingMode.ZeroPage => 2,
        AddressingMode.ZeroPageX => 2,
        AddressingMode.ZeroPageY => 2,
        AddressingMode.IndirectX => 2,
        AddressingMode.IndirectY => 2,
        AddressingMode.Relative => 2,
        AddressingMode.Absolute => 3,
        AddressingMode.AbsoluteX => 3,
        AddressingMode.AbsoluteY => 3,
        AddressingMode.Indirect => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    #endregion
}
