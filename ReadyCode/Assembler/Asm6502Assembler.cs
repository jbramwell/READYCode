// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;

namespace ReadyCode.Assembler;

/// <summary>
/// A two-pass assembler for standard 6502 mnemonics, labels, comments, and the ".byte" data
/// directive (no macros or other directives). Produces complete, ready-to-transfer .prg bytes:
/// a tiny tokenized BASIC loader stub ("10 SYS 2062") followed immediately by the assembled
/// machine code, so the result is an ordinary runnable C64 program - callers never need to know
/// about the stub trick.
/// </summary>
public class Asm6502Assembler
{
    #region Private Fields

    // "10 SYS 2062" tokenizes (via PrgConverter/BasicTokenizer) to a 15-byte stub PRG whose last
    // byte lands at memory $080D, so appended code starts at $080E = decimal 2062 - the same
    // number the stub SYS's into. This pairing is self-consistent for this codebase's exact
    // tokenizer and is pinned by Asm6502AssemblerTests.StubLine_ProducesFifteenByteStubLandingCodeAt080E;
    // if PrgConverter's byte layout ever changes, that test must fail before this constant is
    // silently wrong.
    private const string _basicStubLine = "10 SYS 2062";
    private const ushort _codeOrigin = 0x080E;

    #endregion

    #region Public Methods

    /// <summary>
    /// Assembles a complete 6502 source program.
    /// </summary>
    /// <param name="source">The assembly source text.</param>
    public AssemblyResult Assemble(string source)
    {
        var parser = new AsmLineParser();
        string[] rawLines = source.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        var parsedLines = new List<ParsedAsmLine>(rawLines.Length);
        for (int i = 0; i < rawLines.Length; i++)
            parsedLines.Add(parser.ParseLine(rawLines[i], i + 1));

        var errors = new List<AssemblyError>();

        // Pass 0: collect every "NAME = value" constant declaration up front, so constants can
        // be referenced before or after their declaration line, and so their (already-known)
        // value - unlike a label's, which depends on code layout - can be treated exactly like a
        // numeric literal for zero-page-eligibility purposes in pass 1 below. Case-sensitive
        // (like labels below) - unlike mnemonics, which are a small fixed vocabulary, symbol
        // names are user-chosen, and conventionally case-sensitive in real 6502 assemblers so
        // e.g. a "DELAY" constant and a "delay:" label can coexist as distinct symbols.
        var constants = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in parsedLines)
        {
            if (line.ConstantName == null) continue;
            if (!constants.TryAdd(line.ConstantName, line.ConstantValue!.Value))
                errors.Add(new AssemblyError(line.LineNumber, $"Duplicate constant \"{line.ConstantName}\"."));
        }

        // Case-sensitive, same reasoning as constants above.
        var labelAddresses = new Dictionary<string, ushort>(StringComparer.Ordinal);
        // Mode is null for a ".byte" data line - kept in the same ordered list as instruction
        // lines (rather than a separate list) so pass 2 emits data and code interleaved in
        // exactly the order they appear in source, not all instructions followed by all data.
        var encoded = new List<(ParsedAsmLine Line, ushort Address, AddressingMode? Mode)>();

        // Pass 1: assign every label's address and validate mnemonics/addressing modes. Sizing
        // never depends on a label's resolved value (see TryResolveMode), so this single forward
        // pass is enough - no iterative relaxation is needed.
        ushort address = _codeOrigin;
        foreach (var line in parsedLines)
        {
            if (line.Error != null)
            {
                errors.Add(new AssemblyError(line.LineNumber, line.Error));
                continue;
            }

            if (line.Label != null)
            {
                if (constants.ContainsKey(line.Label))
                    errors.Add(new AssemblyError(line.LineNumber, $"\"{line.Label}\" is already defined as a constant."));
                else if (!labelAddresses.TryAdd(line.Label, address))
                    errors.Add(new AssemblyError(line.LineNumber, $"Duplicate label \"{line.Label}\"."));
            }

            if (line.ByteData != null)
            {
                encoded.Add((line, address, null));
                address += (ushort)line.ByteData.Count;
                continue;
            }

            if (line.Mnemonic == null) continue;

            if (!TryResolveMode(line, constants, out AddressingMode mode, out string? modeError))
            {
                errors.Add(new AssemblyError(line.LineNumber, modeError!));
                continue;
            }

            encoded.Add((line, address, mode));
            address += (ushort)mode.InstructionLength();
        }

        if (errors.Count > 0)
            return new AssemblyResult { Success = false, Errors = errors };

        // Pass 2: emit real bytes, resolving label references now that every address is known.
        var codeBytes = new List<byte>();
        foreach (var (line, lineAddress, mode) in encoded)
        {
            if (mode == null)
            {
                codeBytes.AddRange(line.ByteData!);
                continue;
            }

            codeBytes.Add(OpcodeTable.Modes[line.Mnemonic!][mode.Value]);
            EmitOperand(line, lineAddress, mode.Value, constants, labelAddresses, codeBytes, errors);
        }

        if (errors.Count > 0)
            return new AssemblyResult { Success = false, Errors = errors };

        byte[] stub = new PrgConverter().ConvertToPrg(_basicStubLine);
        return new AssemblyResult { Success = true, PrgBytes = [.. stub, .. codeBytes] };
    }

    #endregion

    #region Private Methods

    // Resolves a parsed line's operand shape into a concrete addressing mode, validating it
    // against the mnemonic's legal modes. A bare literal - or a reference to a known constant,
    // whose value (unlike a label's) is already known at this point regardless of code layout -
    // that fits a zero-page byte prefers the zero-page mode (if the mnemonic supports it); a
    // label reference always assembles absolute, even when its resolved address would fit in
    // zero page - see AsmLineParser's OperandForm doc comment for why this is deliberate.
    private static bool TryResolveMode(ParsedAsmLine line, IReadOnlyDictionary<string, int> constants, out AddressingMode mode, out string? error)
    {
        mode = default;
        error = null;

        if (!OpcodeTable.Modes.TryGetValue(line.Mnemonic!, out var legalModes))
        {
            error = $"Unknown mnemonic \"{line.Mnemonic}\".";
            return false;
        }

        switch (line.Form)
        {
            case OperandForm.None:
                mode = AddressingMode.Implied;
                break;
            case OperandForm.Accumulator:
                mode = AddressingMode.Accumulator;
                break;
            case OperandForm.Immediate:
            case OperandForm.ImmediateLowByte:
            case OperandForm.ImmediateHighByte:
                mode = AddressingMode.Immediate;
                break;
            case OperandForm.IndirectX:
                mode = AddressingMode.IndirectX;
                break;
            case OperandForm.IndirectY:
                mode = AddressingMode.IndirectY;
                break;
            case OperandForm.IndirectAbsolute:
                mode = AddressingMode.Indirect;
                break;

            case OperandForm.Address:
            case OperandForm.AddressX:
            case OperandForm.AddressY:
                if (legalModes.ContainsKey(AddressingMode.Relative))
                {
                    if (line.Form != OperandForm.Address)
                    {
                        error = "Branch operand cannot be indexed.";
                        return false;
                    }
                    mode = AddressingMode.Relative;
                    break;
                }

                (AddressingMode zp, AddressingMode abs) = line.Form switch
                {
                    OperandForm.AddressX => (AddressingMode.ZeroPageX, AddressingMode.AbsoluteX),
                    OperandForm.AddressY => (AddressingMode.ZeroPageY, AddressingMode.AbsoluteY),
                    _ => (AddressingMode.ZeroPage, AddressingMode.Absolute),
                };

                // A known constant's value (plus any "+N" offset) is substituted here exactly
                // like a literal; only an unresolved label reference (not yet a known value) is
                // forced absolute.
                bool isDeferredLabel = line.SymbolName != null && !constants.ContainsKey(line.SymbolName);
                int? effectiveValue = line.SymbolName != null && constants.TryGetValue(line.SymbolName, out int constValue)
                    ? constValue + line.SymbolOffset
                    : line.NumericValue;

                bool zeroPageEligible = !isDeferredLabel
                    && effectiveValue is >= 0 and <= 0xFF
                    && legalModes.ContainsKey(zp);
                mode = zeroPageEligible ? zp : abs;
                break;

            default:
                error = "Malformed operand.";
                return false;
        }

        if (!legalModes.ContainsKey(mode))
        {
            error = $"Addressing mode {mode} is not valid for {line.Mnemonic}.";
            return false;
        }

        return true;
    }

    // Emits the operand byte(s) for an already-resolved addressing mode, appending directly to
    // codeBytes and recording any resolution/range error onto errors rather than throwing -
    // Assemble() collects every problem across the whole program before failing.
    private static void EmitOperand(
        ParsedAsmLine line, ushort lineAddress, AddressingMode mode, IReadOnlyDictionary<string, int> constants,
        IReadOnlyDictionary<string, ushort> labelAddresses, List<byte> codeBytes, List<AssemblyError> errors)
    {
        switch (mode)
        {
            case AddressingMode.Implied:
            case AddressingMode.Accumulator:
                return;

            case AddressingMode.Relative:
                if (!TryResolveValue(line, constants, labelAddresses, out int target, out string? targetError))
                {
                    errors.Add(new AssemblyError(line.LineNumber, targetError!));
                    return;
                }

                int offset = target - (lineAddress + 2);
                if (offset is < -128 or > 127)
                {
                    errors.Add(new AssemblyError(line.LineNumber,
                        $"Branch target out of range (offset {offset}) - must be within -128..127 of the instruction following the branch."));
                    return;
                }

                codeBytes.Add(unchecked((byte)(sbyte)offset));
                return;

            case AddressingMode.Immediate:
                if (!TryResolveValue(line, constants, labelAddresses, out int immValue, out string? immError))
                {
                    errors.Add(new AssemblyError(line.LineNumber, immError!));
                    return;
                }

                // "#<expr"/"#>expr" take the low/high byte of a (possibly 16-bit) resolved value -
                // masking always yields a valid byte, so the range check below only applies to a
                // plain "#expr" immediate, which must already fit in a byte on its own.
                int maskedValue = line.Form switch
                {
                    OperandForm.ImmediateLowByte => immValue & 0xFF,
                    OperandForm.ImmediateHighByte => (immValue >> 8) & 0xFF,
                    _ => immValue,
                };

                if (line.Form == OperandForm.Immediate && maskedValue is < 0 or > 0xFF)
                {
                    errors.Add(new AssemblyError(line.LineNumber, $"Value {maskedValue} does not fit an 8-bit operand (0-255)."));
                    return;
                }

                codeBytes.Add((byte)maskedValue);
                return;

            case AddressingMode.ZeroPage:
            case AddressingMode.ZeroPageX:
            case AddressingMode.ZeroPageY:
            case AddressingMode.IndirectX:
            case AddressingMode.IndirectY:
                if (!TryResolveValue(line, constants, labelAddresses, out int byteValue, out string? byteError))
                {
                    errors.Add(new AssemblyError(line.LineNumber, byteError!));
                    return;
                }

                if (byteValue is < 0 or > 0xFF)
                {
                    errors.Add(new AssemblyError(line.LineNumber, $"Value {byteValue} does not fit an 8-bit operand (0-255)."));
                    return;
                }

                codeBytes.Add((byte)byteValue);
                return;

            case AddressingMode.Absolute:
            case AddressingMode.AbsoluteX:
            case AddressingMode.AbsoluteY:
            case AddressingMode.Indirect:
                if (!TryResolveValue(line, constants, labelAddresses, out int wordValue, out string? wordError))
                {
                    errors.Add(new AssemblyError(line.LineNumber, wordError!));
                    return;
                }

                if (wordValue is < 0 or > 0xFFFF)
                {
                    errors.Add(new AssemblyError(line.LineNumber, $"Value {wordValue} does not fit a 16-bit operand (0-65535)."));
                    return;
                }

                codeBytes.Add((byte)(wordValue & 0xFF));
                codeBytes.Add((byte)((wordValue >> 8) & 0xFF));
                return;
        }
    }

    // Resolves a parsed line's operand to its final numeric value - the literal itself, a known
    // constant's value, or a label's address looked up now that every label from pass 1 is known.
    // Any "+N"/"-N" offset (e.g. the "+1" in "msgptr+1") is added on top of a symbol's resolved
    // value here; for a plain numeric literal, an offset was already folded into NumericValue
    // during parsing.
    private static bool TryResolveValue(
        ParsedAsmLine line, IReadOnlyDictionary<string, int> constants, IReadOnlyDictionary<string, ushort> labelAddresses,
        out int value, out string? error)
    {
        error = null;

        if (line.SymbolName != null)
        {
            if (constants.TryGetValue(line.SymbolName, out int constValue))
            {
                value = constValue + line.SymbolOffset;
                return true;
            }

            if (!labelAddresses.TryGetValue(line.SymbolName, out ushort labelAddress))
            {
                value = 0;
                error = $"Undefined label \"{line.SymbolName}\".";
                return false;
            }

            value = labelAddress + line.SymbolOffset;
            return true;
        }

        value = line.NumericValue ?? 0;
        return true;
    }

    #endregion
}
