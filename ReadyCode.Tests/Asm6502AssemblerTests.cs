// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Assembler;
using ReadyCode.Tokenizer;
using Xunit;
using Encoding = System.Text.Encoding;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="Asm6502Assembler"/> and its supporting types.
/// </summary>
public class Asm6502AssemblerTests
{
    #region Public Methods

    // Pins the exact byte layout the BASIC loader stub trick depends on: appending machine code
    // right after PrgConverter.ConvertToPrg("10 SYS 2062") must land the first code byte at
    // memory address $080E (decimal 2062), matching the SYS target. If PrgConverter's tokenized
    // output ever changes shape, this test - not a silently-wrong load address - must fail first.
    [Fact]
    public void StubLine_ProducesFifteenByteStubLandingCodeAt080E()
    {
        byte[] stub = new PrgConverter().ConvertToPrg("10 SYS 2062");

        Assert.Equal(15, stub.Length);
        Assert.Equal(new byte[]
        {
            0x01, 0x08,             // load address $0801
            0x0C, 0x08,             // next-line link -> $080C (end-of-program marker)
            0x0A, 0x00,             // line number 10
            0x9E,                   // SYS token
            0x20,                   // space
            0x32, 0x30, 0x36, 0x32, // "2062"
            0x00,                   // line terminator
            0x00, 0x00,             // end-of-program marker
        }, stub);
    }

    // ── Addressing-mode families ─────────────────────────────────────────────────

    [Fact]
    public void Assemble_ImmediateAddressing()
    {
        Assert.Equal(new byte[] { 0xA9, 0x41 }, AssembleCode("LDA #$41"));
    }

    [Fact]
    public void Assemble_ZeroPageAddressing()
    {
        Assert.Equal(new byte[] { 0xA5, 0x02 }, AssembleCode("LDA $02"));
    }

    [Fact]
    public void Assemble_AbsoluteAddressing()
    {
        Assert.Equal(new byte[] { 0xAD, 0x00, 0x02 }, AssembleCode("LDA $0200"));
    }

    [Fact]
    public void Assemble_ZeroPageIndexedXAddressing()
    {
        Assert.Equal(new byte[] { 0xB5, 0x10 }, AssembleCode("LDA $10,X"));
    }

    [Fact]
    public void Assemble_AbsoluteIndexedYAddressing()
    {
        Assert.Equal(new byte[] { 0xB9, 0x00, 0x02 }, AssembleCode("LDA $0200,Y"));
    }

    [Fact]
    public void Assemble_IndirectXAddressing()
    {
        Assert.Equal(new byte[] { 0x81, 0x20 }, AssembleCode("STA ($20,X)"));
    }

    [Fact]
    public void Assemble_IndirectYAddressing()
    {
        Assert.Equal(new byte[] { 0xB1, 0xFB }, AssembleCode("LDA ($FB),Y"));
    }

    [Fact]
    public void Assemble_JmpIndirectAbsolute()
    {
        Assert.Equal(new byte[] { 0x6C, 0x34, 0x12 }, AssembleCode("JMP ($1234)"));
    }

    [Fact]
    public void Assemble_AccumulatorAddressing()
    {
        Assert.Equal(new byte[] { 0x0A }, AssembleCode("ASL A"));
    }

    [Fact]
    public void Assemble_ImpliedAddressing()
    {
        Assert.Equal(new byte[] { 0xEA }, AssembleCode("NOP"));
    }

    [Fact]
    public void Assemble_InvalidAddressingModeFails()
    {
        var result = new Asm6502Assembler().Assemble("STX #$00");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.LineNumber == 1 && e.Message.Contains("STX"));
    }

    // ── Label references ─────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_BackwardBranchProducesNegativeOffset()
    {
        // LOOP: DEX (1 byte, at origin) ; BNE LOOP (2 bytes, at origin+1).
        // offset = target - (branchAddr + 2) = origin - (origin + 1 + 2) = -3.
        byte[] code = AssembleCode("LOOP: DEX\nBNE LOOP");

        Assert.Equal(new byte[] { 0xCA, 0xD0, unchecked((byte)-3) }, code);
    }

    [Fact]
    public void Assemble_ForwardJsrResolvesToLaterLabelAddress()
    {
        // JSR SUB (3 bytes, at origin $080E) ; RTS (1 byte, at $0811) ; SUB: RTS (1 byte, at $0812).
        byte[] code = AssembleCode("JSR SUB\nRTS\nSUB: RTS");

        Assert.Equal(new byte[] { 0x20, 0x12, 0x08, 0x60, 0x60 }, code);
    }

    [Fact]
    public void Assemble_LabelOnlyAndCommentOnlyLinesAddNoBytes()
    {
        // A standalone label line and a comment-only line must contribute zero bytes, so START
        // resolves to the very first real instruction's address (the origin).
        byte[] code = AssembleCode("; just a comment\nSTART:\nLDA #$01\n; trailing comment\nSTA START");

        Assert.Equal(new byte[] { 0xA9, 0x01, 0x8D, 0x0E, 0x08 }, code);
    }

    // ── Branch range ──────────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_BranchWithinRangeSucceeds()
    {
        string nops = string.Concat(Enumerable.Repeat("NOP\n", 125));
        byte[] code = AssembleCode($"BNE TARGET\n{nops}TARGET: NOP");

        Assert.Equal(0xD0, code[0]);
        Assert.Equal(125, (sbyte)code[1]);
    }

    [Fact]
    public void Assemble_BranchOutOfRangeFails()
    {
        string nops = string.Concat(Enumerable.Repeat("NOP\n", 200));
        var result = new Asm6502Assembler().Assemble($"BNE TARGET\n{nops}TARGET: NOP");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("out of range"));
    }

    // ── Malformed input ───────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_UnknownMnemonicFails()
    {
        var result = new Asm6502Assembler().Assemble("FOO $00");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.LineNumber == 1 && e.Message.Contains("FOO"));
    }

    [Fact]
    public void Assemble_UndefinedLabelFails()
    {
        var result = new Asm6502Assembler().Assemble("JMP MISSING");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("MISSING"));
    }

    [Fact]
    public void Assemble_DuplicateLabelFails()
    {
        var result = new Asm6502Assembler().Assemble("X: NOP\nX: NOP");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Assemble_MalformedOperandFails()
    {
        var result = new Asm6502Assembler().Assemble("LDA $ZZ");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.LineNumber == 1);
    }

    // ── Empty source ──────────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_EmptySourceProducesStubOnlyPrg()
    {
        var result = new Asm6502Assembler().Assemble("");

        Assert.True(result.Success);
        Assert.Equal(15, result.PrgBytes!.Length);
    }

    [Fact]
    public void Assemble_WhitespaceOnlySourceProducesStubOnlyPrg()
    {
        var result = new Asm6502Assembler().Assemble("   \n\n  \t\n");

        Assert.True(result.Success);
        Assert.Equal(15, result.PrgBytes!.Length);
    }

    // ── Constants ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_ZeroPageConstantAssemblesZeroPage()
    {
        // Unlike a label, a constant's value is known immediately, so it's zero-page-eligible
        // exactly like an equivalent bare literal would be.
        byte[] code = AssembleCode("PTR = $fb\nLDA PTR");

        Assert.Equal(new byte[] { 0xA5, 0xFB }, code);
    }

    [Fact]
    public void Assemble_AbsoluteConstantAssemblesAbsolute()
    {
        // The user's real-world KERNAL CHROUT scenario: a constant naming an address above the
        // zero-page range, used as a JSR target.
        byte[] code = AssembleCode("chrout = $ffd2\njsr chrout");

        Assert.Equal(new byte[] { 0x20, 0xD2, 0xFF }, code);
    }

    [Fact]
    public void Assemble_ConstantCanBeUsedBeforeItsDeclarationLine()
    {
        byte[] code = AssembleCode("LDA CHROUT\nCHROUT = $ffd2");

        Assert.Equal(new byte[] { 0xAD, 0xD2, 0xFF }, code);
    }

    [Fact]
    public void Assemble_DuplicateConstantFails()
    {
        var result = new Asm6502Assembler().Assemble("X = $01\nX = $02\nNOP");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("Duplicate constant"));
    }

    [Fact]
    public void Assemble_ConstantCollidingWithLabelFails()
    {
        var result = new Asm6502Assembler().Assemble("X = $01\nX: NOP");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("already defined as a constant"));
    }

    [Fact]
    public void Assemble_ConstantAndLabelDifferingOnlyByCaseAreDistinctSymbols()
    {
        // Symbol names are case-sensitive (unlike mnemonics) - a common real-world style uses an
        // uppercase constant for a tunable value alongside a same-spelled lowercase label, e.g.
        // "DELAY" (a constant) and "delay:" (a subroutine), which must not collide.
        byte[] code = AssembleCode("DELAY = $30\ndelay:\nldx #DELAY\nrts");

        Assert.Equal(new byte[] { 0xA2, 0x30, 0x60 }, code);
    }

    [Fact]
    public void Assemble_NonNumericConstantValueFails()
    {
        var result = new Asm6502Assembler().Assemble("X = SOMETHING\nNOP");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.LineNumber == 1 && e.Message.Contains("numeric literal"));
    }

    // ── .byte directive ───────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_ByteDirectiveNumericList()
    {
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, AssembleCode(".byte $01, $02, $03"));
    }

    [Fact]
    public void Assemble_ByteDirectiveStringLiteral()
    {
        // Each character becomes its plain character code - no PETSCII remapping.
        Assert.Equal(new byte[] { 0x41, 0x42, 0x00 }, AssembleCode(".byte \"AB\", $00"));
    }

    [Fact]
    public void Assemble_ByteDirectiveStringWithInternalCommaIsNotSplit()
    {
        byte[] expected = [.. Encoding.ASCII.GetBytes("HELLO, WORLD!"), 0x0D, 0x00];

        Assert.Equal(expected, AssembleCode(".byte \"HELLO, WORLD!\", $0d, $00"));
    }

    [Fact]
    public void Assemble_IndexedAddressingOverLabelledByteData()
    {
        // message: (address $0812, 0 bytes) -> .byte "AB",$00 (3 bytes) - LDA message,X must
        // resolve to $0812 even though it labels data, not code.
        byte[] code = AssembleCode("lda message,x\nrts\nmessage:\n.byte \"AB\", $00");

        Assert.Equal(new byte[] { 0xBD, 0x12, 0x08, 0x60, 0x41, 0x42, 0x00 }, code);
    }

    [Fact]
    public void Assemble_UnterminatedByteStringFails()
    {
        var result = new Asm6502Assembler().Assemble(".byte \"AB");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("Unterminated"));
    }

    [Fact]
    public void Assemble_InvalidByteValueFails()
    {
        var result = new Asm6502Assembler().Assemble(".byte $100");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("Invalid .byte value"));
    }

    [Fact]
    public void Assemble_UserKernalChroutIndexedProgramAssembles()
    {
        // The user's real reported program: a KERNAL CHROUT constant, an indexed-addressing
        // print loop over a null-terminated .byte string, verified byte-for-byte end to end.
        string source = """
            ; Hello World using KERNAL chrout

            chrout = $ffd2

                    jsr dispmsg
                    rts

            dispmsg:
                    ldx #0

            loop:
                    lda message,x   ; load next character (indexed addressing)
                    beq done        ; null terminator = we're done
                    jsr chrout      ; print it
                    inx             ; advance index
                    bne loop        ; loop (bne safe here; string won't be 256 chars)

            done:
                    rts

            message:
                    .byte "HELLO, WORLD!", $0d, $00
            """;

        byte[] code = AssembleCode(source);

        byte[] expected =
        [
            0x20, 0x12, 0x08, // jsr dispmsg ($0812)
            0x60,             // rts
            0xA2, 0x00,       // ldx #0
            0xBD, 0x20, 0x08, // lda message,x ($0820)
            0xF0, 0x06,       // beq done (+6)
            0x20, 0xD2, 0xFF, // jsr chrout ($ffd2)
            0xE8,             // inx
            0xD0, 0xF5,       // bne loop (-11)
            0x60,             // rts
            .. Encoding.ASCII.GetBytes("HELLO, WORLD!"),
            0x0D, 0x00,
        ];

        Assert.Equal(expected, code);
    }

    // ── Low/high byte immediates and symbol offsets ──────────────────────────────

    [Fact]
    public void Assemble_ImmediateLowAndHighByteOfLabel()
    {
        // TARGET resolves to $0812 (after the two 2-byte immediate loads before it).
        byte[] code = AssembleCode("lda #<TARGET\nlda #>TARGET\nTARGET:\nrts");

        Assert.Equal(new byte[] { 0xA9, 0x12, 0xA9, 0x08, 0x60 }, code);
    }

    [Fact]
    public void Assemble_ImmediateLowAndHighByteOfConstant()
    {
        byte[] code = AssembleCode("FOO = $1234\nlda #<FOO\nlda #>FOO");

        Assert.Equal(new byte[] { 0xA9, 0x34, 0xA9, 0x12 }, code);
    }

    [Fact]
    public void Assemble_ConstantPlusOffsetAssemblesZeroPage()
    {
        // msgptr+1 = $fc, still zero-page - a known constant's offset value is available
        // immediately, unlike a label's.
        byte[] code = AssembleCode("msgptr = $fb\nsta msgptr+1");

        Assert.Equal(new byte[] { 0x85, 0xFC }, code);
    }

    [Fact]
    public void Assemble_LabelPlusOffsetStillAssemblesAbsolute()
    {
        // LABEL resolves to $0812; LABEL+1 = $0813, but a label reference (deferred, unlike a
        // constant) always assembles absolute regardless of the offset - same rule as a bare
        // label reference.
        byte[] code = AssembleCode("sta LABEL+1\nrts\nLABEL:\nrts");

        Assert.Equal(new byte[] { 0x8D, 0x13, 0x08, 0x60, 0x60 }, code);
    }

    [Fact]
    public void Assemble_UserZeroPagePointerProgramAssembles()
    {
        // The user's real reported program: VIC-20-style color pokes, a zero-page pointer
        // (msgptr/msgptr+1) built from #<label/#>label, and two messages printed through it via
        // indirect-indexed addressing - verified byte-for-byte end to end.
        string source = """
            ; Hello World using KERNAL chrout
            chrout  = $ffd2
            msgptr  = $fb          ; zero page pointer (uses $fb and $fc)

                    ; Set VIC-20 style colors
                    lda #3
                    sta $d020
                    lda #1
                    sta $d021
                    lda #6
                    sta $0286

                    ; Print message 1
                    lda #<message1  ; low byte of address
                    sta msgptr
                    lda #>message1  ; high byte of address
                    sta msgptr+1
                    jsr printmsg

                    ; Print message 2
                    lda #<message2
                    sta msgptr
                    lda #>message2
                    sta msgptr+1
                    jsr printmsg

                    rts

            ; printmsg: prints null-terminated string pointed to by msgptr/msgptr+1
            printmsg:
                    ldy #0

            loop:
                    lda (msgptr),y  ; load byte from [msgptr + Y]
                    beq done        ; null terminator = done
                    jsr chrout
                    iny
                    bne loop        ; safe for strings < 256 chars
            done:
                    rts

            message1:
                    .byte $93, "HELLO, WORLD!", $0d, $0d, $00

            message2:
                    .byte "READYCODE ASSEMBLY SUPPORT COMING SOON!", $0d, $00
            """;

        byte[] code = AssembleCode(source);

        byte[] expected =
        [
            0xA9, 0x03,       // lda #3
            0x8D, 0x20, 0xD0, // sta $d020
            0xA9, 0x01,       // lda #1
            0x8D, 0x21, 0xD0, // sta $d021
            0xA9, 0x06,       // lda #6
            0x8D, 0x86, 0x02, // sta $0286

            0xA9, 0x41,       // lda #<message1 ($0841)
            0x85, 0xFB,       // sta msgptr
            0xA9, 0x08,       // lda #>message1
            0x85, 0xFC,       // sta msgptr+1
            0x20, 0x34, 0x08, // jsr printmsg ($0834)

            0xA9, 0x52,       // lda #<message2 ($0852)
            0x85, 0xFB,       // sta msgptr
            0xA9, 0x08,       // lda #>message2
            0x85, 0xFC,       // sta msgptr+1
            0x20, 0x34, 0x08, // jsr printmsg

            0x60,             // rts

            0xA0, 0x00,       // ldy #0 (printmsg:)

            0xB1, 0xFB,       // lda (msgptr),y (loop:)
            0xF0, 0x06,       // beq done (+6)
            0x20, 0xD2, 0xFF, // jsr chrout
            0xC8,             // iny
            0xD0, 0xF6,       // bne loop (-10)

            0x60,             // rts (done:)

            0x93,             // message1:
            .. Encoding.ASCII.GetBytes("HELLO, WORLD!"),
            0x0D, 0x0D, 0x00,

            .. Encoding.ASCII.GetBytes("READYCODE ASSEMBLY SUPPORT COMING SOON!"),
            0x0D, 0x00,
        ];

        Assert.Equal(expected, code);
    }

    // ── .text directive ───────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_TextDirectiveMatchesByteDirectiveGrammar()
    {
        // ".text" is a pure alias of ".byte" - same grammar, same output.
        Assert.Equal(AssembleCode(".byte \"HI\""), AssembleCode(".text \"HI\""));
    }

    [Fact]
    public void Assemble_TextDirectiveMixedStringAndNumeric()
    {
        Assert.Equal(new byte[] { 0x48, 0x49, 0x00 }, AssembleCode(".text \"HI\", 0"));
    }

    // ── .org directive ────────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_NoOrgDirectiveDefaultsToFixedOriginWithStub()
    {
        // Regression guard: a program with no ".org" must produce byte-identical output to
        // before ".org" support existed - the 15-byte BASIC stub followed by code at $080E.
        var result = new Asm6502Assembler().Assemble("LDA #$01");

        Assert.True(result.Success);
        Assert.Equal(0x080E, result.Origin);
        Assert.Equal(17, result.PrgBytes!.Length);
        Assert.Equal(new byte[] { 0xA9, 0x01 }, result.PrgBytes[15..]);
    }

    [Fact]
    public void Assemble_OrgDirectiveSetsOriginAndOmitsStub()
    {
        var result = new Asm6502Assembler().Assemble(".org $2000\nLDA #$01");

        Assert.True(result.Success);
        Assert.Equal(0x2000, result.Origin);
        Assert.Equal(new byte[] { 0x00, 0x20, 0xA9, 0x01 }, result.PrgBytes);
    }

    [Fact]
    public void Assemble_OrgDirectiveRetargetsLabelAddresses()
    {
        var result = new Asm6502Assembler().Assemble(".org $2000\nSTART:\nLDA #$01\nJMP START");

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 0x00, 0x20, 0xA9, 0x01, 0x4C, 0x00, 0x20 }, result.PrgBytes);
    }

    [Fact]
    public void Assemble_OrgDirectiveAfterCodeFails()
    {
        var result = new Asm6502Assembler().Assemble("NOP\n.org $2000");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("must appear before any code"));
    }

    [Fact]
    public void Assemble_DuplicateOrgDirectiveFails()
    {
        var result = new Asm6502Assembler().Assemble(".org $2000\n.org $3000\nNOP");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("Duplicate '.org'"));
    }

    [Fact]
    public void Assemble_OrgDirectiveWithSymbolValueFails()
    {
        var result = new Asm6502Assembler().Assemble(".org LABEL\nLABEL: NOP");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("numeric literal"));
    }

    // ── .word directive ───────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_WordDirectiveNumericLiteral()
    {
        Assert.Equal(new byte[] { 0x34, 0x12 }, AssembleCode(".word $1234"));
    }

    [Fact]
    public void Assemble_WordDirectiveJumpTableOfForwardLabels()
    {
        // TABLE: (origin) -> .word ENTRY1, ENTRY2 (4 bytes) -> ENTRY1: NOP ($0812) -> ENTRY2: NOP ($0813).
        byte[] code = AssembleCode("TABLE:\n.word ENTRY1, ENTRY2\nENTRY1: NOP\nENTRY2: NOP");

        Assert.Equal(new byte[] { 0x12, 0x08, 0x13, 0x08, 0xEA, 0xEA }, code);
    }

    [Fact]
    public void Assemble_WordDirectiveLabelPlusOffset()
    {
        byte[] code = AssembleCode(".word LABEL+1\nLABEL:\nNOP");

        Assert.Equal(new byte[] { 0x11, 0x08, 0xEA }, code);
    }

    [Fact]
    public void Assemble_WordDirectiveUndefinedLabelFails()
    {
        var result = new Asm6502Assembler().Assemble(".word MISSING");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("MISSING"));
    }

    [Fact]
    public void Assemble_WordDirectiveRequiresAtLeastOneValue()
    {
        var result = new Asm6502Assembler().Assemble(".word");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains(".word requires at least one value"));
    }

    [Fact]
    public void Assemble_WordDirectiveInvalidValueFails()
    {
        var result = new Asm6502Assembler().Assemble(".word $ZZ");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("Invalid .word value"));
    }

    // ── Regression guard ──────────────────────────────────────────────────────────
    // OpcodeTable and AsmTokens are independent tables (encoding vs. reference metadata) that
    // must still describe the exact same 56 mnemonics - this catches either one drifting.

    [Fact]
    public void OpcodeTable_MnemonicSetMatchesAsmTokens()
    {
        var opcodeMnemonics = OpcodeTable.Modes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokenMnemonics = AsmTokens.Mnemonics.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(tokenMnemonics, opcodeMnemonics);
    }

    #endregion

    #region Private Methods

    // Assembles source expected to succeed and returns just the machine-code portion (the
    // 15-byte BASIC loader stub stripped off), so tests can assert on the assembled bytes alone.
    private static byte[] AssembleCode(string source)
    {
        var result = new Asm6502Assembler().Assemble(source);
        Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => $"L{e.LineNumber}: {e.Message}")));
        return result.PrgBytes![15..];
    }

    #endregion
}
