// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Prettify;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="CodePrettifier"/>.
/// </summary>
public class CodePrettifierTests
{
    #region Public Methods

    // ── AddWhitespace ─────────────────────────────────────────────────────────

    [Fact]
    public void AddWhitespace_AddsSpaceAfterLineNumber()
    {
        Assert.Equal("10 PRINT \"HI\"", CodePrettifier.AddWhitespace("10PRINT\"HI\""));
    }

    [Fact]
    public void AddWhitespace_SpacesKeywordsInCompactCode()
    {
        Assert.Equal("10 FOR I=1 TO 10", CodePrettifier.AddWhitespace("10FORI=1TO10"));
    }

    [Fact]
    public void AddWhitespace_PreservesLeadingZerosOnLineNumber()
    {
        // Line-start zeros written by RenumberLines must survive AddWhitespace unchanged.
        Assert.Equal("0010 PRINT \"HI\"", CodePrettifier.AddWhitespace("0010PRINT\"HI\""));
    }

    [Fact]
    public void AddWhitespace_PreservesStringContents()
    {
        // Keywords inside string literals must not receive extra spacing.
        Assert.Equal("10 PRINT \"FOR I=1 TO 10\"", CodePrettifier.AddWhitespace("10PRINT\"FOR I=1 TO 10\""));
    }

    [Fact]
    public void AddWhitespace_FunctionKeywordNoSpaceBeforeParen()
    {
        // Function keywords (PEEK, LEN, …) must not be followed by a space before '('.
        Assert.Equal("10 IF PEEK(49152)=1 THEN 20", CodePrettifier.AddWhitespace("10IFPEEK(49152)=1THEN20"));
    }

    [Fact]
    public void AddWhitespace_HandlesConditional()
    {
        Assert.Equal("10 IF X>5 AND X<10 THEN 30", CodePrettifier.AddWhitespace("10IFX>5ANDX<10THEN30"));
    }

    [Fact]
    public void AddWhitespace_NoSpaceBeforeSemicolon()
    {
        Assert.Equal("10 PRINT;", CodePrettifier.AddWhitespace("10PRINT;"));
    }

    [Fact]
    public void AddWhitespace_RemContentCopiedVerbatim()
    {
        // Everything after REM is copied as-is; embedded keywords like AND get no spaces.
        Assert.Equal("10 REM FOOANDBAR", CodePrettifier.AddWhitespace("10REMFOOANDBAR"));
    }

    [Fact]
    public void AddWhitespace_DataValuesAreCompact()
    {
        // Spaces around commas in DATA are stripped even when "Add Whitespace" is on.
        Assert.Equal("10 DATA 0,12,68,96", CodePrettifier.AddWhitespace("10 DATA 0, 12, 68, 96"));
    }

    [Fact]
    public void AddWhitespace_DataFromMinifiedHasOneSpaceBeforeValues()
    {
        // DATA keyword with no trailing space gets exactly one space inserted.
        Assert.Equal("10 DATA 0,12,68", CodePrettifier.AddWhitespace("10DATA0,12,68"));
    }

    [Fact]
    public void AddWhitespace_DataSpaceBeforeCommaStripped()
    {
        // Spaces BEFORE commas are also removed.
        Assert.Equal("10 DATA 56,120,124", CodePrettifier.AddWhitespace("10 DATA 56 , 120 , 124"));
    }

    [Fact]
    public void AddWhitespace_DataStringLiteralSpacesPreserved()
    {
        // Spaces inside a DATA string literal must be kept.
        Assert.Equal("10 DATA 1,\"HELLO WORLD\",2", CodePrettifier.AddWhitespace("10 DATA 1, \"HELLO WORLD\" ,2"));
    }

    [Fact]
    public void AddWhitespace_HandlesCompoundStatement()
    {
        Assert.Equal("10 PRINT \"A\":GOTO 20", CodePrettifier.AddWhitespace("10PRINT\"A\":GOTO20"));
    }

    [Fact]
    public void AddWhitespace_MultipleLines()
    {
        Assert.Equal("10 PRINT \"HI\"\n20 END", CodePrettifier.AddWhitespace("10PRINT\"HI\"\n20END"));
    }

    [Fact]
    public void AddWhitespace_IsIdempotent()
    {
        string once  = CodePrettifier.AddWhitespace("10FORI=1TO10:NEXTI");
        string twice = CodePrettifier.AddWhitespace(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void AddWhitespace_AlreadySpacedLineUnchanged()
    {
        string input = "10 PRINT \"HELLO\"";
        Assert.Equal(input, CodePrettifier.AddWhitespace(input));
    }

    // ── ReplacePeriodWithZero ─────────────────────────────────────────────────

    [Fact]
    public void ReplacePeriodWithZero_ReplacesLeadingPeriod()
    {
        Assert.Equal("10 X=0.5", CodePrettifier.ReplacePeriodWithZero("10 X=.5"));
    }

    [Fact]
    public void ReplacePeriodWithZero_DoesNotAffectNormalDecimal()
    {
        Assert.Equal("10 X=10.5", CodePrettifier.ReplacePeriodWithZero("10 X=10.5"));
    }

    [Fact]
    public void ReplacePeriodWithZero_ReplacesMultipleOccurrences()
    {
        Assert.Equal("10 X=0.5:Y=0.1", CodePrettifier.ReplacePeriodWithZero("10 X=.5:Y=.1"));
    }

    [Fact]
    public void ReplacePeriodWithZero_PreservesStringContents()
    {
        Assert.Equal("10 PRINT \".5\"", CodePrettifier.ReplacePeriodWithZero("10 PRINT \".5\""));
    }

    [Fact]
    public void ReplacePeriodWithZero_IntegerUnchanged()
    {
        Assert.Equal("10 X=5", CodePrettifier.ReplacePeriodWithZero("10 X=5"));
    }

    // ── UseStandardNotation ───────────────────────────────────────────────────

    [Fact]
    public void UseStandardNotation_ExpandsSimpleENotation()
    {
        Assert.Equal("10 X=10000", CodePrettifier.UseStandardNotation("10 X=1E4"));
    }

    [Fact]
    public void UseStandardNotation_ExpandsDecimalMantissa()
    {
        Assert.Equal("10 X=1500", CodePrettifier.UseStandardNotation("10 X=1.5E3"));
    }

    [Fact]
    public void UseStandardNotation_LeavesNonIntegerResultUnchanged()
    {
        // 1.23E1 = 12.3 — not a whole number, leave as-is.
        Assert.Equal("10 X=1.23E1", CodePrettifier.UseStandardNotation("10 X=1.23E1"));
    }

    [Fact]
    public void UseStandardNotation_PreservesStringContents()
    {
        Assert.Equal("10 PRINT \"1E4\"", CodePrettifier.UseStandardNotation("10 PRINT \"1E4\""));
    }

    [Fact]
    public void UseStandardNotation_ExpandsMultipleNumbers()
    {
        Assert.Equal("10 X=20000:Y=100000", CodePrettifier.UseStandardNotation("10 X=2E4:Y=1E5"));
    }

    // ── AddNextVariables ──────────────────────────────────────────────────────

    [Fact]
    public void AddNextVariables_AddsVariableToBarenNext()
    {
        string input    = "10 FOR I=1 TO 10\n20 NEXT";
        string expected = "10 FOR I=1 TO 10\n20 NEXT I";
        Assert.Equal(expected, CodePrettifier.AddNextVariables(input));
    }

    [Fact]
    public void AddNextVariables_LeavesNextWithVariableUnchanged()
    {
        string input = "10 FOR I=1 TO 10\n20 NEXT I";
        Assert.Equal(input, CodePrettifier.AddNextVariables(input));
    }

    [Fact]
    public void AddNextVariables_HandlesNestedLoops()
    {
        string input    = "10 FOR I=1 TO 10\n20 FOR J=1 TO 10\n30 NEXT\n40 NEXT";
        string expected = "10 FOR I=1 TO 10\n20 FOR J=1 TO 10\n30 NEXT J\n40 NEXT I";
        Assert.Equal(expected, CodePrettifier.AddNextVariables(input));
    }

    [Fact]
    public void AddNextVariables_HandlesNextInCompoundLine()
    {
        string input    = "10 FOR I=1 TO 10:NEXT";
        string expected = "10 FOR I=1 TO 10:NEXT I";
        Assert.Equal(expected, CodePrettifier.AddNextVariables(input));
    }

    [Fact]
    public void AddNextVariables_DetectsForWithoutSpaceAfterKeyword()
    {
        // Minified code has no space between FOR and the variable name.
        string input    = "10 FORI=1 TO 10\n20 NEXT";
        string expected = "10 FORI=1 TO 10\n20 NEXT I";
        Assert.Equal(expected, CodePrettifier.AddNextVariables(input));
    }

    [Fact]
    public void AddNextVariables_HandlesMinifiedCompoundLineWithFor()
    {
        // FOR is the second statement in a compound line, no spaces anywhere.
        string input    = "10 C=1:FORI=2 TO S\n20 NEXT";
        string expected = "10 C=1:FORI=2 TO S\n20 NEXT I";
        Assert.Equal(expected, CodePrettifier.AddNextVariables(input));
    }

    [Fact]
    public void AddNextVariables_LeavesBareNextWithEmptyStack()
    {
        // NEXT with no matching FOR on the stack must be left unchanged.
        string input = "10 NEXT";
        Assert.Equal(input, CodePrettifier.AddNextVariables(input));
    }

    [Fact]
    public void AddNextVariables_DoesNotModifyStringContents()
    {
        string input = "10 PRINT \"NEXT I\"";
        Assert.Equal(input, CodePrettifier.AddNextVariables(input));
    }

    [Fact]
    public void RenumberLines_UpdatesGotoImmediatelyAfterVariableInMinifiedCode()
    {
        // In minified code, GOTO can directly follow a variable with no space:
        // "IFR%<=SGOTO24" — the \b word boundary must NOT be used or GOTO is skipped.
        string input    = "21 IFR%<=SGOTO24\n24 END";
        string expected = "10 IFR%<=SGOTO 20\n20 END";
        Assert.Equal(expected, CodePrettifier.RenumberLines(input, 10, 10, 0));
    }

    // ── RenumberLines ─────────────────────────────────────────────────────────

    [Fact]
    public void RenumberLines_RenumbersWithIncrement()
    {
        string input = "1 PRINT \"HI\"\n2 END";
        Assert.Equal("10 PRINT \"HI\"\n20 END", CodePrettifier.RenumberLines(input, 10, 10, 0));
    }

    [Fact]
    public void RenumberLines_UpdatesGotoTarget()
    {
        string input = "100 GOTO 200\n200 END";
        Assert.Equal("10 GOTO 20\n20 END", CodePrettifier.RenumberLines(input, 10, 10, 0));
    }

    [Fact]
    public void RenumberLines_UpdatesGosubTarget()
    {
        string input    = "10 GOSUB 100\n20 END\n100 PRINT A\n110 RETURN";
        string expected = "10 GOSUB 30\n20 END\n30 PRINT A\n40 RETURN";
        Assert.Equal(expected, CodePrettifier.RenumberLines(input, 10, 10, 0));
    }

    [Fact]
    public void RenumberLines_UpdatesThenTarget()
    {
        string input    = "10 IF X THEN 100\n100 END";
        string expected = "10 IF X THEN 20\n20 END";
        Assert.Equal(expected, CodePrettifier.RenumberLines(input, 10, 10, 0));
    }

    [Fact]
    public void RenumberLines_AppliesPaddingToLineNumbers()
    {
        string input    = "10 PRINT \"HI\"\n20 END";
        string expected = "0010 PRINT \"HI\"\n0020 END";
        Assert.Equal(expected, CodePrettifier.RenumberLines(input, 10, 10, 4));
    }

    [Fact]
    public void RenumberLines_DoesNotPadLineReferences()
    {
        // Line-start numbers get zero-padded; GOTO/GOSUB/THEN targets never do.
        string input    = "10 GOTO 20\n20 END";
        string expected = "0010 GOTO 20\n0020 END";
        Assert.Equal(expected, CodePrettifier.RenumberLines(input, 10, 10, 4));
    }

    [Fact]
    public void RenumberLines_SkipsBlankLines()
    {
        string input    = "10 PRINT A\n\n20 END";
        string expected = "10 PRINT A\n20 END";
        Assert.Equal(expected, CodePrettifier.RenumberLines(input, 10, 10, 0));
    }

    [Fact]
    public void RenumberLines_UpdatesOnGotoTargets()
    {
        string input    = "10 ON X GOTO 100,200,300\n100 PRINT 1\n200 PRINT 2\n300 PRINT 3";
        string expected = "10 ON X GOTO 20,30,40\n20 PRINT 1\n30 PRINT 2\n40 PRINT 3";
        Assert.Equal(expected, CodePrettifier.RenumberLines(input, 10, 10, 0));
    }

    // ── Prettify (orchestrator) ───────────────────────────────────────────────

    [Fact]
    public void Prettify_NoPassesReturnsSameContent()
    {
        string input = "10 FOR I=1 TO 10\n20 END";
        Assert.Equal(input, CodePrettifier.Prettify(input, false, false, false, false, false));
    }

    [Fact]
    public void Prettify_AppliesAllPasses()
    {
        // Input is lightly minified: compact line numbers, shorthand decimal, E-notation, bare NEXT.
        string input    = "1 FOR I=1 TO 5\n2 PRINT .5\n3 X=1E3\n4 NEXT\n5 GOTO 2";
        string expected = "10 FOR I=1 TO 5\n20 PRINT 0.5\n30 X=1000\n40 NEXT I\n50 GOTO 20";
        string result   = CodePrettifier.Prettify(input,
            addWhitespace: true, replacePeriodWithZero: true, useStandardNotation: true,
            addNextVariables: true, renumberLines: true,
            lineNumberIncrement: 10, lineNumberPadding: 0);
        Assert.Equal(expected, result);
    }

    #endregion
}
