// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Assembler;

/// <summary>
/// The opcode byte for each addressing mode legally supported by every standard 6502 mnemonic.
/// A mnemonic's absence of a given <see cref="AddressingMode"/> key is exactly how an invalid
/// addressing mode for that mnemonic (e.g. "STX #imm") is detected - there is no separate
/// validity list to keep in sync.
/// </summary>
public static class OpcodeTable
{
    #region Public Properties

    /// <summary>
    /// Maps each of the 56 standard 6502 mnemonics to the addressing modes it supports and the
    /// opcode byte for each. Keyed case-insensitively to match <see cref="Tokenizer.AsmTokens.Mnemonics"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<AddressingMode, byte>> Modes =
        new Dictionary<string, IReadOnlyDictionary<AddressingMode, byte>>(StringComparer.OrdinalIgnoreCase)
    {
        // Load/Store
        { "LDA", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0xA9 }, { AddressingMode.ZeroPage, 0xA5 }, { AddressingMode.ZeroPageX, 0xB5 },
            { AddressingMode.Absolute, 0xAD }, { AddressingMode.AbsoluteX, 0xBD }, { AddressingMode.AbsoluteY, 0xB9 },
            { AddressingMode.IndirectX, 0xA1 }, { AddressingMode.IndirectY, 0xB1 },
        } },
        { "LDX", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0xA2 }, { AddressingMode.ZeroPage, 0xA6 }, { AddressingMode.ZeroPageY, 0xB6 },
            { AddressingMode.Absolute, 0xAE }, { AddressingMode.AbsoluteY, 0xBE },
        } },
        { "LDY", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0xA0 }, { AddressingMode.ZeroPage, 0xA4 }, { AddressingMode.ZeroPageX, 0xB4 },
            { AddressingMode.Absolute, 0xAC }, { AddressingMode.AbsoluteX, 0xBC },
        } },
        { "STA", new Dictionary<AddressingMode, byte> {
            { AddressingMode.ZeroPage, 0x85 }, { AddressingMode.ZeroPageX, 0x95 },
            { AddressingMode.Absolute, 0x8D }, { AddressingMode.AbsoluteX, 0x9D }, { AddressingMode.AbsoluteY, 0x99 },
            { AddressingMode.IndirectX, 0x81 }, { AddressingMode.IndirectY, 0x91 },
        } },
        { "STX", new Dictionary<AddressingMode, byte> {
            { AddressingMode.ZeroPage, 0x86 }, { AddressingMode.ZeroPageY, 0x96 }, { AddressingMode.Absolute, 0x8E },
        } },
        { "STY", new Dictionary<AddressingMode, byte> {
            { AddressingMode.ZeroPage, 0x84 }, { AddressingMode.ZeroPageX, 0x94 }, { AddressingMode.Absolute, 0x8C },
        } },

        // Arithmetic
        { "ADC", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0x69 }, { AddressingMode.ZeroPage, 0x65 }, { AddressingMode.ZeroPageX, 0x75 },
            { AddressingMode.Absolute, 0x6D }, { AddressingMode.AbsoluteX, 0x7D }, { AddressingMode.AbsoluteY, 0x79 },
            { AddressingMode.IndirectX, 0x61 }, { AddressingMode.IndirectY, 0x71 },
        } },
        { "SBC", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0xE9 }, { AddressingMode.ZeroPage, 0xE5 }, { AddressingMode.ZeroPageX, 0xF5 },
            { AddressingMode.Absolute, 0xED }, { AddressingMode.AbsoluteX, 0xFD }, { AddressingMode.AbsoluteY, 0xF9 },
            { AddressingMode.IndirectX, 0xE1 }, { AddressingMode.IndirectY, 0xF1 },
        } },

        // Logical
        { "AND", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0x29 }, { AddressingMode.ZeroPage, 0x25 }, { AddressingMode.ZeroPageX, 0x35 },
            { AddressingMode.Absolute, 0x2D }, { AddressingMode.AbsoluteX, 0x3D }, { AddressingMode.AbsoluteY, 0x39 },
            { AddressingMode.IndirectX, 0x21 }, { AddressingMode.IndirectY, 0x31 },
        } },
        { "ORA", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0x09 }, { AddressingMode.ZeroPage, 0x05 }, { AddressingMode.ZeroPageX, 0x15 },
            { AddressingMode.Absolute, 0x0D }, { AddressingMode.AbsoluteX, 0x1D }, { AddressingMode.AbsoluteY, 0x19 },
            { AddressingMode.IndirectX, 0x01 }, { AddressingMode.IndirectY, 0x11 },
        } },
        { "EOR", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0x49 }, { AddressingMode.ZeroPage, 0x45 }, { AddressingMode.ZeroPageX, 0x55 },
            { AddressingMode.Absolute, 0x4D }, { AddressingMode.AbsoluteX, 0x5D }, { AddressingMode.AbsoluteY, 0x59 },
            { AddressingMode.IndirectX, 0x41 }, { AddressingMode.IndirectY, 0x51 },
        } },
        { "BIT", new Dictionary<AddressingMode, byte> {
            { AddressingMode.ZeroPage, 0x24 }, { AddressingMode.Absolute, 0x2C },
        } },

        // Shift/Rotate
        { "ASL", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Accumulator, 0x0A }, { AddressingMode.ZeroPage, 0x06 }, { AddressingMode.ZeroPageX, 0x16 },
            { AddressingMode.Absolute, 0x0E }, { AddressingMode.AbsoluteX, 0x1E },
        } },
        { "LSR", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Accumulator, 0x4A }, { AddressingMode.ZeroPage, 0x46 }, { AddressingMode.ZeroPageX, 0x56 },
            { AddressingMode.Absolute, 0x4E }, { AddressingMode.AbsoluteX, 0x5E },
        } },
        { "ROL", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Accumulator, 0x2A }, { AddressingMode.ZeroPage, 0x26 }, { AddressingMode.ZeroPageX, 0x36 },
            { AddressingMode.Absolute, 0x2E }, { AddressingMode.AbsoluteX, 0x3E },
        } },
        { "ROR", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Accumulator, 0x6A }, { AddressingMode.ZeroPage, 0x66 }, { AddressingMode.ZeroPageX, 0x76 },
            { AddressingMode.Absolute, 0x6E }, { AddressingMode.AbsoluteX, 0x7E },
        } },

        // Increment/Decrement
        { "INC", new Dictionary<AddressingMode, byte> {
            { AddressingMode.ZeroPage, 0xE6 }, { AddressingMode.ZeroPageX, 0xF6 }, { AddressingMode.Absolute, 0xEE }, { AddressingMode.AbsoluteX, 0xFE },
        } },
        { "INX", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xE8 } } },
        { "INY", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xC8 } } },
        { "DEC", new Dictionary<AddressingMode, byte> {
            { AddressingMode.ZeroPage, 0xC6 }, { AddressingMode.ZeroPageX, 0xD6 }, { AddressingMode.Absolute, 0xCE }, { AddressingMode.AbsoluteX, 0xDE },
        } },
        { "DEX", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xCA } } },
        { "DEY", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x88 } } },

        // Compare
        { "CMP", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0xC9 }, { AddressingMode.ZeroPage, 0xC5 }, { AddressingMode.ZeroPageX, 0xD5 },
            { AddressingMode.Absolute, 0xCD }, { AddressingMode.AbsoluteX, 0xDD }, { AddressingMode.AbsoluteY, 0xD9 },
            { AddressingMode.IndirectX, 0xC1 }, { AddressingMode.IndirectY, 0xD1 },
        } },
        { "CPX", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0xE0 }, { AddressingMode.ZeroPage, 0xE4 }, { AddressingMode.Absolute, 0xEC },
        } },
        { "CPY", new Dictionary<AddressingMode, byte> {
            { AddressingMode.Immediate, 0xC0 }, { AddressingMode.ZeroPage, 0xC4 }, { AddressingMode.Absolute, 0xCC },
        } },

        // Branch (relative addressing only)
        { "BCC", new Dictionary<AddressingMode, byte> { { AddressingMode.Relative, 0x90 } } },
        { "BCS", new Dictionary<AddressingMode, byte> { { AddressingMode.Relative, 0xB0 } } },
        { "BEQ", new Dictionary<AddressingMode, byte> { { AddressingMode.Relative, 0xF0 } } },
        { "BMI", new Dictionary<AddressingMode, byte> { { AddressingMode.Relative, 0x30 } } },
        { "BNE", new Dictionary<AddressingMode, byte> { { AddressingMode.Relative, 0xD0 } } },
        { "BPL", new Dictionary<AddressingMode, byte> { { AddressingMode.Relative, 0x10 } } },
        { "BVC", new Dictionary<AddressingMode, byte> { { AddressingMode.Relative, 0x50 } } },
        { "BVS", new Dictionary<AddressingMode, byte> { { AddressingMode.Relative, 0x70 } } },

        // Jump/Subroutine
        { "JMP", new Dictionary<AddressingMode, byte> { { AddressingMode.Absolute, 0x4C }, { AddressingMode.Indirect, 0x6C } } },
        { "JSR", new Dictionary<AddressingMode, byte> { { AddressingMode.Absolute, 0x20 } } },
        { "RTS", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x60 } } },
        { "RTI", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x40 } } },

        // Stack
        { "PHA", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x48 } } },
        { "PHP", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x08 } } },
        { "PLA", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x68 } } },
        { "PLP", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x28 } } },

        // Transfer
        { "TAX", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xAA } } },
        { "TAY", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xA8 } } },
        { "TXA", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x8A } } },
        { "TYA", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x98 } } },
        { "TSX", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xBA } } },
        { "TXS", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x9A } } },

        // Flags
        { "CLC", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x18 } } },
        { "CLD", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xD8 } } },
        { "CLI", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x58 } } },
        { "CLV", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xB8 } } },
        { "SEC", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x38 } } },
        { "SED", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xF8 } } },
        { "SEI", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x78 } } },

        // System
        { "BRK", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0x00 } } },
        { "NOP", new Dictionary<AddressingMode, byte> { { AddressingMode.Implied, 0xEA } } },
    };

    #endregion
}
