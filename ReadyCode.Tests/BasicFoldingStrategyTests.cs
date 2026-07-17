// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ReadyCode.Editor;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="BasicFoldingStrategy"/>.
/// </summary>
public class BasicFoldingStrategyTests
{
    #region Public Methods

    // ── FOR/NEXT ──────────────────────────────────────────────────────────────

    [Fact]
    public void CreateNewFoldings_SimpleForNext_ReturnsOneFoldingWithCorrectOffsets()
    {
        var foldings = Analyze("10 FOR I=1 TO 5\n20 NEXT I");

        var f = Assert.Single(foldings);
        Assert.Equal(15, f.StartOffset);
        Assert.Equal(25, f.EndOffset);
    }

    [Fact]
    public void CreateNewFoldings_NestedForNext_ReturnsTwoCorrectlyPairedFoldings()
    {
        var foldings = Analyze("10 FOR I=1 TO 5\n20 FOR J=1 TO 5\n30 NEXT J\n40 NEXT I");

        Assert.Equal(2, foldings.Count);
        // Sorted by StartOffset - outer loop (FOR I ... NEXT I) starts first.
        Assert.Equal(15, foldings[0].StartOffset);
        Assert.Equal(51, foldings[0].EndOffset);
        Assert.Equal(31, foldings[1].StartOffset);
        Assert.Equal(41, foldings[1].EndOffset);
    }

    [Fact]
    public void CreateNewFoldings_CompoundNextClosesBothLoopsOnOneLine()
    {
        // "FOR Y ... FOR X" on one line, closed by a single compound "NEXT X,Y" - must close
        // BOTH loops (one pop per listed variable), not just one, so the trailing dangling
        // "NEXT Q" doesn't wrongly inherit the stranded FOR Y and produce a bogus wide fold.
        // Both closed loops share the exact same start/end line, though, so only one fold should
        // be emitted for that span - two identical overlapping folds can't be independently
        // toggled (unfolding one leaves the other still folded, hiding the text underneath).
        var foldings = Analyze("10 FOR Y=0 TO 5:FOR X=0 TO 5\n20 NEXT X,Y\n30 NEXT Q");

        var f = Assert.Single(foldings);
        Assert.Equal(28, f.StartOffset);
        Assert.Equal(40, f.EndOffset);
    }

    [Fact]
    public void CreateNewFoldings_DanglingNext_ReturnsNoFolding()
    {
        Assert.Empty(Analyze("10 NEXT I"));
    }

    [Fact]
    public void CreateNewFoldings_UnclosedFor_ReturnsNoFolding()
    {
        Assert.Empty(Analyze("10 FOR I=1 TO 5"));
    }

    // ── REM blocks ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateNewFoldings_ThreeConsecutiveRemLines_ReturnsOneFoldingSpanningAllThree()
    {
        var foldings = Analyze("10 REM AAA\n20 REM BBB\n30 REM CCC");

        var f = Assert.Single(foldings);
        Assert.Equal(10, f.StartOffset);
        Assert.Equal(32, f.EndOffset);
    }

    [Fact]
    public void CreateNewFoldings_SingleRemLine_ReturnsNoFolding()
    {
        Assert.Empty(Analyze("10 REM ONLY"));
    }

    [Fact]
    public void CreateNewFoldings_TrailingInlineRemComments_AreNotTreatedAsFullRemLines()
    {
        Assert.Empty(Analyze("10 X=1:REM note\n20 X=2:REM note\n30 X=3:REM note"));
    }

    // ── String literals ───────────────────────────────────────────────────────

    [Fact]
    public void CreateNewFoldings_KeywordsInsideAStringLiteral_AreIgnored()
    {
        Assert.Empty(Analyze("10 PRINT \"FOR X\""));
    }

    #endregion

    #region Private Methods

    private static List<NewFolding> Analyze(string source) =>
        new BasicFoldingStrategy().CreateNewFoldings(new TextDocument(source)).ToList();

    #endregion
}
