// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace ReadyCode.Assembler;

/// <summary>
/// The syntactic shape of an instruction's operand, as written in source. Combined with the
/// mnemonic's legal <see cref="AddressingMode"/> set, this determines the actual addressing mode -
/// e.g. <see cref="Address"/> resolves to zero-page or absolute depending on the operand's value
/// and what the mnemonic supports.
/// </summary>
public enum OperandForm
{
    /// <summary>No operand text (implied addressing).</summary>
    None,

    /// <summary>The literal accumulator operand "A".</summary>
    Accumulator,

    /// <summary>An immediate operand ("#...").</summary>
    Immediate,

    /// <summary>An immediate operand taking the low byte of a 16-bit value ("#&lt;...").</summary>
    ImmediateLowByte,

    /// <summary>An immediate operand taking the high byte of a 16-bit value ("#&gt;...").</summary>
    ImmediateHighByte,

    /// <summary>A zero-page indirect operand indexed by X before dereferencing ("(...,X)").</summary>
    IndirectX,

    /// <summary>A zero-page indirect operand indexed by Y after dereferencing ("(...),Y").</summary>
    IndirectY,

    /// <summary>An indirect absolute operand ("(...)"), legal only for JMP.</summary>
    IndirectAbsolute,

    /// <summary>A bare address/value operand with no index.</summary>
    Address,

    /// <summary>An address/value operand indexed by X ("...,X").</summary>
    AddressX,

    /// <summary>An address/value operand indexed by Y ("...,Y").</summary>
    AddressY,
}

/// <summary>
/// A single resolved-or-symbolic entry in a ".word" directive's data list.
/// </summary>
/// <param name="NumericValue">The entry's numeric value, or null if it references a symbol.</param>
/// <param name="SymbolName">The entry's referenced symbol name, or null if it is a numeric literal.</param>
/// <param name="SymbolOffset">A constant integer added to <paramref name="SymbolName"/>'s resolved value (e.g. the "+1" in "msgptr+1"), 0 if none.</param>
public sealed record AsmWordEntry(int? NumericValue, string? SymbolName, int SymbolOffset = 0);

/// <summary>
/// The result of parsing a single line of 6502 assembly source.
/// </summary>
/// <param name="LineNumber">1-based source line number.</param>
/// <param name="Label">The label defined on this line (before the colon), or null if none.</param>
/// <param name="Mnemonic">The instruction mnemonic, or null for a blank/label-only/comment-only line.</param>
/// <param name="Form">The operand's syntactic shape. Meaningless when <paramref name="Mnemonic"/> is null.</param>
/// <param name="NumericValue">
/// For an instruction line, the operand's resolved numeric value (null if it was a symbol
/// reference or there is no operand). For a "NAME = value"/".label" constant declaration line
/// (<paramref name="ConstantName"/> non-null) whose value is a plain numeric literal, that value -
/// null if the constant's value is a symbol reference or <paramref name="ConstantIsCurrentAddress"/>.
/// </param>
/// <param name="SymbolName">
/// For an instruction line, the operand's referenced symbol name (null if numeric or no operand).
/// For a constant declaration whose value references another symbol (e.g. the KickAssembler-style
/// ".label datsaucxlo = data+1"), that symbol's name - null for a plain literal or "*".
/// </param>
/// <param name="Error">A description of why this line could not be parsed, or null if parsing succeeded.</param>
/// <param name="SymbolOffset">
/// A constant integer added to the resolved value of <paramref name="SymbolName"/> (or, for a
/// constant whose value is "*"/"*+N", to the current address) - e.g. the "+1" in "msgptr+1". 0 if
/// none.
/// </param>
/// <param name="ConstantName">The name being defined, for a "NAME = value"/".label" constant declaration line, or null otherwise.</param>
/// <param name="ByteData">The literal bytes to emit, for a ".byte" or ".text" directive line, or null otherwise.</param>
/// <param name="OrgAddress">The requested origin address, for an ".org" directive line, or null otherwise.</param>
/// <param name="WordData">The literal-or-symbolic 16-bit entries to emit, for a ".word" directive line, or null otherwise.</param>
/// <param name="ConstantIsCurrentAddress">
/// True when a constant declaration's value (or its base term, if an <paramref name="OffsetSymbolName"/>
/// or <paramref name="SymbolOffset"/> follows) is "*" - KickAssembler's "current program counter"
/// pseudo-symbol, e.g. ".label data = *". Always false outside a constant declaration.
/// </param>
/// <param name="OffsetSymbolName">
/// For a constant declaration whose value has the form "A - B"/"A + B" where B is itself a
/// symbol or "*" (e.g. "size = * - start"), B's name (or "*") - null when there is no offset
/// term, or when it's a plain integer already folded into <paramref name="SymbolOffset"/>.
/// Always null outside a constant declaration; an instruction/".word" operand's offset is
/// always a plain integer.
/// </param>
/// <param name="OffsetIsNegative">
/// True when <paramref name="OffsetSymbolName"/>'s resolved value is subtracted from the base
/// rather than added (e.g. the "-" in "* - start"). Meaningless when <paramref name="OffsetSymbolName"/> is null.
/// </param>
/// <param name="OperandIsWideHexLiteral">
/// True when an instruction's <see cref="OperandForm.Address"/>/<see cref="OperandForm.AddressX"/>/
/// <see cref="OperandForm.AddressY"/> operand was written as a "$" hex literal with more than 2
/// digits (e.g. "$00F0"), the standard
/// convention - also used by Asm6502Disassembler's own output - for explicitly requesting a
/// 2-byte/absolute operand even when the value itself would otherwise fit a single byte. Used by
/// Asm6502Assembler.TryResolveMode to avoid silently narrowing such an operand to zero-page, which
/// would shrink the instruction by a byte and desynchronize every address after it. Always false
/// for a symbol reference, a non-hex literal, or outside an instruction operand.
/// </param>
public sealed record ParsedAsmLine(
    int LineNumber,
    string? Label,
    string? Mnemonic,
    OperandForm Form,
    int? NumericValue,
    string? SymbolName,
    string? Error,
    string? ConstantName = null,
    IReadOnlyList<byte>? ByteData = null,
    int SymbolOffset = 0,
    int? OrgAddress = null,
    IReadOnlyList<AsmWordEntry>? WordData = null,
    bool ConstantIsCurrentAddress = false,
    string? OffsetSymbolName = null,
    bool OffsetIsNegative = false,
    bool OperandIsWideHexLiteral = false);

/// <summary>
/// Parses a single line of 6502 assembly source into its label, mnemonic, and operand shape.
/// Has no knowledge of legal addressing modes per mnemonic or label addresses - resolving the
/// operand shape into a final <see cref="AddressingMode"/> is <see cref="Asm6502Assembler"/>'s job.
/// The one exception to this line-at-a-time independence is ".encoding" (see the private
/// <c>_encoding</c> field), which must be driven as a single instance across a whole file in
/// source order, same as <see cref="Asm6502Assembler.Assemble"/> already does.
/// </summary>
public class AsmLineParser
{
    #region Private Fields

    private static readonly char[] _whitespaceChars = [' ', '\t'];

    private static readonly Regex _labelPattern = new(@"^\s*([A-Za-z_][A-Za-z0-9_]*):", RegexOptions.Compiled);
    private static readonly Regex _constantPattern = new(@"^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex _symbolPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex _indirectYPattern = new(@"^\((.+)\)\s*,\s*[Yy]$", RegexOptions.Compiled);
    private static readonly Regex _indirectXPattern = new(@"^\((.+),\s*[Xx]\)$", RegexOptions.Compiled);
    private static readonly Regex _indirectAbsPattern = new(@"^\((.+)\)$", RegexOptions.Compiled);
    private static readonly Regex _indexedPattern = new(@"^(.+?)\s*,\s*([XxYy])$", RegexOptions.Compiled);

    // Set by an ".encoding" directive and applied to every ".byte"/".text" string literal from
    // that point in the source onward, until the next ".encoding" - the one piece of state this
    // parser carries across lines (see the class doc comment), relying on Asm6502Assembler always
    // driving a single instance through the whole file in source order.
    private AsmTextEncoding _encoding = AsmTextEncoding.Ascii;

    #endregion

    #region Public Methods

    /// <summary>
    /// Parses a single source line (with no line-ending characters).
    /// </summary>
    /// <param name="rawLine">The raw source line text.</param>
    /// <param name="lineNumber">The 1-based source line number, used for error reporting.</param>
    public ParsedAsmLine ParseLine(string rawLine, int lineNumber)
    {
        int commentIdx = rawLine.IndexOf(';');
        string code = commentIdx >= 0 ? rawLine[..commentIdx] : rawLine;

        string? label = null;
        var labelMatch = _labelPattern.Match(code);
        if (labelMatch.Success)
        {
            label = labelMatch.Groups[1].Value;
            code = code[labelMatch.Length..];
        }

        code = code.Trim();
        if (code.Length == 0)
            return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, null);

        // ".org" directive (e.g. .org $2000) - sets the assembly origin. Only a single numeric
        // literal is accepted (a symbol reference is nonsensical here, since a label's own
        // address depends on the origin). Checked before the mnemonic/operand split below since
        // it isn't a real mnemonic.
        if (IsDirective(code, ".org", out string orgArgs))
        {
            if (!TryParseValue(orgArgs, out int orgValue, out string? orgSymbol, out _) || orgSymbol != null)
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                    $"'.org' value \"{orgArgs}\" must be a numeric literal.");
            if (orgValue is < 0 or > 0xFFFF)
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                    $"'.org' value {orgValue} does not fit a 16-bit address (0-65535).");

            return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, null, OrgAddress: orgValue);
        }

        // "* = value" (e.g. "* = $c000") - an alternate spelling of ".org value", ported from
        // assemblers (KickAssembler among them) that use "*" for the current program counter and
        // let assigning straight to it retarget the origin. Same rules as ".org": a numeric
        // literal only, and Asm6502Assembler enforces it must be the first thing in the file (it
        // reads the exact same OrgAddress field, so both spellings share that check for free).
        if (code.StartsWith('*'))
        {
            string afterStar = code[1..].TrimStart();
            if (afterStar.StartsWith('='))
            {
                string starOrgArgs = afterStar[1..].Trim();
                if (!TryParseValue(starOrgArgs, out int starOrgValue, out string? starOrgSymbol, out _) || starOrgSymbol != null)
                    return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                        $"'* =' value \"{starOrgArgs}\" must be a numeric literal.");
                if (starOrgValue is < 0 or > 0xFFFF)
                    return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                        $"'* =' value {starOrgValue} does not fit a 16-bit address (0-65535).");

                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, null, OrgAddress: starOrgValue);
            }
        }

        // ".encoding \"mode\"" (KickAssembler-style, e.g. .encoding "petscii_mixed") - changes how
        // every ".byte"/".text" string literal from here to the next ".encoding" (or end of file)
        // converts its characters to bytes. Recognized mode names: "ascii" (the default - a plain
        // character code, unchanged), "petscii_upper", "petscii_mixed", "screencode_upper",
        // "screencode_mixed" - see AsmTextEncoding for what each one means. Declares no bytes of
        // its own and doesn't advance the address counter, same as any other pure directive.
        if (IsDirective(code, ".encoding", out string encodingArgs))
        {
            if (!TryParseQuotedString(encodingArgs, out string modeText))
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                    $"'.encoding' value \"{encodingArgs}\" must be a quoted string.");
            if (!TryParseEncodingMode(modeText, out AsmTextEncoding mode))
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                    $"Unknown '.encoding' mode \"{modeText}\".");

            _encoding = mode;
            return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, null);
        }

        // ".byte"/".text" directives (e.g. .byte "HELLO", $0d, $00) - a comma-separated list of
        // quoted strings (each character becomes a byte per the active ".encoding" above - a
        // plain character code, unchanged, unless ".encoding" has set something else) and/or
        // numeric literals. ".text" is a pure alias of ".byte" - both directive names exist only
        // to let source express intent (data vs. text), the grammar is identical. Checked before
        // the mnemonic/operand split below since these directives have their own comma-delimited
        // grammar rather than a single operand.
        if (IsDirective(code, ".byte", out string byteArgs) || IsDirective(code, ".text", out byteArgs))
        {
            if (!TryParseByteDirective(byteArgs, _encoding, out List<byte>? byteData, out string? byteError))
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, byteError);

            return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, null, ByteData: byteData);
        }

        // ".word" directive (e.g. .word $1234, LABEL, LABEL+1) - a comma-separated list of
        // 16-bit numeric literals and/or symbol references (unlike ".byte", symbols are allowed,
        // since a jump/address table is ".word"'s main real-world use).
        if (IsDirective(code, ".word", out string wordArgs))
        {
            if (!TryParseWordDirective(wordArgs, out List<AsmWordEntry>? wordData, out string? wordError))
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, wordError);

            return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, null, WordData: wordData);
        }

        // ".label NAME = value" (KickAssembler-style constant declaration) is accepted as a
        // synonym for the bare "NAME = value" form immediately below - stripping the keyword and
        // falling through to the exact same parsing, including "*"/symbol-reference values.
        if (IsDirective(code, ".label", out string labelDeclText))
            code = labelDeclText;

        // "NAME = value" constant declaration (e.g. "chrout = $ffd2") - checked before the
        // mnemonic/operand split below, since without this check the '=' would otherwise be
        // mis-parsed as a nonsensical operand on a bogus "chrout" mnemonic. The value's base term
        // can be a plain numeric literal, a reference to another symbol, or "*" for the current
        // program counter (e.g. ".label data = *"); an optional "+"/"-" offset term follows the
        // same three shapes, so "data+1", "*+1", and "size = * - start" are all valid.
        // Asm6502Assembler resolves which of these it actually is - a symbol/"*" base or offset
        // needs to know code layout, a plain literal doesn't.
        var constMatch = _constantPattern.Match(code);
        if (constMatch.Success)
        {
            string constName = constMatch.Groups[1].Value;
            string valueText = constMatch.Groups[2].Value.Trim();

            if (!TrySplitExpressionTerms(valueText, out string baseText, out string? offsetText, out bool offsetIsNegative))
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                    $"Malformed constant value \"{valueText}\".");

            bool baseIsCurrentAddress = baseText == "*";
            int? baseNumeric = null;
            string? baseSymbol = null;
            if (!baseIsCurrentAddress)
            {
                if (!TryParseValue(baseText, out int parsedBase, out baseSymbol, out _))
                    return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                        $"Malformed constant value \"{valueText}\".");
                baseNumeric = baseSymbol == null ? parsedBase : null;
            }

            int literalOffset = 0;
            string? offsetSymbol = null;
            if (offsetText != null)
            {
                if (offsetText == "*" || _symbolPattern.IsMatch(offsetText))
                {
                    offsetSymbol = offsetText;
                }
                else if (int.TryParse(offsetIsNegative ? $"-{offsetText}" : offsetText, out int parsedOffset))
                {
                    literalOffset = parsedOffset;
                }
                else
                {
                    return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                        $"Malformed constant value \"{valueText}\".");
                }
            }

            // A plain literal base with no symbol/"*" anywhere folds the whole expression into a
            // single resolved number right here, exactly like before - everything else (a "*"
            // /symbol base, or a "*"/symbol offset) is deferred to Asm6502Assembler's pass 1,
            // since it depends on code layout.
            if (!baseIsCurrentAddress && baseSymbol == null && offsetSymbol == null)
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, baseNumeric!.Value + literalOffset, null, null, ConstantName: constName);

            return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, baseNumeric, baseSymbol, null,
                ConstantName: constName, SymbolOffset: literalOffset, ConstantIsCurrentAddress: baseIsCurrentAddress,
                OffsetSymbolName: offsetSymbol, OffsetIsNegative: offsetSymbol != null && offsetIsNegative);
        }

        int sp = code.IndexOfAny(_whitespaceChars);
        string mnemonic = sp < 0 ? code : code[..sp];
        string operand = sp < 0 ? string.Empty : code[(sp + 1)..].Trim();

        if (operand.Length == 0)
            return new ParsedAsmLine(lineNumber, label, mnemonic, OperandForm.None, null, null, null);

        if (operand.Equals("A", StringComparison.OrdinalIgnoreCase))
            return new ParsedAsmLine(lineNumber, label, mnemonic, OperandForm.Accumulator, null, null, null);

        if (operand.StartsWith('#'))
        {
            string immText = operand[1..];
            OperandForm immForm = OperandForm.Immediate;
            if (immText.StartsWith('<')) { immForm = OperandForm.ImmediateLowByte; immText = immText[1..]; }
            else if (immText.StartsWith('>')) { immForm = OperandForm.ImmediateHighByte; immText = immText[1..]; }

            return ResolveInner(lineNumber, label, mnemonic, immForm, immText);
        }

        Match m;
        if ((m = _indirectYPattern.Match(operand)).Success)
            return ResolveInner(lineNumber, label, mnemonic, OperandForm.IndirectY, m.Groups[1].Value);
        if ((m = _indirectXPattern.Match(operand)).Success)
            return ResolveInner(lineNumber, label, mnemonic, OperandForm.IndirectX, m.Groups[1].Value);
        if ((m = _indirectAbsPattern.Match(operand)).Success)
            return ResolveInner(lineNumber, label, mnemonic, OperandForm.IndirectAbsolute, m.Groups[1].Value);
        if ((m = _indexedPattern.Match(operand)).Success)
        {
            var form = m.Groups[2].Value.Equals("X", StringComparison.OrdinalIgnoreCase) ? OperandForm.AddressX : OperandForm.AddressY;
            return ResolveInner(lineNumber, label, mnemonic, form, m.Groups[1].Value);
        }

        return ResolveInner(lineNumber, label, mnemonic, OperandForm.Address, operand);
    }

    #endregion

    #region Private Methods

    private static ParsedAsmLine ResolveInner(int lineNumber, string? label, string mnemonic, OperandForm form, string innerText)
    {
        if (!TrySplitOffset(innerText, out string baseText, out int offset))
            return new ParsedAsmLine(lineNumber, label, mnemonic, OperandForm.None, null, null, $"Malformed operand \"{innerText}\".");

        if (!TryParseValue(baseText, out int value, out string? symbol, out bool isWideHexLiteral))
            return new ParsedAsmLine(lineNumber, label, mnemonic, OperandForm.None, null, null, $"Malformed operand \"{innerText}\".");

        return symbol != null
            ? new ParsedAsmLine(lineNumber, label, mnemonic, form, null, symbol, null, SymbolOffset: offset)
            : new ParsedAsmLine(lineNumber, label, mnemonic, form, value + offset, null, null, OperandIsWideHexLiteral: isWideHexLiteral);
    }

    // Returns whether code begins with the given directive name, splitting off its trimmed
    // argument text. A directive name must be followed by whitespace or end-of-line so e.g.
    // ".org" doesn't spuriously match a hypothetical ".organ" directive.
    private static bool IsDirective(string code, string name, out string argsText)
    {
        argsText = string.Empty;
        if (!code.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return false;
        if (code.Length > name.Length && !char.IsWhiteSpace(code[name.Length])) return false;

        argsText = code.Length > name.Length ? code[name.Length..].Trim() : string.Empty;
        return true;
    }

    // Splits a trailing "+N"/"-N" (e.g. the "+1" in "msgptr+1") off as a constant offset added
    // to whatever the base resolves to. Skips index 0 so a value's own $/% prefix, or - if
    // offsets are ever extended to numeric literals - a leading sign, is never mistaken for this
    // separator.
    private static bool TrySplitOffset(string text, out string baseText, out int offset)
    {
        baseText = text;
        offset = 0;
        if (text.Length == 0) return true; // nothing to split - TryParseValue rejects the empty base text

        int opIdx = text.IndexOfAny(['+', '-'], 1);
        if (opIdx < 0) return true;

        baseText = text[..opIdx].Trim();
        return int.TryParse(text[opIdx..].Trim(), out offset);
    }

    // Splits a constant's value expression "A" or "A + B" / "A - B" into its base and (optional)
    // offset term text, leaving both terms unparsed - unlike TrySplitOffset (used for
    // instruction/".word" operands), a constant's offset term isn't necessarily a plain integer,
    // e.g. the "start" in "size = * - start", so the caller decides how to parse each term. Skips
    // index 0, same reasoning as TrySplitOffset.
    private static bool TrySplitExpressionTerms(string text, out string baseText, out string? offsetText, out bool offsetIsNegative)
    {
        baseText = text;
        offsetText = null;
        offsetIsNegative = false;
        if (text.Length == 0) return false;

        int opIdx = text.IndexOfAny(['+', '-'], 1);
        if (opIdx < 0) return true;

        baseText = text[..opIdx].Trim();
        offsetIsNegative = text[opIdx] == '-';
        offsetText = text[(opIdx + 1)..].Trim();
        return baseText.Length > 0 && offsetText.Length > 0;
    }

    // Parses a $hex / %binary / decimal numeric literal, "*" for the current program counter, or
    // - failing that - accepts a bare identifier as a symbol reference to be resolved against
    // label addresses later. "*" is returned as symbol "*", a reserved name no real identifier
    // can ever collide with (see _symbolPattern) - callers that resolve symbols (Asm6502Assembler)
    // special-case it to mean the address of whatever line is being resolved right now, e.g. an
    // instruction operand like "JMP *" or a ".word *" entry. AsmSymbolIndex excludes it from the
    // Symbols panel since it isn't a real symbol.
    //
    // isWideHexLiteral reports whether a "$" literal was written with more than 2 hex digits (e.g.
    // "$00F0"), the standard convention (also used by Asm6502Disassembler's own output) for
    // explicitly requesting a 2-byte/absolute operand even when the value itself would otherwise
    // fit a single byte - see its use in Asm6502Assembler.TryResolveMode.
    private static bool TryParseValue(string text, out int value, out string? symbol, out bool isWideHexLiteral)
    {
        value = 0;
        symbol = null;
        isWideHexLiteral = false;
        text = text.Trim();
        if (text.Length == 0) return false;

        if (text == "*")
        {
            symbol = "*";
            return true;
        }

        if (text[0] == '$')
        {
            string digits = text[1..];
            isWideHexLiteral = digits.Length > 2;
            return TryParseRadix(digits, 16, out value);
        }
        if (text[0] == '%') return TryParseRadix(text[1..], 2, out value);
        if (char.IsDigit(text[0])) return TryParseRadix(text, 10, out value);

        if (_symbolPattern.IsMatch(text))
        {
            symbol = text;
            return true;
        }

        return false;
    }

    // Parses ".encoding"'s single double-quoted string argument (e.g. the "petscii_mixed" in
    // .encoding "petscii_mixed"), rejecting anything else - no comma list, no numeric literals,
    // unlike ".byte"'s richer grammar.
    private static bool TryParseQuotedString(string argsText, out string value)
    {
        value = "";
        string trimmed = argsText.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"') return false;

        value = trimmed[1..^1];
        return true;
    }

    private static bool TryParseEncodingMode(string modeText, out AsmTextEncoding mode)
    {
        switch (modeText.Trim().ToLowerInvariant())
        {
            case "ascii": mode = AsmTextEncoding.Ascii; return true;
            case "petscii_upper": mode = AsmTextEncoding.PetsciiUpper; return true;
            case "petscii_mixed": mode = AsmTextEncoding.PetsciiMixed; return true;
            case "screencode_upper": mode = AsmTextEncoding.ScreenCodeUpper; return true;
            case "screencode_mixed": mode = AsmTextEncoding.ScreenCodeMixed; return true;
            default: mode = AsmTextEncoding.Ascii; return false;
        }
    }

    // Parses a ".byte" directive's comma-separated argument list into raw bytes. Each item is
    // either a double-quoted string (each character encoded per the active AsmTextEncoding - see
    // AsmTextEncoder) or a $hex/%binary/decimal numeric literal in the 0-255 byte range - symbol
    // references aren't supported here, keeping this directive's scope to literal data only.
    private static bool TryParseByteDirective(string argsText, AsmTextEncoding encoding, out List<byte>? bytes, out string? error)
    {
        var result = new List<byte>();
        error = null;
        int i = 0;
        int n = argsText.Length;

        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(argsText[i])) i++;
            if (i >= n) break;

            if (argsText[i] == '"')
            {
                i++;
                int start = i;
                while (i < n && argsText[i] != '"') i++;
                if (i >= n)
                {
                    bytes = null;
                    error = "Unterminated string literal in .byte.";
                    return false;
                }

                foreach (char c in argsText[start..i])
                    result.Add(AsmTextEncoder.Encode(c, encoding));
                i++; // consume closing quote
            }
            else
            {
                int start = i;
                while (i < n && argsText[i] != ',') i++;
                string token = argsText[start..i].Trim();
                if (!TryParseByteValue(token, out byte value))
                {
                    bytes = null;
                    error = $"Invalid .byte value \"{token}\".";
                    return false;
                }

                result.Add(value);
            }

            while (i < n && char.IsWhiteSpace(argsText[i])) i++;
            if (i >= n) break;

            if (argsText[i] != ',')
            {
                bytes = null;
                error = $"Expected ',' in .byte list near \"{argsText[i..]}\".";
                return false;
            }

            i++; // consume comma
        }

        if (result.Count == 0)
        {
            bytes = null;
            error = ".byte requires at least one value.";
            return false;
        }

        bytes = result;
        return true;
    }

    // Parses a ".word" directive's comma-separated argument list into 16-bit entries, each
    // either a numeric literal (resolved immediately) or a symbol reference with an optional
    // "+N"/"-N" offset (resolved later, once every label's address is known).
    private static bool TryParseWordDirective(string argsText, out List<AsmWordEntry>? entries, out string? error)
    {
        entries = null;
        error = null;

        if (argsText.Length == 0)
        {
            error = ".word requires at least one value.";
            return false;
        }

        var result = new List<AsmWordEntry>();
        foreach (string rawToken in argsText.Split(','))
        {
            string token = rawToken.Trim();
            if (token.Length == 0)
            {
                error = $"Expected a value in .word list near \"{argsText}\".";
                return false;
            }

            if (!TrySplitOffset(token, out string baseText, out int offset))
            {
                error = $"Malformed .word value \"{token}\".";
                return false;
            }

            if (!TryParseValue(baseText, out int value, out string? symbol, out _))
            {
                error = $"Invalid .word value \"{token}\".";
                return false;
            }

            result.Add(symbol != null
                ? new AsmWordEntry(null, symbol, offset)
                : new AsmWordEntry(value + offset, null));
        }

        entries = result;
        return true;
    }

    private static bool TryParseByteValue(string text, out byte value)
    {
        value = 0;
        if (text.Length == 0) return false;

        bool ok = text[0] switch
        {
            '$' => TryParseRadix(text[1..], 16, out int hexValue) && SetIfByteRange(hexValue, out value),
            '%' => TryParseRadix(text[1..], 2, out int binValue) && SetIfByteRange(binValue, out value),
            _ when char.IsDigit(text[0]) => TryParseRadix(text, 10, out int decValue) && SetIfByteRange(decValue, out value),
            _ => false,
        };

        return ok;
    }

    private static bool SetIfByteRange(int intValue, out byte value)
    {
        value = 0;
        if (intValue is < 0 or > 0xFF) return false;
        value = (byte)intValue;
        return true;
    }

    private static bool TryParseRadix(string digits, int radix, out int value)
    {
        try
        {
            value = Convert.ToInt32(digits, radix);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            value = 0;
            return false;
        }
    }

    #endregion
}
