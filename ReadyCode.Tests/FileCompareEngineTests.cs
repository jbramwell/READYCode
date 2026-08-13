// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Linq;
using DiffPlex.DiffBuilder.Model;
using ReadyCode.Diff;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="FileCompareEngine"/>.
/// </summary>
public class FileCompareEngineTests
{
    #region Public Methods

    [Fact]
    public void Compute_IdenticalText_HasNoHunks()
    {
        var result = Compute("A\nB\nC", "A\nB\nC");

        Assert.Empty(result.SplitHunkStartRows);
        Assert.Empty(result.UnifiedHunkStartLines);
    }

    [Fact]
    public void Compute_SingleChangedLine_MarksThatRowAsHunk()
    {
        var result = Compute("A\nB\nC", "A\nX\nC");

        Assert.Equal([1], result.SplitHunkStartRows);
        Assert.NotEqual(ChangeType.Unchanged, result.SideBySide.OldText.Lines[1].Type);
        Assert.NotEqual(ChangeType.Unchanged, result.SideBySide.NewText.Lines[1].Type);
        Assert.Equal(ChangeType.Unchanged, result.SideBySide.OldText.Lines[0].Type);
        Assert.Equal(ChangeType.Unchanged, result.SideBySide.OldText.Lines[2].Type);
    }

    [Fact]
    public void Compute_TwoSeparatedChanges_ProducesTwoHunkStarts()
    {
        string left = string.Join('\n', Enumerable.Range(1, 10).Select(n => n.ToString()));
        string right = string.Join('\n', Enumerable.Range(1, 10).Select(n => n is 2 or 8 ? "CHANGED" : n.ToString()));

        var result = Compute(left, right);

        Assert.Equal([1, 7], result.SplitHunkStartRows);
    }

    [Fact]
    public void Compute_LongUnchangedRun_IsCollapsed()
    {
        // 10 unchanged lines between two single-line changes - well past the collapse threshold.
        string left = "CHANGE1\n" + string.Join('\n', Enumerable.Repeat("SAME", 10)) + "\nCHANGE2";
        string right = "changed1\n" + string.Join('\n', Enumerable.Repeat("SAME", 10)) + "\nchanged2";

        var result = Compute(left, right);

        Assert.Single(result.SplitCollapsedRuns);
        Assert.Equal((1, 10), result.SplitCollapsedRuns[0]);
    }

    [Fact]
    public void Compute_ShortUnchangedRun_IsNotCollapsed()
    {
        string left = "CHANGE1\nSAME\nSAME\nCHANGE2";
        string right = "changed1\nSAME\nSAME\nchanged2";

        var result = Compute(left, right);

        Assert.Empty(result.SplitCollapsedRuns);
    }

    [Fact]
    public void Compute_IgnoreWhitespaceTrue_TreatsWhitespaceOnlyDiffAsUnchanged()
    {
        var result = Compute("10 PRINT X", "10  PRINT X", ignoreWhitespace: true);

        Assert.Empty(result.SplitHunkStartRows);
    }

    [Fact]
    public void Compute_IgnoreWhitespaceFalse_TreatsWhitespaceOnlyDiffAsChanged()
    {
        var result = Compute("10 PRINT X", "10  PRINT X", ignoreWhitespace: false);

        Assert.NotEmpty(result.SplitHunkStartRows);
    }

    [Fact]
    public void Compute_ModifiedLine_SubPiecesIsolateOnlyTheChangedWord()
    {
        var result = Compute("10 PRINT X", "10 PRINT Y");

        var oldSubPieces = result.SideBySide.OldText.Lines[0].SubPieces;
        var newSubPieces = result.SideBySide.NewText.Lines[0].SubPieces;

        Assert.Contains(oldSubPieces, p => p.Text.Contains("PRINT") && p.Type == ChangeType.Unchanged);
        Assert.Contains(oldSubPieces, p => p.Text.Contains('X') && p.Type != ChangeType.Unchanged);
        Assert.Contains(newSubPieces, p => p.Text.Contains("PRINT") && p.Type == ChangeType.Unchanged);
        Assert.Contains(newSubPieces, p => p.Text.Contains('Y') && p.Type != ChangeType.Unchanged);
    }

    // ── LeftChangeRuns / RightChangeRuns / UnifiedDeletedRuns / UnifiedInsertedRuns ────────────

    [Fact]
    public void Compute_PureDeletion_AppearsOnlyInLeftChangeRuns()
    {
        var result = Compute("A\nB\nC", "A\nC");

        Assert.Equal([(1, 1)], result.LeftChangeRuns);
        Assert.Empty(result.RightChangeRuns);
    }

    [Fact]
    public void Compute_PureInsertion_AppearsOnlyInRightChangeRuns()
    {
        var result = Compute("A\nC", "A\nB\nC");

        Assert.Empty(result.LeftChangeRuns);
        Assert.Equal([(1, 1)], result.RightChangeRuns);
    }

    [Fact]
    public void Compute_ModifiedLine_AppearsInBothLeftAndRightChangeRuns()
    {
        var result = Compute("A\nB\nC", "A\nX\nC");

        Assert.Equal([(1, 1)], result.LeftChangeRuns);
        Assert.Equal([(1, 1)], result.RightChangeRuns);
    }

    [Fact]
    public void Compute_MultiLinePureDeletion_ProducesOneSizedRunNotOnePerLine()
    {
        var result = Compute("A\nB1\nB2\nB3\nC", "A\nC");

        Assert.Equal([(1, 3)], result.LeftChangeRuns);
        Assert.Equal([(1, 3)], result.UnifiedDeletedRuns);
        Assert.Empty(result.UnifiedInsertedRuns);
    }

    [Fact]
    public void Compute_MultiLinePureInsertion_ProducesOneSizedRunNotOnePerLine()
    {
        var result = Compute("A\nC", "A\nB1\nB2\nB3\nC");

        Assert.Equal([(1, 3)], result.RightChangeRuns);
        Assert.Equal([(1, 3)], result.UnifiedInsertedRuns);
        Assert.Empty(result.UnifiedDeletedRuns);
    }

    [Fact]
    public void Compute_CarriesResolvedMetadataThrough()
    {
        var result = FileCompareEngine.Compute(
            "LEFT.BAS", "10 PRINT X", leftIsAsciiStyled: true, leftWarning: null,
            "RIGHT.BAS", "10 PRINT Y", rightIsAsciiStyled: true, rightWarning: "some warning",
            ignoreWhitespace: false);

        Assert.Equal("LEFT.BAS", result.LeftName);
        Assert.Equal("RIGHT.BAS", result.RightName);
        Assert.True(result.LeftIsAsciiStyled);
        Assert.Null(result.LeftWarning);
        Assert.Equal("some warning", result.RightWarning);
    }

    #endregion

    #region Private Methods

    private static FileCompareResult Compute(string left, string right, bool ignoreWhitespace = false) =>
        FileCompareEngine.Compute(
            "left.bas", left, leftIsAsciiStyled: true, leftWarning: null,
            "right.bas", right, rightIsAsciiStyled: true, rightWarning: null,
            ignoreWhitespace);

    #endregion
}
