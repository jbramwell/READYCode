// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Tokenizer;

/// <summary>
/// Describes a single 6502 assembly mnemonic: the reference metadata used by completion and
/// hover tooltips.
/// </summary>
/// <param name="Snippet">The ghost-text/completion snippet, with '|' marking the caret position after insertion.</param>
/// <param name="Description">The reference description shown in tooltips and completion.</param>
/// <param name="Category">The reference grouping this mnemonic belongs to.</param>
public record MnemonicInfo(string Snippet, string Description, string Category);

/// <summary>
/// Standard 6502 assembly mnemonic definitions: all 56 official opcodes, independent of any
/// one assembler's directive or macro syntax.
/// </summary>
public static class AsmTokens
{
    #region Public Properties

    /// <summary>
    /// Single source of truth for every standard 6502 mnemonic and its reference metadata.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, MnemonicInfo> Mnemonics = new Dictionary<string, MnemonicInfo>(StringComparer.OrdinalIgnoreCase)
    {
        // Load/Store
        { "LDA", new("LDA #$|", "Loads a value into the accumulator (A). LDA #imm | zp | zp,X | abs | abs,X | abs,Y | (zp,X) | (zp),Y", "Load/Store") },
        { "LDX", new("LDX #$|", "Loads a value into the X register. LDX #imm | zp | zp,Y | abs | abs,Y",                              "Load/Store") },
        { "LDY", new("LDY #$|", "Loads a value into the Y register. LDY #imm | zp | zp,X | abs | abs,X",                              "Load/Store") },
        { "STA", new("STA $|",  "Stores the accumulator (A) to memory. STA zp | zp,X | abs | abs,X | abs,Y | (zp,X) | (zp),Y",         "Load/Store") },
        { "STX", new("STX $|",  "Stores the X register to memory. STX zp | zp,Y | abs",                                               "Load/Store") },
        { "STY", new("STY $|",  "Stores the Y register to memory. STY zp | zp,X | abs",                                               "Load/Store") },

        // Arithmetic
        { "ADC", new("ADC #$|", "Adds a value and the carry flag to the accumulator (A). ADC #imm | zp | zp,X | abs | abs,X | abs,Y | (zp,X) | (zp),Y", "Arithmetic") },
        { "SBC", new("SBC #$|", "Subtracts a value and the inverted carry flag from the accumulator (A). SBC #imm | zp | zp,X | abs | abs,X | abs,Y | (zp,X) | (zp),Y", "Arithmetic") },

        // Logical
        { "AND", new("AND #$|", "Performs a bitwise AND with the accumulator (A). AND #imm | zp | zp,X | abs | abs,X | abs,Y | (zp,X) | (zp),Y", "Logical") },
        { "ORA", new("ORA #$|", "Performs a bitwise OR with the accumulator (A). ORA #imm | zp | zp,X | abs | abs,X | abs,Y | (zp,X) | (zp),Y", "Logical") },
        { "EOR", new("EOR #$|", "Performs a bitwise exclusive-OR with the accumulator (A). EOR #imm | zp | zp,X | abs | abs,X | abs,Y | (zp,X) | (zp),Y", "Logical") },
        { "BIT", new("BIT $|",  "Tests bits: ANDs A with memory (result discarded) and sets N/V/Z from the operand. BIT zp | abs", "Logical") },

        // Shift/Rotate
        { "ASL", new("ASL $|", "Shifts a value left one bit, into the carry flag. ASL A | zp | zp,X | abs | abs,X",       "Shift/Rotate") },
        { "LSR", new("LSR $|", "Shifts a value right one bit, into the carry flag. LSR A | zp | zp,X | abs | abs,X",      "Shift/Rotate") },
        { "ROL", new("ROL $|", "Rotates a value left one bit through the carry flag. ROL A | zp | zp,X | abs | abs,X",    "Shift/Rotate") },
        { "ROR", new("ROR $|", "Rotates a value right one bit through the carry flag. ROR A | zp | zp,X | abs | abs,X",   "Shift/Rotate") },

        // Increment/Decrement
        { "INC", new("INC $|", "Increments a memory location by one. INC zp | zp,X | abs | abs,X", "Increment/Decrement") },
        { "INX", new("INX",    "Increments the X register by one.",                                 "Increment/Decrement") },
        { "INY", new("INY",    "Increments the Y register by one.",                                 "Increment/Decrement") },
        { "DEC", new("DEC $|", "Decrements a memory location by one. DEC zp | zp,X | abs | abs,X", "Increment/Decrement") },
        { "DEX", new("DEX",    "Decrements the X register by one.",                                 "Increment/Decrement") },
        { "DEY", new("DEY",    "Decrements the Y register by one.",                                 "Increment/Decrement") },

        // Compare
        { "CMP", new("CMP #$|", "Compares the accumulator (A) with a value. CMP #imm | zp | zp,X | abs | abs,X | abs,Y | (zp,X) | (zp),Y", "Compare") },
        { "CPX", new("CPX #$|", "Compares the X register with a value. CPX #imm | zp | abs",   "Compare") },
        { "CPY", new("CPY #$|", "Compares the Y register with a value. CPY #imm | zp | abs",   "Compare") },

        // Branch (relative addressing only)
        { "BCC", new("BCC |", "Branches if the carry flag is clear. BCC label",    "Branch") },
        { "BCS", new("BCS |", "Branches if the carry flag is set. BCS label",      "Branch") },
        { "BEQ", new("BEQ |", "Branches if the zero flag is set (values equal). BEQ label", "Branch") },
        { "BMI", new("BMI |", "Branches if the negative flag is set. BMI label",   "Branch") },
        { "BNE", new("BNE |", "Branches if the zero flag is clear (values not equal). BNE label", "Branch") },
        { "BPL", new("BPL |", "Branches if the negative flag is clear. BPL label", "Branch") },
        { "BVC", new("BVC |", "Branches if the overflow flag is clear. BVC label", "Branch") },
        { "BVS", new("BVS |", "Branches if the overflow flag is set. BVS label",   "Branch") },

        // Jump/Subroutine
        { "JMP", new("JMP $|", "Jumps to an address. JMP abs | (ind)", "Jump/Subroutine") },
        { "JSR", new("JSR |",  "Calls a subroutine at an address, pushing the return address. JSR abs", "Jump/Subroutine") },
        { "RTS", new("RTS",    "Returns from a subroutine.",           "Jump/Subroutine") },
        { "RTI", new("RTI",    "Returns from an interrupt handler, restoring flags and the return address.", "Jump/Subroutine") },

        // Stack
        { "PHA", new("PHA", "Pushes the accumulator (A) onto the stack.",         "Stack") },
        { "PHP", new("PHP", "Pushes the processor status flags onto the stack.", "Stack") },
        { "PLA", new("PLA", "Pulls a value from the stack into the accumulator (A).", "Stack") },
        { "PLP", new("PLP", "Pulls the processor status flags from the stack.",  "Stack") },

        // Transfer
        { "TAX", new("TAX", "Copies the accumulator (A) into the X register.", "Transfer") },
        { "TAY", new("TAY", "Copies the accumulator (A) into the Y register.", "Transfer") },
        { "TXA", new("TXA", "Copies the X register into the accumulator (A).", "Transfer") },
        { "TYA", new("TYA", "Copies the Y register into the accumulator (A).", "Transfer") },
        { "TSX", new("TSX", "Copies the stack pointer into the X register.",  "Transfer") },
        { "TXS", new("TXS", "Copies the X register into the stack pointer.", "Transfer") },

        // Flags
        { "CLC", new("CLC", "Clears the carry flag.",             "Flags") },
        { "CLD", new("CLD", "Clears the decimal mode flag.",       "Flags") },
        { "CLI", new("CLI", "Clears the interrupt-disable flag.", "Flags") },
        { "CLV", new("CLV", "Clears the overflow flag.",           "Flags") },
        { "SEC", new("SEC", "Sets the carry flag.",                "Flags") },
        { "SED", new("SED", "Sets the decimal mode flag.",         "Flags") },
        { "SEI", new("SEI", "Sets the interrupt-disable flag.",   "Flags") },

        // System
        { "BRK", new("BRK", "Forces a software interrupt.",  "System") },
        { "NOP", new("NOP", "Performs no operation.",         "System") },
    };

    /// <summary>
    /// Single source of truth for every assembler directive and its reference metadata.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, MnemonicInfo> Directives = new Dictionary<string, MnemonicInfo>(StringComparer.OrdinalIgnoreCase)
    {
        { ".org",  new(".org $|",  "Sets the assembly origin. Must be the first thing in the file; omits the BASIC loader stub and emits a raw load-address header instead.", "Directives") },
        { ".byte", new(".byte |",  "Emits literal byte data: quoted strings (one byte per character, no PETSCII remapping) and/or numeric literals, comma-separated.", "Directives") },
        { ".text", new(".text |",  "Alias of \".byte\" - identical grammar, used to signal that the data is text rather than raw bytes.", "Directives") },
        { ".word", new(".word |",  "Emits 16-bit little-endian data: numeric literals and/or label/constant references (with an optional +N/-N offset), comma-separated.", "Directives") },
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// Checks whether a word is a recognized 6502 mnemonic.
    /// </summary>
    public static bool IsMnemonic(string word) => Mnemonics.ContainsKey(word);

    #endregion
}
