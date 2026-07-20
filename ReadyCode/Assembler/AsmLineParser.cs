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
/// <param name="NumericValue">The operand's resolved numeric value, or null if it was a symbol reference or there is no operand.</param>
/// <param name="SymbolName">The operand's referenced symbol name, or null if it was numeric or there is no operand.</param>
/// <param name="Error">A description of why this line could not be parsed, or null if parsing succeeded.</param>
/// <param name="SymbolOffset">A constant integer added to <paramref name="SymbolName"/>'s resolved value (e.g. the "+1" in "msgptr+1"), 0 if none.</param>
/// <param name="ConstantName">The name being defined, for a "NAME = value" constant declaration line, or null otherwise.</param>
/// <param name="ConstantValue">The declared constant's numeric value, non-null exactly when <paramref name="ConstantName"/> is non-null.</param>
/// <param name="ByteData">The literal bytes to emit, for a ".byte" or ".text" directive line, or null otherwise.</param>
/// <param name="OrgAddress">The requested origin address, for an ".org" directive line, or null otherwise.</param>
/// <param name="WordData">The literal-or-symbolic 16-bit entries to emit, for a ".word" directive line, or null otherwise.</param>
public sealed record ParsedAsmLine(
    int LineNumber,
    string? Label,
    string? Mnemonic,
    OperandForm Form,
    int? NumericValue,
    string? SymbolName,
    string? Error,
    string? ConstantName = null,
    int? ConstantValue = null,
    IReadOnlyList<byte>? ByteData = null,
    int SymbolOffset = 0,
    int? OrgAddress = null,
    IReadOnlyList<AsmWordEntry>? WordData = null);

/// <summary>
/// Parses a single line of 6502 assembly source into its label, mnemonic, and operand shape.
/// Has no knowledge of legal addressing modes per mnemonic or label addresses - resolving the
/// operand shape into a final <see cref="AddressingMode"/> is <see cref="Asm6502Assembler"/>'s job.
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
            if (!TryParseValue(orgArgs, out int orgValue, out string? orgSymbol) || orgSymbol != null)
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                    $"'.org' value \"{orgArgs}\" must be a numeric literal.");
            if (orgValue is < 0 or > 0xFFFF)
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                    $"'.org' value {orgValue} does not fit a 16-bit address (0-65535).");

            return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, null, OrgAddress: orgValue);
        }

        // ".byte"/".text" directives (e.g. .byte "HELLO", $0d, $00) - a comma-separated list of
        // quoted strings (each character becomes one byte, its plain character code - no PETSCII
        // remapping) and/or numeric literals. ".text" is a pure alias of ".byte" - both directive
        // names exist only to let source express intent (data vs. text), the grammar is
        // identical. Checked before the mnemonic/operand split below since these directives have
        // their own comma-delimited grammar rather than a single operand.
        if (IsDirective(code, ".byte", out string byteArgs) || IsDirective(code, ".text", out byteArgs))
        {
            if (!TryParseByteDirective(byteArgs, out List<byte>? byteData, out string? byteError))
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

        // "NAME = value" constant declaration (e.g. "chrout = $ffd2") - checked before the
        // mnemonic/operand split below, since without this check the '=' would otherwise be
        // mis-parsed as a nonsensical operand on a bogus "chrout" mnemonic.
        var constMatch = _constantPattern.Match(code);
        if (constMatch.Success)
        {
            string constName = constMatch.Groups[1].Value;
            string valueText = constMatch.Groups[2].Value.Trim();
            if (!TryParseValue(valueText, out int constValue, out string? constSymbol) || constSymbol != null)
                return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null,
                    $"Constant value \"{valueText}\" must be a numeric literal.");

            return new ParsedAsmLine(lineNumber, label, null, OperandForm.None, null, null, null, constName, constValue);
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

        if (!TryParseValue(baseText, out int value, out string? symbol))
            return new ParsedAsmLine(lineNumber, label, mnemonic, OperandForm.None, null, null, $"Malformed operand \"{innerText}\".");

        return symbol != null
            ? new ParsedAsmLine(lineNumber, label, mnemonic, form, null, symbol, null, SymbolOffset: offset)
            : new ParsedAsmLine(lineNumber, label, mnemonic, form, value + offset, null, null);
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
        int opIdx = text.IndexOfAny(['+', '-'], 1);
        if (opIdx < 0) return true;

        baseText = text[..opIdx].Trim();
        return int.TryParse(text[opIdx..].Trim(), out offset);
    }

    // Parses a $hex / %binary / decimal numeric literal, or - failing that - accepts a bare
    // identifier as a symbol reference to be resolved against label addresses later.
    private static bool TryParseValue(string text, out int value, out string? symbol)
    {
        value = 0;
        symbol = null;
        text = text.Trim();
        if (text.Length == 0) return false;

        if (text[0] == '$') return TryParseRadix(text[1..], 16, out value);
        if (text[0] == '%') return TryParseRadix(text[1..], 2, out value);
        if (char.IsDigit(text[0])) return TryParseRadix(text, 10, out value);

        if (_symbolPattern.IsMatch(text))
        {
            symbol = text;
            return true;
        }

        return false;
    }

    // Parses a ".byte" directive's comma-separated argument list into raw bytes. Each item is
    // either a double-quoted string (each character emitted as its plain character code) or a
    // $hex/%binary/decimal numeric literal in the 0-255 byte range - symbol references aren't
    // supported here, keeping this directive's scope to literal data only.
    private static bool TryParseByteDirective(string argsText, out List<byte>? bytes, out string? error)
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
                    result.Add((byte)c);
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

            if (!TryParseValue(baseText, out int value, out string? symbol))
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
