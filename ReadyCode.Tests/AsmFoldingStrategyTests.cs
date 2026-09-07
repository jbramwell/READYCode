// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Editor;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="AsmFoldRegionFinder"/> (the editor-independent core of the WPF
/// <c>AsmFoldingStrategy</c>).
/// </summary>
public class AsmFoldingStrategyTests
{
    #region Public Methods

    [Fact]
    public void Find_TwoConsecutiveCommentLines_ReturnsOneFolding()
    {
        var foldings = AsmFoldRegionFinder.Find("; one\n; two\n LDA #0");

        var f = Assert.Single(foldings);
        Assert.Equal(5, f.StartOffset);
        Assert.Equal(11, f.EndOffset);
    }

    [Fact]
    public void Find_SingleCommentLine_ReturnsNoFolding()
    {
        Assert.Empty(AsmFoldRegionFinder.Find("; only\n LDA #0"));
    }

    [Fact]
    public void Find_IndentedCommentsCount_TrailingCommentsDoNot()
    {
        var foldings = AsmFoldRegionFinder.Find(" LDA #0 ; a\n   ; b\n\t; c\n RTS ; d\n; e");

        var f = Assert.Single(foldings);
        Assert.Equal(18, f.StartOffset);
        Assert.Equal(23, f.EndOffset);
    }

    [Fact]
    public void Find_RunAtEndOfFile_IsClosed()
    {
        var foldings = AsmFoldRegionFinder.Find(" RTS\n; a\n; b");

        var f = Assert.Single(foldings);
        Assert.Equal(8, f.StartOffset);
        Assert.Equal(12, f.EndOffset);
    }

    #endregion
}
