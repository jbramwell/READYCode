// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace ReadyCode.Assembler;

/// <summary>
/// Disassembles raw 6502 machine code back into READYCode's assembly source syntax - the
/// reverse of <see cref="Asm6502Assembler"/>. Only the 56 official opcodes are recognized (same
/// coverage as the assembler); any other byte, or a recognized opcode whose operand runs past
/// the end of the buffer, is emitted as a ".byte" line instead so no input byte is ever lost.
/// </summary>
public class Asm6502Disassembler
{
    #region Private Fields

    // Built once from OpcodeTable.Modes (mnemonic -> mode -> opcode) inverted into the
    // opcode -> (mnemonic, mode) lookup a disassembler actually needs. Every official opcode
    // maps to exactly one mnemonic/mode pair, so the inversion is lossless.
    private static readonly IReadOnlyDictionary<byte, (string Mnemonic, AddressingMode Mode)> _byOpcode = BuildOpcodeLookup();

    #endregion

    #region Public Methods

    /// <summary>
    /// Disassembles a byte range into READYCode assembly source text, starting with an ".org"
    /// directive for <paramref name="startAddress"/> so re-assembling the output reproduces the
    /// same addresses.
    /// </summary>
    /// <param name="bytes">The raw bytes to disassemble.</param>
    /// <param name="startAddress">The memory address of the first byte.</param>
    /// <param name="mnemonicIndentColumn">
    /// The column mnemonics/".byte" lines are indented to (see
    /// <c>AppSettings.AsmMnemonicIndentColumn</c>).
    /// </param>
    /// <param name="commentAlignColumn">
    /// The column the trailing address/bytes comment is aligned to (see
    /// <c>AppSettings.AsmCommentAlignColumn</c>).
    /// </param>
    /// <returns>The disassembled source text and its per-line address map.</returns>
    public DisassemblyResult Disassemble(byte[] bytes, ushort startAddress, int mnemonicIndentColumn = 9, int commentAlignColumn = 32)
    {
        var sb = new StringBuilder();
        var lineAddresses = new Dictionary<int, ushort>();
        int lineNumber = 1;
        string indent = new string(' ', Math.Max(0, mnemonicIndentColumn - 1));

        sb.Append(".org $").Append(startAddress.ToString("X4")).Append('\n');
        lineNumber++;
        sb.Append('\n');
        lineNumber++;

        int i = 0;
        while (i < bytes.Length)
        {
            ushort address = (ushort)(startAddress + i);
            byte opcode = bytes[i];

            if (_byOpcode.TryGetValue(opcode, out var entry) && i + entry.Mode.InstructionLength() <= bytes.Length)
            {
                int length = entry.Mode.InstructionLength();
                string operand = FormatOperand(entry.Mode, bytes, i, address);
                string rawBytes = FormatRawBytes(bytes, i, length);
                string codePart = indent + entry.Mnemonic + (operand.Length > 0 ? " " + operand : "");

                sb.Append(AlignComment(codePart, commentAlignColumn))
                  .Append("; $").Append(address.ToString("X4")).Append(": ").Append(rawBytes).Append('\n');
                lineAddresses[lineNumber] = address;
                lineNumber++;

                i += length;
            }
            else
            {
                string codePart = indent + ".byte $" + opcode.ToString("X2");

                sb.Append(AlignComment(codePart, commentAlignColumn))
                  .Append("; $").Append(address.ToString("X4")).Append('\n');
                lineAddresses[lineNumber] = address;
                lineNumber++;
                i++;
            }
        }

        return new DisassemblyResult { Source = sb.ToString(), LineAddresses = lineAddresses };
    }

    #endregion

    #region Private Methods

    // Pads codePart with spaces up to commentAlignColumn (so the trailing comment starts there),
    // or - if codePart already reaches or exceeds that column - just a two-space gap instead, the
    // same fallback a real assembler's listing would use for an unusually long line.
    private static string AlignComment(string codePart, int commentAlignColumn)
    {
        int targetLength = Math.Max(0, commentAlignColumn - 1);
        return codePart.Length < targetLength ? codePart.PadRight(targetLength) : codePart + "  ";
    }

    private static IReadOnlyDictionary<byte, (string, AddressingMode)> BuildOpcodeLookup()
    {
        var lookup = new Dictionary<byte, (string, AddressingMode)>();
        foreach (var (mnemonic, modes) in OpcodeTable.Modes)
            foreach (var (mode, opcode) in modes)
                lookup[opcode] = (mnemonic, mode);
        return lookup;
    }

    private static string FormatOperand(AddressingMode mode, byte[] bytes, int i, ushort instructionAddress) => mode switch
    {
        AddressingMode.Implied => "",
        AddressingMode.Accumulator => "A",
        AddressingMode.Immediate => $"#${bytes[i + 1]:X2}",
        AddressingMode.ZeroPage => $"${bytes[i + 1]:X2}",
        AddressingMode.ZeroPageX => $"${bytes[i + 1]:X2},X",
        AddressingMode.ZeroPageY => $"${bytes[i + 1]:X2},Y",
        AddressingMode.IndirectX => $"(${bytes[i + 1]:X2},X)",
        AddressingMode.IndirectY => $"(${bytes[i + 1]:X2}),Y",
        AddressingMode.Absolute => $"${ReadWord(bytes, i + 1):X4}",
        AddressingMode.AbsoluteX => $"${ReadWord(bytes, i + 1):X4},X",
        AddressingMode.AbsoluteY => $"${ReadWord(bytes, i + 1):X4},Y",
        AddressingMode.Indirect => $"(${ReadWord(bytes, i + 1):X4})",
        // The branch target is rendered as its resolved absolute address, not the raw signed
        // offset byte - both are valid operand syntax for a branch mnemonic (see
        // Asm6502Assembler.EmitOperand), but the resolved address is what a human reads the
        // listing as, and re-assembling it recomputes an identical offset.
        AddressingMode.Relative => $"${(ushort)(instructionAddress + 2 + unchecked((sbyte)bytes[i + 1])):X4}",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static ushort ReadWord(byte[] bytes, int offset) => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static string FormatRawBytes(byte[] bytes, int offset, int length)
        => string.Join(' ', bytes.Skip(offset).Take(length).Select(b => b.ToString("X2")));

    #endregion
}
