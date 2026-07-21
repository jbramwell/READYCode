// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Models;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="FileClassifier"/>.
/// </summary>
public class FileClassifierTests
{
    #region Public Methods

    // ── Extension classification ──────────────────────────────────────────────

    [Fact]
    public void Classify_Folder_ReturnsFolder()
    {
        Assert.Equal(C64UFileKind.Folder, FileClassifier.Classify("anything", isFolder: true));
    }

    [Theory]
    [InlineData("program.bas", C64UFileKind.Bas)]
    [InlineData("program.asm", C64UFileKind.Asm)]
    [InlineData("program.s", C64UFileKind.Asm)]
    [InlineData("disk.d64", C64UFileKind.D64)]
    [InlineData("disk.d81", C64UFileKind.D81)]
    [InlineData("readme.txt", C64UFileKind.Other)]
    [InlineData("noextension", C64UFileKind.Other)]
    public void Classify_ByExtension_ReturnsExpectedKind(string name, C64UFileKind expected)
    {
        Assert.Equal(expected, FileClassifier.Classify(name, isFolder: false));
    }

    [Fact]
    public void Classify_ExtensionIsCaseInsensitive()
    {
        Assert.Equal(C64UFileKind.D64, FileClassifier.Classify("DISK.D64", isFolder: false));
    }

    // ── .prg sniffing ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_PrgWithNoCallback_DefaultsToPrg()
    {
        Assert.Equal(C64UFileKind.Prg, FileClassifier.Classify("game.prg", isFolder: false));
    }

    [Fact]
    public void Classify_PrgWithBasicBytes_ReturnsPrg()
    {
        byte[] basic = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"");
        var kind = FileClassifier.Classify("game.prg", isFolder: false, () => basic);
        Assert.Equal(C64UFileKind.Prg, kind);
    }

    [Fact]
    public void Classify_PrgWithMachineCodeBytes_ReturnsMl()
    {
        byte[] machineCode = [0x01, 0x08, 0xA9, 0x00, 0x60];
        var kind = FileClassifier.Classify("game.prg", isFolder: false, () => machineCode);
        Assert.Equal(C64UFileKind.Ml, kind);
    }

    [Fact]
    public void Classify_PrgWithThrowingCallback_DefaultsToPrg()
    {
        // e.g. a remote entry whose content hasn't been downloaded yet, or an unreadable local file.
        var kind = FileClassifier.Classify("game.prg", isFolder: false, () => throw new IOException("locked"));
        Assert.Equal(C64UFileKind.Prg, kind);
    }

    // ── IsDiskImageKind ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(C64UFileKind.D64, true)]
    [InlineData(C64UFileKind.D81, true)]
    [InlineData(C64UFileKind.Prg, false)]
    [InlineData(C64UFileKind.Bas, false)]
    [InlineData(C64UFileKind.Ml, false)]
    [InlineData(C64UFileKind.Asm, false)]
    [InlineData(C64UFileKind.Folder, false)]
    [InlineData(C64UFileKind.Other, false)]
    public void IsDiskImageKind_ReturnsExpected(C64UFileKind kind, bool expected)
    {
        Assert.Equal(expected, kind.IsDiskImageKind());
    }

    #endregion
}
