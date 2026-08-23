// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Models;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="PrgConverter"/>.
/// </summary>
public class PrgConverterTests
{
    #region Public Methods

    // ── ConvertToPrg ─────────────────────────────────────────────────────────

    [Fact]
    public void ConvertToPrg_StartsWithStandardLoadAddress()
    {
        byte[] prg = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"");
        Assert.Equal(0x01, prg[0]);
        Assert.Equal(0x08, prg[1]);
    }

    [Fact]
    public void ConvertToPrg_EndsWithZeroLinkMarker()
    {
        byte[] prg = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"");
        Assert.Equal(0x00, prg[^2]);
        Assert.Equal(0x00, prg[^1]);
    }

    [Fact]
    public void ConvertToPrg_EmptySource_ReturnsMinimalValidPrg()
    {
        byte[] prg = new PrgConverter().ConvertToPrg("");
        Assert.Equal(new byte[] { 0x01, 0x08, 0x00, 0x00 }, prg);
    }

    [Fact]
    public void ConvertToPrg_LineNumberOnlyNoCode_ProducesNoOutputLine()
    {
        var converter = new PrgConverter();
        byte[] prg = converter.ConvertToPrg("10\n20 PRINT \"HI\"");
        string listing = converter.ConvertFromPrg(prg);

        Assert.DoesNotContain("10 ", listing);
        Assert.Contains("20 PRINT", listing);
    }

    // ── Round trip ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("10 PRINT \"HELLO WORLD\"")]
    [InlineData("10 FOR I=1 TO 10\n20 PRINT I\n30 NEXT I")]
    [InlineData("10 IF X=1 THEN GOTO 30\n20 PRINT \"NO\"\n30 PRINT \"YES\"")]
    [InlineData("10 PRINT SPC(5)\"HELLO WORLD\"")]
    [InlineData("10 PRINT TAB(5)\"HELLO WORLD\"")]
    public void ConvertToPrg_ThenConvertFromPrg_RetokenizesToIdenticalBytes(string source)
    {
        // The strongest available round-trip check: ConvertFromPrg's spacing doesn't necessarily
        // match the original source text verbatim, but re-tokenizing its output must reproduce
        // byte-identical PRG data, or something was lost in the detokenize/retokenize path.
        var converter = new PrgConverter();
        byte[] prg = converter.ConvertToPrg(source);
        string listing = converter.ConvertFromPrg(prg);
        byte[] prgAgain = converter.ConvertToPrg(listing);

        Assert.Equal(prg, prgAgain);
    }

    // ── ConvertFromPrg ───────────────────────────────────────────────────────

    [Fact]
    public void ConvertFromPrg_TooShortForEvenOneLine_ReturnsEmptyString()
    {
        // Too short to hold a real line isn't an error - e.g. a brand-new blank .prg from "New
        // File..." (which FileClassifier now classifies as Prg, not Ml) must still open cleanly.
        Assert.Equal(string.Empty, new PrgConverter().ConvertFromPrg([0x01, 0x08]));
    }

    [Fact]
    public void ConvertFromPrg_EmptyData_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, new PrgConverter().ConvertFromPrg([]));
    }

    [Fact]
    public void ConvertFromPrg_IncludesLineNumberPrefix()
    {
        byte[] prg = new PrgConverter().ConvertToPrg("100 PRINT \"HI\"");
        string listing = new PrgConverter().ConvertFromPrg(prg);
        Assert.StartsWith("100 ", listing);
    }

    [Fact]
    public void ConvertFromPrg_KeepsStringLiteralContentUnexpanded()
    {
        // A byte that would normally expand to a keyword must stay literal inside a string -
        // e.g. the letter sequence spelling a keyword inside quotes isn't a token, so this just
        // verifies the round trip preserves quoted text verbatim.
        var converter = new PrgConverter();
        byte[] prg = converter.ConvertToPrg("10 PRINT \"PRINT THIS\"");
        string listing = converter.ConvertFromPrg(prg);
        Assert.Contains("\"PRINT THIS\"", listing);
    }

    [Fact]
    public void ConvertFromPrg_SpcTokenRendersWithSingleParen()
    {
        // Regression test: SPC(=0xA6 bakes the opening paren into the token's own display text
        // on real hardware, so the tokenized stream must contain exactly one "(" byte, and
        // detokenizing it must not duplicate that paren (e.g. "SPC((5)").
        var converter = new PrgConverter();
        byte[] prg = converter.ConvertToPrg("10 PRINT SPC(5)\"HELLO WORLD\"");
        string listing = converter.ConvertFromPrg(prg);

        Assert.Contains("SPC(5)", listing);
        Assert.DoesNotContain("SPC((5)", listing);
    }

    [Fact]
    public void ConvertFromPrg_TabTokenRendersWithSingleParen()
    {
        var converter = new PrgConverter();
        byte[] prg = converter.ConvertToPrg("10 PRINT TAB(5)\"HELLO WORLD\"");
        string listing = converter.ConvertFromPrg(prg);

        Assert.Contains("TAB(5)", listing);
        Assert.DoesNotContain("TAB((5)", listing);
    }

    // ── IsBasicProgram ───────────────────────────────────────────────────────

    [Fact]
    public void IsBasicProgram_GenuineTokenizedProgram_ReturnsTrue()
    {
        byte[] prg = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"\n20 GOTO 10");
        Assert.True(new PrgConverter().IsBasicProgram(prg));
    }

    [Fact]
    public void IsBasicProgram_EmptyProgram_ReturnsTrue()
    {
        // ConvertToPrg's own "no lines" fallback output must round-trip as valid.
        byte[] prg = new PrgConverter().ConvertToPrg("");
        Assert.True(new PrgConverter().IsBasicProgram(prg));
    }

    [Fact]
    public void IsBasicProgram_WrongLoadAddress_ReturnsFalse()
    {
        byte[] data = [0x00, 0x10, 0x00, 0x00];
        Assert.False(new PrgConverter().IsBasicProgram(data));
    }

    [Fact]
    public void IsBasicProgram_MachineLanguageStub_ReturnsFalse()
    {
        // Real load address, but the bytes after it are raw 6502 code, not a valid line chain.
        byte[] data = [0x01, 0x08, 0xA9, 0x00, 0x8D, 0x20, 0xD0, 0x60];
        Assert.False(new PrgConverter().IsBasicProgram(data));
    }

    [Fact]
    public void IsBasicProgram_TruncatedProgram_ReturnsFalse()
    {
        byte[] prg = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"");
        byte[] truncated = prg[..^3];
        Assert.False(new PrgConverter().IsBasicProgram(truncated));
    }

    [Fact]
    public void IsBasicProgram_TooShort_ReturnsFalse()
    {
        Assert.False(new PrgConverter().IsBasicProgram([0x01]));
    }

    [Fact]
    public void IsBasicProgram_TrailingPaddingAfterEndMarker_ReturnsTrue()
    {
        // Regression test: a real-world .prg (e.g. one extracted whole from a D64 disk sector)
        // can carry a few stray bytes past its own 0x0000 end-of-program marker. Real hardware
        // stops following the line-link chain right at that marker and never looks past it, so
        // this must still be recognized as BASIC rather than falling back to the hex/ML viewer.
        byte[] prg = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"\n20 GOTO 10");
        byte[] withPadding = [..prg, 0xA0, 0x00, 0xAE];
        Assert.True(new PrgConverter().IsBasicProgram(withPadding));
    }

    // ── TryDetectBasicStub ───────────────────────────────────────────────────

    [Fact]
    public void TryDetectBasicStub_StubFollowedByMachineCode_ReturnsTrue()
    {
        var converter = new PrgConverter();
        byte[] stub = converter.ConvertToPrg("10 SYS 2064");
        byte[] mlBytes = [0xA9, 0x00, 0x8D, 0x20, 0xD0, 0x60];
        byte[] combined = [.. stub, .. mlBytes];

        bool found = converter.TryDetectBasicStub(combined, out IReadOnlyList<string> stubLines, out int codeOffset);

        Assert.True(found);
        Assert.Equal(stub.Length, codeOffset);
        Assert.Equal(mlBytes, combined[codeOffset..]);
        Assert.Single(stubLines);
        Assert.Equal("10 SYS 2064", stubLines[0]);
    }

    [Fact]
    public void TryDetectBasicStub_NonstandardButForwardMovingLink_StillDetected()
    {
        // Real-world stub generators don't always compute the link pointer with the exact
        // arithmetic ConvertToPrg's own output uses - BASIC itself doesn't validate it either, it
        // just follows whatever's stored. As long as the link still moves strictly forward,
        // detection must still succeed (this is what previously broke on a real cracked-scene
        // "1994 SYS 2059" stub whose link didn't match a from-scratch recomputation).
        var converter = new PrgConverter();
        byte[] stub = converter.ConvertToPrg("1994 SYS 2059");
        ushort originalLink = (ushort)(stub[2] | (stub[3] << 8));
        ushort skewedLink = (ushort)(originalLink + 3);
        stub[2] = (byte)(skewedLink & 0xFF);
        stub[3] = (byte)(skewedLink >> 8);

        byte[] mlBytes = [0xA9, 0x00, 0x60];
        byte[] combined = [.. stub, .. mlBytes];

        bool found = converter.TryDetectBasicStub(combined, out IReadOnlyList<string> stubLines, out int codeOffset);

        Assert.True(found);
        Assert.Equal(stub.Length, codeOffset);
        Assert.Equal("1994 SYS 2059", stubLines[0]);
    }

    [Fact]
    public void TryDetectBasicStub_NoEndOfProgramMarker_MachineCodeStartsRightAfterLine_StillDetected()
    {
        // BASIC's LIST/RUN never needs to continue past a SYS-and-jump line (SYS transfers
        // control away for good), so a real stub commonly has no trailing 0x0000 "end of
        // program" marker at all - the machine code starts immediately after the line's own
        // terminator byte. This was previously misdetected as "not a stub" because the check
        // always required a second link/marker pair after the first line.
        var converter = new PrgConverter();
        byte[] stubWithMarker = converter.ConvertToPrg("1994 SYS 2059");
        byte[] stubNoMarker = stubWithMarker[..^2]; // drop ConvertToPrg's own trailing 0x0000 marker
        byte[] mlBytes = [0xA9, 0x00, 0x8D, 0x20, 0xD0, 0x60];
        byte[] combined = [.. stubNoMarker, .. mlBytes];

        bool found = converter.TryDetectBasicStub(combined, out IReadOnlyList<string> stubLines, out int codeOffset);

        Assert.True(found);
        Assert.Equal(stubNoMarker.Length, codeOffset);
        Assert.Equal(mlBytes, combined[codeOffset..]);
        Assert.Single(stubLines);
        Assert.Equal("1994 SYS 2059", stubLines[0]);
    }

    [Fact]
    public void TryDetectBasicStub_RealWorldExample_NoSpaceAfterKeyword_StillDetected()
    {
        // Exact byte layout of the real cracked-scene file that exposed this bug: "1994
        // SYS2059" (no space typed between the keyword and its argument) with no 0x0000 end
        // marker before the machine code.
        byte[] data =
        [
            0x01, 0x08,                         // load address $0801
            0x0B, 0x08,                         // link -> $080B (2059) - the code's real start
            0xCA, 0x07,                         // line number 1994
            0x9E, 0x32, 0x30, 0x35, 0x39,       // tokens: SYS token + "2059"
            0x00,                               // line terminator
            0xA9, 0x00, 0x8D, 0x20, 0xD0, 0x60, // raw ML code - starts immediately, no marker
        ];

        bool found = new PrgConverter().TryDetectBasicStub(data, out IReadOnlyList<string> stubLines, out int codeOffset);

        Assert.True(found);
        Assert.Equal(12, codeOffset);
        Assert.Equal(new byte[] { 0xA9, 0x00, 0x8D, 0x20, 0xD0, 0x60 }, data[codeOffset..]);
        Assert.Equal("1994 SYS2059", stubLines[0]);
    }

    [Fact]
    public void TryDetectBasicStub_CompleteBasicProgramWithNoTrailingBytes_ReturnsFalse()
    {
        // A genuine, complete BASIC program - nothing follows the terminator, so there's no
        // machine code to find the origin of.
        byte[] prg = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"");
        Assert.False(new PrgConverter().TryDetectBasicStub(prg, out _, out _));
    }

    [Fact]
    public void TryDetectBasicStub_MachineLanguageOnly_ReturnsFalse()
    {
        // Real load address, but the bytes after it are raw 6502 code, not a valid line chain.
        byte[] data = [0x01, 0x08, 0xA9, 0x00, 0x8D, 0x20, 0xD0, 0x60];
        Assert.False(new PrgConverter().TryDetectBasicStub(data, out _, out _));
    }

    [Fact]
    public void TryDetectBasicStub_WrongLoadAddress_ReturnsFalse()
    {
        byte[] data = [0x00, 0x10, 0x00, 0x00, 0xA9, 0x00];
        Assert.False(new PrgConverter().TryDetectBasicStub(data, out _, out _));
    }

    [Fact]
    public void TryDetectBasicStub_TooShort_ReturnsFalse()
    {
        Assert.False(new PrgConverter().TryDetectBasicStub([0x01], out _, out _));
    }

    // ── NeedsSysToRun ────────────────────────────────────────────────────────

    [Fact]
    public void NeedsSysToRun_CompleteBasicProgram_ReturnsFalse()
    {
        // Autostart's RUN works natively - no SYS needed.
        byte[] prg = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"");
        Assert.False(new PrgConverter().NeedsSysToRun(prg, out _));
    }

    [Fact]
    public void NeedsSysToRun_BasicStubPlusMachineCode_ReturnsFalse()
    {
        // Autostart follows the stub's own SYS line - no typed command needed either.
        var converter = new PrgConverter();
        byte[] stub = converter.ConvertToPrg("10 SYS 2064");
        byte[] mlBytes = [0xA9, 0x00, 0x60];
        byte[] combined = [.. stub, .. mlBytes];

        Assert.False(converter.NeedsSysToRun(combined, out _));
    }

    [Fact]
    public void NeedsSysToRun_RawMachineCodeNoBasicEntryPoint_ReturnsTrueWithHeaderOrigin()
    {
        // No BASIC anywhere - autostart's RUN would have nothing to execute, so a typed
        // "SYS <origin>" is required, using the file's own load-address header as the target.
        byte[] data = [0x00, 0xC0, 0xA9, 0x00, 0x60]; // origin $C000
        bool needsSys = new PrgConverter().NeedsSysToRun(data, out ushort origin);

        Assert.True(needsSys);
        Assert.Equal(0xC000, origin);
    }

    [Fact]
    public void NeedsSysToRun_TooShort_ReturnsFalse()
    {
        Assert.False(new PrgConverter().NeedsSysToRun([0x01], out _));
    }

    // ── ShouldTokenizeOnSave ─────────────────────────────────────────────────

    [Theory]
    [InlineData(EditorLanguage.Basic, "program.prg", true)]
    [InlineData(EditorLanguage.Basic, "program.bas", false)]
    [InlineData(EditorLanguage.Basic, "PROGRAM.BAS", false)]
    [InlineData(EditorLanguage.Basic, "noextension", true)]
    [InlineData(EditorLanguage.Asm, "program.asm", false)]
    [InlineData(EditorLanguage.Asm, "program.bas", false)]
    public void ShouldTokenizeOnSave_ReturnsExpected(EditorLanguage language, string filePath, bool expected)
    {
        Assert.Equal(expected, PrgConverter.ShouldTokenizeOnSave(language, filePath));
    }

    #endregion
}
