// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Diagnostics;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="BasicDiagnostics"/>.
/// </summary>
public class BasicDiagnosticsTests
{
    #region Public Methods

    // ── Clean input ───────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_CleanProgram_ReturnsNoDiagnostics()
    {
        string source = "10 PRINT \"HI\"\n20 GOTO 10";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    // ── Undefined GOTO/GOSUB/THEN targets ────────────────────────────────────

    [Fact]
    public void Analyze_UndefinedGotoTarget_IsFlaggedAtTheTargetOffset()
    {
        string source = "10 GOTO 20";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(8, d.Offset);
        Assert.Equal(2, d.Length);
        Assert.Equal("Line 20 does not exist.", d.Message);
    }

    [Fact]
    public void Analyze_UndefinedGosubTarget_IsFlagged()
    {
        string source = "10 GOSUB 20";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("Line 20 does not exist.", d.Message);
    }

    [Fact]
    public void Analyze_OnGotoCommaList_OnlyFlagsTheBadTarget()
    {
        string source = "10 ON X GOTO 10,20,30\n20 END";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("Line 30 does not exist.", d.Message);
    }

    [Fact]
    public void Analyze_UndefinedThenTarget_IsFlagged()
    {
        string source = "10 IF X THEN 999";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("Line 999 does not exist.", d.Message);
    }

    [Fact]
    public void Analyze_ThenFollowedByStatement_IsNotTreatedAsATarget()
    {
        string source = "10 IF X THEN PRINT \"HI\"\n20 END";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_TargetMatchingZeroPaddedDeclaration_ResolvesCorrectly()
    {
        string source = "0100 GOTO 100";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    // ── FOR/NEXT matching ─────────────────────────────────────────────────────

    [Fact]
    public void Analyze_MatchedForNext_ReturnsNoDiagnostics()
    {
        string source = "10 FOR I=1 TO 5\n20 NEXT I";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_DanglingNext_IsFlaggedAtTheNextKeyword()
    {
        string source = "10 NEXT I";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(3, d.Offset);
        Assert.Equal(4, d.Length);
        Assert.Equal("NEXT without a matching FOR.", d.Message);
    }

    [Fact]
    public void Analyze_UnclosedFor_IsFlaggedAtTheForKeyword()
    {
        string source = "10 FOR I=1 TO 5";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(3, d.Offset);
        Assert.Equal(3, d.Length);
        Assert.Equal("FOR I has no matching NEXT.", d.Message);
    }

    [Fact]
    public void Analyze_NestedForNext_MatchesInOrderWithNoDiagnostics()
    {
        string source = "10 FOR I=1 TO 5\n20 FOR J=1 TO 5\n30 NEXT J\n40 NEXT I";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_NextVariableNotMatchingFor_IsFlaggedAtTheNextVariable()
    {
        string source = "10 FOR X=1 TO 10\n20 NEXT Y";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(25, d.Offset);
        Assert.Equal(1, d.Length);
        Assert.Equal("NEXT Y does not match FOR X.", d.Message);
    }

    [Fact]
    public void Analyze_NextVariableListWithOneMismatch_FlagsOnlyTheMismatchedVariable()
    {
        string source = "10 FOR I=1 TO 5\n20 FOR J=1 TO 5\n30 NEXT J,K";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("NEXT K does not match FOR I.", d.Message);
    }

    // ── Unterminated strings ──────────────────────────────────────────────────

    [Fact]
    public void Analyze_UnterminatedString_IsFlaggedFromTheOpeningQuote()
    {
        string source = "10 PRINT \"HELLO\n20 PRINT \"WORLD\"";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(9, d.Offset);
        Assert.Equal(6, d.Length);
        Assert.Equal("Unterminated string literal.", d.Message);
    }

    [Fact]
    public void Analyze_StrayQuoteInsideRemComment_IsNotFlagged()
    {
        string source = "10 REM THIS HAS A QUOTE \" INSIDE IT";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    // ── Duplicate line numbers ────────────────────────────────────────────────

    [Fact]
    public void Analyze_DuplicateLineNumber_FlagsBothOccurrences()
    {
        string source = "10 PRINT \"A\"\n10 PRINT \"B\"";
        var diagnostics = BasicDiagnostics.Analyze(source);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("Duplicate line number 10.", d.Message));
        Assert.Equal(0, diagnostics[0].Offset);
        Assert.Equal(13, diagnostics[1].Offset);
    }

    #endregion
}
