// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Minify;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="CodeMinifier"/>.
/// </summary>
public class CodeMinifierTests
{
    #region Public Methods

    // ── SplitBasicLine ────────────────────────────────────────────────────────

    [Fact]
    public void SplitBasicLine_ParsesLineNumberAndCode()
    {
        var (num, code) = CodeMinifier.SplitBasicLine("10 PRINT \"HI\"");
        Assert.Equal("10", num);
        Assert.Equal("PRINT \"HI\"", code);
    }

    [Fact]
    public void SplitBasicLine_StripsPaddingZeros()
    {
        var (num, _) = CodeMinifier.SplitBasicLine("0010 PRINT A");
        Assert.Equal("10", num);
    }

    [Fact]
    public void SplitBasicLine_ReturnsNullLineNumForNonBasicLine()
    {
        var (num, _) = CodeMinifier.SplitBasicLine("PRINT A");
        Assert.Null(num);
    }

    [Fact]
    public void SplitBasicLine_HandlesLineZero()
    {
        var (num, code) = CodeMinifier.SplitBasicLine("0 END");
        Assert.Equal("0", num);
        Assert.Equal("END", code);
    }

    // ── RemoveWhitespace ──────────────────────────────────────────────────────

    [Fact]
    public void RemoveWhitespace_RemovesAllSpacesOutsideStrings()
    {
        // All spaces outside string literals are removed, including after the line number.
        Assert.Equal("10PRINTA", CodeMinifier.RemoveWhitespace("10  PRINT  A"));
    }

    [Fact]
    public void RemoveWhitespace_PreservesSpacesInsideStrings()
    {
        Assert.Equal("10PRINT\"HELLO   WORLD\"", CodeMinifier.RemoveWhitespace("10 PRINT  \"HELLO   WORLD\""));
    }

    [Fact]
    public void RemoveWhitespace_RemovesSpaceBetweenKeywordAndOperands()
    {
        Assert.Equal("10FORI=1TO10", CodeMinifier.RemoveWhitespace("10   FOR   I=1   TO   10"));
    }

    [Fact]
    public void RemoveWhitespace_MultipleLines()
    {
        Assert.Equal("10PRINTA\n20END", CodeMinifier.RemoveWhitespace("10  PRINT A\n20  END"));
    }

    [Fact]
    public void RemoveWhitespace_RemovesSpaceAfterLineNumber()
    {
        Assert.Equal("10PRINT\"HI\"", CodeMinifier.RemoveWhitespace("10 PRINT \"HI\""));
    }

    // ── Replace0WithPeriod ────────────────────────────────────────────────────

    [Fact]
    public void Replace0WithPeriod_ReplacesLeadingZero()
    {
        Assert.Equal("10 X=.5", CodeMinifier.Replace0WithPeriod("10 X=0.5"));
    }

    [Fact]
    public void Replace0WithPeriod_DoesNotReplaceNonLeadingZero()
    {
        Assert.Equal("10 X=10.5", CodeMinifier.Replace0WithPeriod("10 X=10.5"));
    }

    [Fact]
    public void Replace0WithPeriod_ReplacesMultipleOccurrences()
    {
        Assert.Equal("10 X=.5:Y=.1", CodeMinifier.Replace0WithPeriod("10 X=0.5:Y=0.1"));
    }

    [Fact]
    public void Replace0WithPeriod_PreservesInsideStrings()
    {
        Assert.Equal("10 PRINT \"0.5\"", CodeMinifier.Replace0WithPeriod("10 PRINT \"0.5\""));
    }

    [Fact]
    public void Replace0WithPeriod_DoesNotReplaceZeroAlone()
    {
        // "0" with no following "." is left untouched
        Assert.Equal("10 X=0", CodeMinifier.Replace0WithPeriod("10 X=0"));
    }

    // ── UseScientificNotation ─────────────────────────────────────────────────

    [Fact]
    public void UseScientificNotation_ConvertsRoundNumber()
    {
        Assert.Equal("10 X=1E4", CodeMinifier.UseScientificNotation("10 X=10000"));
    }

    [Fact]
    public void UseScientificNotation_ConvertsMultipleNumbers()
    {
        Assert.Equal("10 X=2E4:Y=1E5", CodeMinifier.UseScientificNotation("10 X=20000:Y=100000"));
    }

    [Fact]
    public void UseScientificNotation_LeavesNonRoundNumberUnchanged()
    {
        // 32768 → 3.2768E4 (8 chars) is longer than 32768 (5 chars)
        Assert.Equal("10 X=32768", CodeMinifier.UseScientificNotation("10 X=32768"));
    }

    [Fact]
    public void UseScientificNotation_IgnoresSmallNumbers()
    {
        Assert.Equal("10 X=100", CodeMinifier.UseScientificNotation("10 X=100"));
    }

    [Fact]
    public void UseScientificNotation_PreservesInsideStrings()
    {
        Assert.Equal("10 PRINT \"10000\"", CodeMinifier.UseScientificNotation("10 PRINT \"10000\""));
    }

    [Fact]
    public void UseScientificNotation_HandlesExactThreshold()
    {
        // 9999 < 10000 → unchanged
        Assert.Equal("10 X=9999", CodeMinifier.UseScientificNotation("10 X=9999"));
        // 10000 ≥ 10000 → convert
        Assert.Equal("10 X=1E4", CodeMinifier.UseScientificNotation("10 X=10000"));
    }

    // ── RemoveComments ────────────────────────────────────────────────────────

    [Fact]
    public void RemoveComments_RemovesPureRemLine()
    {
        string input = "10 PRINT \"HI\"\n20 REM this is a comment\n30 END";
        Assert.Equal("10 PRINT \"HI\"\n30 END", CodeMinifier.RemoveComments(input));
    }

    [Fact]
    public void RemoveComments_RemovesInlineRemAfterColon()
    {
        Assert.Equal("10 PRINT \"HI\"", CodeMinifier.RemoveComments("10 PRINT \"HI\":REM say hello"));
    }

    [Fact]
    public void RemoveComments_RemovesInlineRemWithSpaces()
    {
        Assert.Equal("10 PRINT \"HI\"", CodeMinifier.RemoveComments("10 PRINT \"HI\" : REM say hello"));
    }

    [Fact]
    public void RemoveComments_PreservesColonThatIsNotRem()
    {
        Assert.Equal("10 PRINT A:B=1", CodeMinifier.RemoveComments("10 PRINT A:B=1"));
    }

    [Fact]
    public void RemoveComments_StripsInlineRemButKeepsEarlierStatements()
    {
        string input = "10 PRINT A:B=1:REM note";
        Assert.Equal("10 PRINT A:B=1", CodeMinifier.RemoveComments(input));
    }

    [Fact]
    public void RemoveComments_DoesNotStripRemInsideString()
    {
        // ":REM" inside a string literal must not be stripped
        Assert.Equal("10 PRINT \":REM\"", CodeMinifier.RemoveComments("10 PRINT \":REM\""));
    }

    [Fact]
    public void RemoveComments_RedirectsGotoToNextSurvivingLine()
    {
        // GOTO 20 (a REM line) must be updated to GOTO 30 (the next surviving line).
        string input = "10 GOTO 20\n20 REM label\n30 END";
        Assert.Equal("10 GOTO 30\n30 END", CodeMinifier.RemoveComments(input));
    }

    [Fact]
    public void RemoveComments_RedirectsGosubToNextSurvivingLine()
    {
        string input = "10 GOSUB 20\n20 REM label\n30 PRINT A\n40 RETURN";
        Assert.Equal("10 GOSUB 30\n30 PRINT A\n40 RETURN", CodeMinifier.RemoveComments(input));
    }

    [Fact]
    public void RemoveComments_RedirectsThenToNextSurvivingLine()
    {
        string input = "10 IF X>5 THEN 20\n20 REM label\n30 PRINT A";
        Assert.Equal("10 IF X>5 THEN 30\n30 PRINT A", CodeMinifier.RemoveComments(input));
    }

    [Fact]
    public void RemoveComments_RedirectsAcrossConsecutiveRemLines()
    {
        // Two consecutive REM lines — both should redirect to the same surviving line.
        string input = "10 GOTO 20\n20 REM label1\n30 REM label2\n40 END";
        Assert.Equal("10 GOTO 40\n40 END", CodeMinifier.RemoveComments(input));
    }

    [Fact]
    public void RemoveComments_DoesNotRedirectGotoToExistingLine()
    {
        // GOTO 30 targets a surviving line — must not be touched.
        string input = "10 GOTO 30\n20 REM comment\n30 END";
        Assert.Equal("10 GOTO 30\n30 END", CodeMinifier.RemoveComments(input));
    }

    [Fact]
    public void RemoveComments_RedirectWorksWithSubsequentRenumber()
    {
        // Full workflow: remove REM 600→next line 605, then renumber.
        string input = "600 REM label\n605 PRINT A\n840 IF C=0 GOTO 600";
        string removed    = CodeMinifier.RemoveComments(input);
        string renumbered = CodeMinifier.RenumberLines(removed);
        Assert.Equal("1 PRINT A\n2 IF C=0 GOTO 1", renumbered);
    }

    // ── SimplifyNextStatements ────────────────────────────────────────────────

    [Fact]
    public void SimplifyNextStatements_RemovesSingleVariable()
    {
        Assert.Equal("10 NEXT", CodeMinifier.SimplifyNextStatements("10 NEXT I"));
    }

    [Fact]
    public void SimplifyNextStatements_RemovesMultipleVariables()
    {
        Assert.Equal("10 NEXT", CodeMinifier.SimplifyNextStatements("10 NEXT I,J"));
    }

    [Fact]
    public void SimplifyNextStatements_RemovesStringVariable()
    {
        Assert.Equal("10 NEXT", CodeMinifier.SimplifyNextStatements("10 NEXT A$"));
    }

    [Fact]
    public void SimplifyNextStatements_LeavesBarenNextAlone()
    {
        Assert.Equal("10 NEXT", CodeMinifier.SimplifyNextStatements("10 NEXT"));
    }

    [Fact]
    public void SimplifyNextStatements_HandlesNextInCompoundLine()
    {
        Assert.Equal("10 NEXT: PRINT A", CodeMinifier.SimplifyNextStatements("10 NEXT I: PRINT A"));
    }

    [Fact]
    public void SimplifyNextStatements_DoesNotTouchNextInsideString()
    {
        string input = "10 PRINT \"NEXT I\"";
        Assert.Equal(input, CodeMinifier.SimplifyNextStatements(input));
    }

    // ── RenumberLines ─────────────────────────────────────────────────────────

    [Fact]
    public void RenumberLines_NumbersSequentiallyFrom1()
    {
        string input = "10 PRINT \"HI\"\n20 END";
        Assert.Equal("1 PRINT \"HI\"\n2 END", CodeMinifier.RenumberLines(input));
    }

    [Fact]
    public void RenumberLines_UpdatesGotoTarget()
    {
        string input = "100 GOTO 200\n200 END";
        Assert.Equal("1 GOTO 2\n2 END", CodeMinifier.RenumberLines(input));
    }

    [Fact]
    public void RenumberLines_UpdatesGosubTarget()
    {
        string input = "10 GOSUB 100\n20 END\n100 PRINT A\n110 RETURN";
        Assert.Equal("1 GOSUB 3\n2 END\n3 PRINT A\n4 RETURN", CodeMinifier.RenumberLines(input));
    }

    [Fact]
    public void RenumberLines_UpdatesThenTarget()
    {
        Assert.Equal("1 IF X>5 THEN 2\n2 END\n3 PRINT \"YES\"",
            CodeMinifier.RenumberLines("10 IF X>5 THEN 20\n20 END\n30 PRINT \"YES\""));
    }

    [Fact]
    public void RenumberLines_UpdatesOnGotoTargets()
    {
        string input = "10 ON X GOTO 100,200,300\n100 PRINT 1\n200 PRINT 2\n300 PRINT 3";
        Assert.Equal("1 ON X GOTO 2,3,4\n2 PRINT 1\n3 PRINT 2\n4 PRINT 3", CodeMinifier.RenumberLines(input));
    }

    [Fact]
    public void RenumberLines_RemovesZeroPadding()
    {
        string input = "0010 PRINT \"HI\"\n0020 END";
        Assert.Equal("1 PRINT \"HI\"\n2 END", CodeMinifier.RenumberLines(input));
    }

    [Fact]
    public void RenumberLines_SkipsBlankLines()
    {
        string input = "10 PRINT A\n\n20 END";
        Assert.Equal("1 PRINT A\n2 END", CodeMinifier.RenumberLines(input));
    }

    // ── Minify (orchestrator) ─────────────────────────────────────────────────

    [Fact]
    public void Minify_AppliesAllPasses()
    {
        string input = "0010  REM header\n0020  X=0.5\n0030  NEXT I\n0040  GOTO 0020";
        string result = CodeMinifier.Minify(input,
            removeWhitespace: true,
            replace0WithPeriod: true,
            useScientificNotation: false,
            removeComments: true,
            simplifyNextStatements: true,
            renumberLines: true);
        // REM line removed, 0.5→.5, NEXT I→NEXT, renumbered, all spaces stripped last
        Assert.Equal("1X=.5\n2NEXT\n3GOTO1", result);
    }

    [Fact]
    public void Minify_NoPassesReturnsSameContent()
    {
        string input = "10 PRINT \"HI\"\n20 END";
        string result = CodeMinifier.Minify(input, false, false, false, false, false, false);
        Assert.Equal(input, result);
    }

    #endregion
}
