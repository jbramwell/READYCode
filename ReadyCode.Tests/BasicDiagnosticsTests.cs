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

    // ── GOTO/GOSUB/THEN not followed by a valid target or statement ─────────────

    [Fact]
    public void Analyze_ThenFollowedByStringLiteral_IsFlaggedAtTheThenKeyword()
    {
        string source = "10 IF A$=\"N\" THEN \"HA HA HA\"";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(13, d.Offset);
        Assert.Equal(4, d.Length);
        Assert.Equal("THEN must be followed by a line number or a statement.", d.Message);
    }

    [Fact]
    public void Analyze_ThenAtEndOfStatement_IsFlagged()
    {
        string source = "10 IF X THEN";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("THEN must be followed by a line number or a statement.", d.Message);
    }

    [Fact]
    public void Analyze_ThenFollowedByImplicitLet_IsNotFlagged()
    {
        string source = "10 IF X THEN Y=1";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_ThenFollowedByArrayElementImplicitLet_IsNotFlagged()
    {
        // Implicit LET into an array element - the "=" is well past the variable name/subscript,
        // not immediately after it, so this must not be mistaken for bare unassigned text.
        string source = "10 IF X THEN A(1)=5";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_ThenFollowedByBareUnassignedText_IsFlagged()
    {
        // The actual real-world bug this check was added for: missing quotes around a string
        // left bare text after THEN (e.g. "HA HA HA HAH HA" typed without its closing/opening
        // quotes) - it starts with a letter, so it must not be mistaken for a keyword or the
        // start of a valid implicit LET (which would need an "=" somewhere).
        string source = "10 IF A$=\"N\" THEN HA HA HA HAH HA";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("THEN must be followed by a line number or a statement.", d.Message);
    }

    [Fact]
    public void Analyze_GotoWithNoTarget_IsFlaggedAtTheGotoKeyword()
    {
        string source = "10 GOTO";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(3, d.Offset);
        Assert.Equal(4, d.Length);
        Assert.Equal("GOTO must be followed by a line number.", d.Message);
    }

    [Fact]
    public void Analyze_GosubWithNoTarget_IsFlagged()
    {
        string source = "10 GOSUB X";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("GOSUB must be followed by a line number.", d.Message);
    }

    [Fact]
    public void Analyze_ThenFollowedByNestedGotoWithNoTarget_FlagsTheGoto()
    {
        // THEN itself is fine (followed by the start of a GOTO statement) - the GOTO nested
        // inside it is the one missing its target.
        string source = "10 IF X THEN GOTO";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("GOTO must be followed by a line number.", d.Message);
    }

    // ── FOR/NEXT matching ─────────────────────────────────────────────────────

    [Fact]
    public void Analyze_MatchedForNext_ReturnsNoDiagnostics()
    {
        string source = "10 FOR I=1 TO 5\n20 NEXT I";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_ForEmbeddedAfterThen_IsPushedAndMatchesItsOwnNext()
    {
        // Regression test: a FOR embedded right after THEN (no colon between them, so it's not
        // its own statement) was never being pushed onto the loop stack - its own NEXT then
        // popped and mismatched against whatever unrelated, still-open outer loop happened to be
        // on top instead (e.g. reporting "NEXT Z does not match FOR T" for a perfectly matched
        // inner Z loop, just because an outer T loop was still open around it).
        string source = "10 FOR T=0 TO 19\n20 IF X THEN FOR Z=1 TO 4:PRINT Z:NEXT Z\n30 NEXT T";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_ForAndNextBothOnSameLineAfterThen_MinifiedIsNotFlagged()
    {
        // Same regression, minified (no spaces) - matches the exact real-world source this bug
        // was found in.
        string source = "10 FORT=0TO19\n20 IFXTHENFORZ=1TO4:POKE630+Z,Z:NEXTZ:POKE635,13\n30 NEXTT";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_NextEmbeddedAfterThen_PopsCorrectly()
    {
        // The NEXT side of the same fix: "IF X THEN NEXT" (a bare NEXT as the THEN clause) must
        // also be recognized, not just a FOR.
        string source = "10 FOR I=1 TO 5\n20 IF I=3 THEN NEXT";
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

    // ── Tokenizer gap: out-of-range line numbers ─────────────────────────────
    // PrgConverter.ParseLineNumberAndCode parses the leading line number into a ushort and
    // silently drops the whole line from the saved/deployed .prg if it doesn't fit - these
    // cases mirror that exact bound so the live diagnostics agree with what actually reaches
    // the .prg (see PrgConverter.ConvertToPrg's two silent-drop paths).

    [Fact]
    public void Analyze_LineNumberBeyondUshortRange_IsFlaggedAtTheLineNumber()
    {
        string source = "70000 PRINT 1";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(0, d.Offset);
        Assert.Equal(5, d.Length);
        Assert.Equal("Line number 70000 is out of range (must be 0-65535).", d.Message);
    }

    [Fact]
    public void Analyze_LineNumberAtUshortMax_IsNotFlagged()
    {
        string source = "65535 PRINT 1";
        Assert.Empty(BasicDiagnostics.Analyze(source));
    }

    [Fact]
    public void Analyze_LineNumberOneBeyondUshortMax_IsFlagged()
    {
        string source = "65536 PRINT 1";
        var diagnostics = BasicDiagnostics.Analyze(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("Line number 65536 is out of range (must be 0-65535).", d.Message);
    }

    #endregion
}
