// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using ReadyCode.Diff;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="CompareFileResolver"/>.
/// </summary>
public class CompareFileResolverTests
{
    #region Public Methods

    // ── Resolve: Bas/Asm passthrough ────────────────────────────────────────

    [Fact]
    public void Resolve_BasFile_ReturnsTextAsciiStyledNoWarning()
    {
        var resolved = CompareFileResolver.Resolve("GAME.BAS", Encoding.UTF8.GetBytes("10 PRINT \"HI\""), C64UFileKind.Bas);

        Assert.Equal("10 PRINT \"HI\"", resolved.Text);
        Assert.True(resolved.IsAsciiStyled);
        Assert.Null(resolved.Warning);
    }

    [Fact]
    public void Resolve_AsmFile_ReturnsTextAsciiStyledNoWarning()
    {
        var resolved = CompareFileResolver.Resolve("CODE.ASM", Encoding.UTF8.GetBytes(".org $c000\nlda #0"), C64UFileKind.Asm);

        Assert.Equal(".org $c000\nlda #0", resolved.Text);
        Assert.True(resolved.IsAsciiStyled);
        Assert.Null(resolved.Warning);
    }

    [Fact]
    public void Resolve_StripsLeadingUtf8Bom()
    {
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        byte[] bytes = [.. bom, .. Encoding.UTF8.GetBytes("10 PRINT \"HI\"")];

        var resolved = CompareFileResolver.Resolve("GAME.BAS", bytes, C64UFileKind.Bas);

        Assert.Equal("10 PRINT \"HI\"", resolved.Text);
    }

    // ── Resolve: BASIC .prg detokenization ──────────────────────────────────

    [Fact]
    public void Resolve_BasicPrg_DetokenizesAndIsPetsciiStyled()
    {
        byte[] prg = new PrgConverter().ConvertToPrg("10 PRINT \"HELLO\"");

        var resolved = CompareFileResolver.Resolve("GAME.PRG", prg, C64UFileKind.Prg);

        Assert.Contains("PRINT", resolved.Text);
        Assert.Contains("HELLO", resolved.Text);
        Assert.False(resolved.IsAsciiStyled);
        Assert.Null(resolved.Warning);
    }

    // ── Resolve: machine-code .prg disassembly ──────────────────────────────

    [Fact]
    public void Resolve_MachineCodePrg_DisassemblesAndIsAsciiStyled()
    {
        // 2-byte load address ($C000) followed by "LDA #$01 / RTS" (A9 01 60).
        byte[] prg = [0x00, 0xC0, 0xA9, 0x01, 0x60];

        var resolved = CompareFileResolver.Resolve("CODE.PRG", prg, C64UFileKind.Ml);

        Assert.Contains("lda", resolved.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rts", resolved.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(resolved.IsAsciiStyled);
        Assert.Null(resolved.Warning);
    }

    [Fact]
    public void Resolve_MachineCodePrgWithBasicStub_IncludesStubCommentLines()
    {
        // "10 SYS 2064" loader stub followed by machine code (LDA #$01 / RTS) at $0810.
        byte[] stub = new PrgConverter().ConvertToPrg("10 SYS 2064");
        byte[] prg = [.. stub[..^2], 0xA9, 0x01, 0x60]; // drop the stub's own end-marker, append code

        var resolved = CompareFileResolver.Resolve("CODE.PRG", prg, C64UFileKind.Ml);

        Assert.Contains("SYS", resolved.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2064", resolved.Text);
        Assert.Contains("lda", resolved.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Null(resolved.Warning);
    }

    [Fact]
    public void Resolve_TooShortMlPrg_ReturnsWarningNotThrow()
    {
        var resolved = CompareFileResolver.Resolve("CODE.PRG", [0x00], C64UFileKind.Ml);

        Assert.Equal(string.Empty, resolved.Text);
        Assert.NotNull(resolved.Warning);
    }

    // ── CanCompare ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(C64UFileKind.Bas, C64UFileKind.Bas, true)]
    [InlineData(C64UFileKind.Asm, C64UFileKind.Asm, true)]
    [InlineData(C64UFileKind.Prg, C64UFileKind.Prg, true)]
    [InlineData(C64UFileKind.Ml, C64UFileKind.Ml, true)]
    [InlineData(C64UFileKind.Bas, C64UFileKind.Prg, false)]
    [InlineData(C64UFileKind.Prg, C64UFileKind.Ml, false)]
    [InlineData(C64UFileKind.Other, C64UFileKind.Other, false)]
    [InlineData(C64UFileKind.Folder, C64UFileKind.Folder, false)]
    public void CanCompare_MatchesExpectedPairs(C64UFileKind leftKind, C64UFileKind rightKind, bool expected)
    {
        var left = MakeRef("A", leftKind);
        var right = MakeRef("B", rightKind);

        Assert.Equal(expected, CompareFileResolver.CanCompare(left, right));
    }

    [Fact]
    public void CanCompare_AsmAndSExtensions_AreComparable()
    {
        // Both ".asm" and ".s" classify as C64UFileKind.Asm - same-kind comparison (not literal
        // extension equality) means they should be treated as interchangeable assembly source.
        var left = MakeRef("CODE.ASM", C64UFileKind.Asm);
        var right = MakeRef("CODE.S", C64UFileKind.Asm);

        Assert.True(CompareFileResolver.CanCompare(left, right));
    }

    #endregion

    #region Private Methods

    private static ComparableFileRef MakeRef(string name, C64UFileKind kind) => new()
    {
        Name = name,
        FullPath = $"C:\\test\\{name}",
        Kind = kind,
        Source = ComparableFileSource.Local,
    };

    #endregion
}
