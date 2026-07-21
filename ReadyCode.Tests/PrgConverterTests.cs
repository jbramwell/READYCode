// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
    public void ConvertFromPrg_TooShort_Throws()
    {
        Assert.Throws<FormatException>(() => new PrgConverter().ConvertFromPrg([0x01, 0x08]));
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

    #endregion
}
