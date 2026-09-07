// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;

namespace ReadyCode.Assembler;

/// <summary>
/// A two-pass assembler for standard 6502 mnemonics, labels, comments, and the ".org", ".byte",
/// ".text", and ".word" directives (no macros). By default, when no ".org" directive is present,
/// produces complete, ready-to-transfer .prg bytes: a tiny tokenized BASIC loader stub
/// ("10 SYS 2062") followed immediately by the assembled machine code, so the result is an
/// ordinary runnable C64 program - callers never need to know about the stub trick. When ".org"
/// is present, the stub is omitted and a raw 2-byte load-address header is emitted instead - see
/// the ".org" handling in <see cref="Assemble"/>. Passing <c>standaloneOutput: true</c> (see
/// <c>AppSettings.AsmOutputMode</c>) skips the stub the same way even without an explicit ".org",
/// using a caller-supplied default origin instead.
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
    /// <param name="standaloneOutput">
    /// When true and the source has no ".org" directive of its own, output is a raw .prg (2-byte
    /// load-address header, no BASIC loader stub) starting at <paramref name="defaultOriginAddress"/>,
    /// instead of the default auto-generated-stub behavior. An explicit ".org" in the source
    /// always wins over this, regardless of its value.
    /// </param>
    /// <param name="defaultOriginAddress">
    /// The origin to use when <paramref name="standaloneOutput"/> is true and the source has no
    /// ".org" of its own. Ignored otherwise.
    /// </param>
    public AssemblyResult Assemble(string source, bool standaloneOutput = false, ushort defaultOriginAddress = 0xC000)
    {
        var parser = new AsmLineParser();
        string[] rawLines = source.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        var parsedLines = new List<ParsedAsmLine>(rawLines.Length);
        for (int i = 0; i < rawLines.Length; i++)
            parsedLines.Add(parser.ParseLine(rawLines[i], i + 1));

        var errors = new List<AssemblyError>();

        // Pass 0: collect every "NAME = value" constant declaration whose value is a plain
        // numeric literal, up front, so - like a label - it can be referenced before or after its
        // declaration line, and its (already-known) value can be treated exactly like a numeric
        // literal for zero-page-eligibility purposes in pass 1 below. Case-sensitive (like labels
        // below) - unlike mnemonics, which are a small fixed vocabulary, symbol names are
        // user-chosen, and conventionally case-sensitive in real 6502 assemblers so e.g. a
        // "DELAY" constant and a "delay:" label can coexist as distinct symbols.
        //
        // A constant whose value is "*" or another symbol (KickAssembler's ".label" idiom, e.g.
        // ".label data = *" or ".label datsaucxlo = data+1") depends on code layout - the current
        // address, or another symbol's - so it can't be resolved yet here; those are instead
        // resolved during pass 1 below, in file order as the address counter and every earlier
        // label/constant become known. That does mean such a constant can only reference a symbol
        // already defined earlier in the file, unlike a plain-literal constant or a label.
        var constants = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in parsedLines)
        {
            if (line.ConstantName == null || line.SymbolName != null || line.ConstantIsCurrentAddress || line.OffsetSymbolName != null) continue;
            if (!constants.TryAdd(line.ConstantName, line.NumericValue!.Value))
                errors.Add(new AssemblyError(line.LineNumber, $"Duplicate constant \"{line.ConstantName}\"."));
        }

        // Pre-pass: locate an optional ".org" directive. It must be the first thing in the file -
        // before any label, mnemonic, ".byte", or ".word" line - since it retargets where code is
        // placed and (unlike every other directive) changes what gets emitted at the very start
        // of the .prg: a custom origin disables the auto-generated BASIC loader stub entirely,
        // emitting a raw 2-byte load-address header instead (see the end of this method).
        ushort? customOrigin = null;
        bool codeSeen = false;
        foreach (var line in parsedLines)
        {
            if (line.OrgAddress != null)
            {
                if (customOrigin != null)
                    errors.Add(new AssemblyError(line.LineNumber, "Duplicate '.org' directive."));
                else if (codeSeen)
                    errors.Add(new AssemblyError(line.LineNumber, "'.org' must appear before any code in the file."));
                else
                    customOrigin = (ushort)line.OrgAddress.Value;
            }

            if (line.Label != null || line.Mnemonic != null || line.ByteData != null || line.WordData != null)
                codeSeen = true;
        }

        ushort origin = customOrigin ?? (standaloneOutput ? defaultOriginAddress : _codeOrigin);

        // Case-sensitive, same reasoning as constants above.
        var labelAddresses = new Dictionary<string, ushort>(StringComparer.Ordinal);
        // Mode is null for a ".byte"/".word" data line - kept in the same ordered list as
        // instruction lines (rather than a separate list) so pass 2 emits data and code
        // interleaved in exactly the order they appear in source, not all instructions followed
        // by all data.
        var encoded = new List<(ParsedAsmLine Line, ushort Address, AddressingMode? Mode)>();

        // Pass 1: assign every label's address and validate mnemonics/addressing modes. Sizing
        // never depends on a label's resolved value (see TryResolveMode), so this single forward
        // pass is enough - no iterative relaxation is needed.
        ushort address = origin;
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

            // A "*"/symbol-referencing constant (see the pass-0 comment above) - resolved now,
            // against whatever's already known at this exact point in the file. Declares no
            // bytes and doesn't advance the address counter, same as a plain-literal constant.
            if (line.ConstantName != null && (line.SymbolName != null || line.ConstantIsCurrentAddress || line.OffsetSymbolName != null))
            {
                if (labelAddresses.ContainsKey(line.ConstantName))
                    errors.Add(new AssemblyError(line.LineNumber, $"\"{line.ConstantName}\" is already defined as a label."));
                else if (!TryResolveConstantValue(line, constants, labelAddresses, address, out int constValue, out string? constError))
                    errors.Add(new AssemblyError(line.LineNumber, constError!));
                else if (!constants.TryAdd(line.ConstantName, constValue))
                    errors.Add(new AssemblyError(line.LineNumber, $"Duplicate constant \"{line.ConstantName}\"."));
                continue;
            }

            if (line.ByteData != null)
            {
                encoded.Add((line, address, null));
                address += (ushort)line.ByteData.Count;
                continue;
            }

            if (line.WordData != null)
            {
                encoded.Add((line, address, null));
                address += (ushort)(2 * line.WordData.Count);
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
            return new AssemblyResult { Success = false, Errors = errors, Origin = origin, HasExplicitOrigin = customOrigin != null, Labels = labelAddresses, Constants = constants, ParsedLines = parsedLines };

        // Pass 2: emit real bytes, resolving label references now that every address is known.
        var codeBytes = new List<byte>();
        var listingEntries = new List<AsmListingEntry>();
        foreach (var (line, lineAddress, mode) in encoded)
        {
            int startIndex = codeBytes.Count;

            if (mode == null)
            {
                if (line.ByteData != null)
                {
                    codeBytes.AddRange(line.ByteData);
                }
                else
                {
                    foreach (var entry in line.WordData!)
                    {
                        if (!TryResolveValue(entry.SymbolName, entry.NumericValue, entry.SymbolOffset, lineAddress, constants, labelAddresses, out int wordValue, out string? wordError))
                        {
                            errors.Add(new AssemblyError(line.LineNumber, wordError!));
                            continue;
                        }

                        if (wordValue is < 0 or > 0xFFFF)
                        {
                            errors.Add(new AssemblyError(line.LineNumber, $"Value {wordValue} does not fit a 16-bit operand (0-65535)."));
                            continue;
                        }

                        codeBytes.Add((byte)(wordValue & 0xFF));
                        codeBytes.Add((byte)((wordValue >> 8) & 0xFF));
                    }
                }
            }
            else
            {
                codeBytes.Add(OpcodeTable.Modes[line.Mnemonic!][mode.Value]);
                EmitOperand(line, lineAddress, mode.Value, constants, labelAddresses, codeBytes, errors);
            }

            if (codeBytes.Count > startIndex)
                listingEntries.Add(new AsmListingEntry(line.LineNumber, lineAddress, codeBytes.GetRange(startIndex, codeBytes.Count - startIndex)));
        }

        if (errors.Count > 0)
            return new AssemblyResult { Success = false, Errors = errors, Origin = origin, HasExplicitOrigin = customOrigin != null, Labels = labelAddresses, Constants = constants, ParsedLines = parsedLines };

        byte[] prgBytes;
        if (customOrigin != null || standaloneOutput)
        {
            byte[] header = [(byte)(origin & 0xFF), (byte)((origin >> 8) & 0xFF)];
            prgBytes = [.. header, .. codeBytes];
        }
        else
        {
            byte[] stub = new PrgConverter().ConvertToPrg(_basicStubLine);
            prgBytes = [.. stub, .. codeBytes];
        }

        return new AssemblyResult
        {
            Success = true, PrgBytes = prgBytes, Origin = origin, HasExplicitOrigin = customOrigin != null,
            Labels = labelAddresses, Constants = constants, ListingEntries = listingEntries, ParsedLines = parsedLines,
        };
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
                // ASL/LSR/ROL/ROR have no Implied form - only Accumulator and memory modes - but
                // many real-world sources (Merlin among them) write these with no operand at all
                // to mean the accumulator, same as writing "A" explicitly. Falling back to
                // Accumulator here when Implied isn't legal covers that convention; any other
                // mnemonic that genuinely needs a real operand still fails the legalModes check
                // below with today's same clear error.
                mode = legalModes.ContainsKey(AddressingMode.Implied) ? AddressingMode.Implied : AddressingMode.Accumulator;
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

                // A literal written with more than 2 hex digits (e.g. "$00F0") explicitly asks for
                // an absolute operand even though the value itself would fit zero-page - without
                // this, a disassembly listing's absolute-mode instructions targeting low memory
                // (a common, legitimate pattern - Asm6502Disassembler always emits 4-digit operands
                // for them) would silently narrow to zero-page on reassembly, shrinking that
                // instruction by a byte and shifting every address after it, eventually breaking
                // unrelated branches elsewhere in the file once enough drift accumulates.
                bool zeroPageEligible = !isDeferredLabel
                    && !line.OperandIsWideHexLiteral
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
                if (!TryResolveValue(line.SymbolName, line.NumericValue, line.SymbolOffset, lineAddress, constants, labelAddresses, out int target, out string? targetError))
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
                if (!TryResolveValue(line.SymbolName, line.NumericValue, line.SymbolOffset, lineAddress, constants, labelAddresses, out int immValue, out string? immError))
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
                if (!TryResolveValue(line.SymbolName, line.NumericValue, line.SymbolOffset, lineAddress, constants, labelAddresses, out int byteValue, out string? byteError))
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
                if (!TryResolveValue(line.SymbolName, line.NumericValue, line.SymbolOffset, lineAddress, constants, labelAddresses, out int wordValue, out string? wordError))
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

    // Resolves an operand (an instruction's, or a ".word" entry's) to its final numeric value -
    // the literal itself, a known constant's value, a label's address looked up now that every
    // label from pass 1 is known, or - for the reserved symbol "*" (see AsmLineParser.TryParseValue) -
    // currentAddress, the address of the instruction/".word" entry being resolved right now (e.g.
    // "JMP *", a common self-loop idiom). Any "+N"/"-N" offset (e.g. the "+1" in "msgptr+1") is
    // added on top of a symbol's resolved value here; for a plain numeric literal, an offset was
    // already folded into numericValue during parsing. Takes the operand's fields directly
    // (rather than a whole ParsedAsmLine) so both instruction operands and ".word" entries
    // (AsmWordEntry) can share this exact resolution logic.
    private static bool TryResolveValue(
        string? symbolName, int? numericValue, int symbolOffset, ushort currentAddress,
        IReadOnlyDictionary<string, int> constants, IReadOnlyDictionary<string, ushort> labelAddresses,
        out int value, out string? error)
    {
        error = null;

        if (symbolName == "*")
        {
            value = currentAddress + symbolOffset;
            return true;
        }

        if (symbolName != null)
        {
            if (constants.TryGetValue(symbolName, out int constValue))
            {
                value = constValue + symbolOffset;
                return true;
            }

            if (!labelAddresses.TryGetValue(symbolName, out ushort labelAddress))
            {
                value = 0;
                error = $"Undefined label \"{symbolName}\".";
                return false;
            }

            value = labelAddress + symbolOffset;
            return true;
        }

        value = numericValue ?? 0;
        return true;
    }

    // Resolves a "*"/symbol-referencing constant's value (see the pass-0 comment above it in
    // Assemble), against whatever's already resolved at this exact point in pass 1: the current
    // address counter for "*", or - via the exact same lookup a regular operand uses - an
    // earlier constant's or label's value for a symbol reference. The base term (SymbolName/
    // ConstantIsCurrentAddress/NumericValue) is resolved first; an OffsetSymbolName term (e.g.
    // the "start" in "size = * - start") is then resolved the same way and added or subtracted
    // per OffsetIsNegative, taking priority over a plain-integer SymbolOffset when present -
    // AsmLineParser only ever sets one or the other, never both.
    private static bool TryResolveConstantValue(
        ParsedAsmLine line, IReadOnlyDictionary<string, int> constants, IReadOnlyDictionary<string, ushort> labelAddresses,
        ushort currentAddress, out int value, out string? error)
    {
        value = 0;

        int baseValue;
        if (line.ConstantIsCurrentAddress)
        {
            baseValue = currentAddress;
            error = null;
        }
        else if (line.SymbolName != null)
        {
            if (!TryResolveValue(line.SymbolName, null, 0, currentAddress, constants, labelAddresses, out baseValue, out error))
                return false;
        }
        else
        {
            baseValue = line.NumericValue!.Value;
            error = null;
        }

        if (line.OffsetSymbolName == null)
        {
            value = baseValue + line.SymbolOffset;
            return true;
        }

        if (!TryResolveValue(line.OffsetSymbolName, null, 0, currentAddress, constants, labelAddresses, out int offsetValue, out error))
            return false;

        value = line.OffsetIsNegative ? baseValue - offsetValue : baseValue + offsetValue;
        return true;
    }

    #endregion
}
