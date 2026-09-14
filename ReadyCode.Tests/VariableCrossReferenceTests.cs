// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Diagnostics;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="VariableCrossReference"/>.
/// </summary>
public class VariableCrossReferenceTests
{
    #region Public Methods

    // ── Assignment ────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_BareAssignment_MarksTheTargetAsWrite()
    {
        var refs = Analyze("10 X=5");

        var r = Assert.Single(refs);
        Assert.Equal("X", r.Name);
        Assert.True(r.IsWrite);
    }

    [Fact]
    public void Analyze_LetAssignment_MarksTheTargetAsWrite()
    {
        var refs = Analyze("10 LET X=5");

        var r = Assert.Single(refs);
        Assert.Equal("X", r.Name);
        Assert.True(r.IsWrite);
    }

    [Fact]
    public void Analyze_StringAssignment_MarksTheTargetAsWrite()
    {
        var refs = Analyze("10 A$=\"HI\"");

        var r = Assert.Single(refs);
        Assert.Equal("A$", r.Name);
        Assert.True(r.IsWrite);
    }

    // ── FOR ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_ForLoopVariable_MarksItAsWrite()
    {
        var refs = Analyze("10 FOR I=1 TO 10");

        var r = Assert.Single(refs);
        Assert.Equal("I", r.Name);
        Assert.True(r.IsWrite);
    }

    // ── INPUT / READ / GET ────────────────────────────────────────────────────

    [Fact]
    public void Analyze_Input_MarksEveryTargetAsWrite()
    {
        var refs = Analyze("10 INPUT A,B$");

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.True(r.IsWrite));
        Assert.Contains(refs, r => r.Name == "A");
        Assert.Contains(refs, r => r.Name == "B$");
    }

    [Fact]
    public void Analyze_InputWithPromptString_SkipsThePromptAndMarksOnlyTheTarget()
    {
        var refs = Analyze("10 INPUT \"NAME\";A$");

        var r = Assert.Single(refs);
        Assert.Equal("A$", r.Name);
        Assert.True(r.IsWrite);
    }

    [Fact]
    public void Analyze_InputHash_SkipsTheFileNumberAndMarksTargetsAsWrite()
    {
        var refs = Analyze("10 INPUT#2,A,B");

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.True(r.IsWrite));
    }

    [Fact]
    public void Analyze_Read_MarksEveryTargetAsWrite()
    {
        var refs = Analyze("10 READ A,B$");

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.True(r.IsWrite));
    }

    [Fact]
    public void Analyze_Get_MarksTheTargetAsWrite()
    {
        var refs = Analyze("10 GET A$");

        var r = Assert.Single(refs);
        Assert.Equal("A$", r.Name);
        Assert.True(r.IsWrite);
    }

    [Fact]
    public void Analyze_GetHash_SkipsTheFileNumberAndMarksTargetAsWrite()
    {
        var refs = Analyze("10 GET#1,A");

        var r = Assert.Single(refs);
        Assert.Equal("A", r.Name);
        Assert.True(r.IsWrite);
    }

    // ── DEF FN ────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_DefFn_MarksParameterAsWriteAndBodyUsesAsReads_AndFunctionNameIsNotTracked()
    {
        var refs = Analyze("10 DEF FN SQ(X)=X*X");

        // The function's own name ("SQ") isn't a variable at all.
        Assert.DoesNotContain(refs, r => r.Name == "SQ");

        var xs = refs.Where(r => r.Name == "X").OrderBy(r => r.Offset).ToList();
        Assert.Equal(3, xs.Count);
        Assert.True(xs[0].IsWrite);   // the parameter
        Assert.False(xs[1].IsWrite);  // X*X body - first read
        Assert.False(xs[2].IsWrite);  // X*X body - second read
    }

    // ── DEF FN parameter scoping ─────────────────────────────────────────────
    // On real hardware, a DEF FN parameter aliases the same storage as a same-named global only
    // for the call's duration (saved before, restored after), so it must never be conflated with
    // an unrelated global of the same name used elsewhere in the program.

    [Fact]
    public void Analyze_DefFnParameterAndBodyReferences_AreScopedToTheFunction()
    {
        var refs = Analyze("10 DEF FN F3(P1)=P1*10");

        var p1s = refs.Where(r => r.Name == "P1").ToList();
        Assert.Equal(2, p1s.Count); // the parameter itself, and the one body reference
        Assert.All(p1s, r => Assert.Equal("F3", r.LocalToFunction));
    }

    [Fact]
    public void Analyze_GlobalVariableSharingNameWithDefFnParameter_IsNotScopedToTheFunction()
    {
        var refs = Analyze("5 P1=30\n15 DEF FN F3(P1)=P1*10");

        var global = Assert.Single(refs, r => r.Name == "P1" && r.LocalToFunction == null);
        Assert.True(global.IsWrite);

        var local = refs.Where(r => r.Name == "P1" && r.LocalToFunction == "F3").ToList();
        Assert.Equal(2, local.Count);
    }

    [Fact]
    public void Analyze_VariableOfSameNameUsedAfterDefFnStatement_IsGlobalAgain()
    {
        // A DEF FN parameter is only local for the extent of that one statement - a later,
        // unrelated statement using the same name refers to the (untouched) global.
        var refs = Analyze("10 DEF FN F3(P1)=P1*10\n20 PRINT P1");

        var print = Assert.Single(refs, r => r.Offset > 20); // the PRINT statement's reference
        Assert.Equal("P1", print.Name);
        Assert.Null(print.LocalToFunction);
    }

    [Fact]
    public void Analyze_TwoDefFnsWithSameParameterName_AreScopedToDifferentFunctions()
    {
        var refs = Analyze("10 DEF FN F3(P1)=P1*10\n20 DEF FN F4(P1)=P1+1");

        var f3 = refs.Where(r => r.LocalToFunction == "F3").ToList();
        var f4 = refs.Where(r => r.LocalToFunction == "F4").ToList();
        Assert.Equal(2, f3.Count);
        Assert.Equal(2, f4.Count);
        Assert.All(f3, r => Assert.Equal("P1", r.Name));
        Assert.All(f4, r => Assert.Equal("P1", r.Name));
    }

    [Fact]
    public void Analyze_DefFnBodyReferencingAnotherVariable_IsStillGlobal()
    {
        // Only the declared parameter is scoped - any other variable the body reads is a genuine
        // global reference, exactly as on real hardware.
        var refs = Analyze("10 DEF FN F3(P1)=P1*K");

        var k = Assert.Single(refs, r => r.Name == "K");
        Assert.Null(k.LocalToFunction);
    }

    // ── AnalyzeFunctionParameters ─────────────────────────────────────────────

    [Fact]
    public void AnalyzeFunctionParameters_FloatParameter_IsReportedAsIs()
    {
        var parameters = VariableCrossReference.AnalyzeFunctionParameters("10 DEF FN SQ(X)=X*X");

        var p = Assert.Single(parameters);
        Assert.Equal("SQ", p.FunctionName);
        Assert.Equal("X", p.ParameterName);
    }

    [Fact]
    public void AnalyzeFunctionParameters_SuffixedParameter_IncludesTheSuffixInTheName()
    {
        var parameters = VariableCrossReference.AnalyzeFunctionParameters("10 DEF FN SQ(X%)=X%*X%");

        var p = Assert.Single(parameters);
        Assert.Equal("X%", p.ParameterName);
        Assert.Equal(2, p.Length);
    }

    [Fact]
    public void AnalyzeFunctionParameters_NoDefFn_ReturnsEmpty()
    {
        Assert.Empty(VariableCrossReference.AnalyzeFunctionParameters("10 X=5"));
    }

    // ── IF ... THEN ───────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_IfThenAssignment_MarksConditionVariableAsReadAndThenTargetAsWrite()
    {
        var refs = Analyze("10 IF X=1 THEN Y=5");

        var x = Assert.Single(refs, r => r.Name == "X");
        Assert.False(x.IsWrite);

        var y = Assert.Single(refs, r => r.Name == "Y");
        Assert.True(y.IsWrite);
    }

    [Fact]
    public void Analyze_IfThenGoto_DoesNotProduceAFalseWrite()
    {
        var refs = Analyze("10 IF X=1 THEN 100");

        var x = Assert.Single(refs);
        Assert.Equal("X", x.Name);
        Assert.False(x.IsWrite);
    }

    // ── Arrays ────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_ArrayElementAssignment_MarksArrayNameAsWriteAndIndexAsRead()
    {
        var refs = Analyze("10 A(I)=5");

        var a = Assert.Single(refs, r => r.Name == "A");
        Assert.True(a.IsWrite);

        var i = Assert.Single(refs, r => r.Name == "I");
        Assert.False(i.IsWrite);
    }

    [Fact]
    public void Analyze_TwoDimensionalArrayAssignment_MarksArrayNameAsWrite()
    {
        var refs = Analyze("10 A(I,J)=5");

        var a = Assert.Single(refs, r => r.Name == "A");
        Assert.True(a.IsWrite);
        Assert.Equal(2, refs.Count(r => r.IsWrite == false));
    }

    [Fact]
    public void Analyze_ArrayAssignmentWithNestedParensAndStringInSubscript_StillParsesCorrectly()
    {
        var refs = Analyze("10 A(LEN(\"X\"))=5");

        var a = Assert.Single(refs);
        Assert.Equal("A", a.Name);
        Assert.True(a.IsWrite);
    }

    [Fact]
    public void Analyze_InputArrayTarget_MarksArrayNameAsWrite()
    {
        var refs = Analyze("10 INPUT A(I)");

        var a = Assert.Single(refs, r => r.Name == "A");
        Assert.True(a.IsWrite);
        var i = Assert.Single(refs, r => r.Name == "I");
        Assert.False(i.IsWrite);
    }

    // ── Plain reads ───────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_PrintStatement_IsARead()
    {
        var refs = Analyze("10 PRINT X");

        var r = Assert.Single(refs);
        Assert.Equal("X", r.Name);
        Assert.False(r.IsWrite);
    }

    [Fact]
    public void Analyze_IfConditionWithoutAssignment_IsARead()
    {
        var refs = Analyze("10 IF X>5 THEN PRINT Y");

        var x = Assert.Single(refs, r => r.Name == "X");
        Assert.False(x.IsWrite);
        var y = Assert.Single(refs, r => r.Name == "Y");
        Assert.False(y.IsWrite);
    }

    // ── Keyword-collision consistency ────────────────────────────────────────

    [Fact]
    public void Analyze_VariableNameContainingKeywordSubstring_SplitsSameAsHoverTooltip()
    {
        // "SCORE" contains "OR", which is a real keyword - the greedy, no-word-boundary scan
        // (deliberately shared with every other scanner in this codebase, including the hover
        // tooltip) reads this as "SC", then the OR keyword, then "E" - not one variable "SCORE".
        var refs = Analyze("10 PRINT SCORE");

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Name == "SC");
        Assert.Contains(refs, r => r.Name == "E");
        Assert.All(refs, r => Assert.False(r.IsWrite));
    }

    // ── Exclusions ────────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_StringLiteralContents_AreNotVariables()
    {
        Assert.Empty(Analyze("10 PRINT \"X=5\""));
    }

    [Fact]
    public void Analyze_RemComment_IsNotScanned()
    {
        Assert.Empty(Analyze("10 REM X=5"));
    }

    [Fact]
    public void Analyze_DataStatementValues_AreNotVariables()
    {
        Assert.Empty(Analyze("10 DATA X,Y,Z"));
    }

    // ── AnalyzeFunctions: DEF FN definitions and call sites ─────────────────────

    [Fact]
    public void AnalyzeFunctions_DefFn_IsReportedAsADefinition()
    {
        var refs = AnalyzeFunctions("10 DEF FN SQ(X)=X*X");

        var r = Assert.Single(refs);
        Assert.Equal("SQ", r.Name);
        Assert.True(r.IsDefinition);
    }

    [Fact]
    public void AnalyzeFunctions_CallSite_IsReportedAsNotADefinition()
    {
        var refs = AnalyzeFunctions("10 DEF FN SQ(X)=X*X\n20 PRINT FN SQ(5)");

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Name == "SQ" && r.IsDefinition);
        Assert.Contains(refs, r => r.Name == "SQ" && !r.IsDefinition);
    }

    [Fact]
    public void AnalyzeFunctions_CallWithNoMatchingDefinition_IsStillReportedAsACall()
    {
        var refs = AnalyzeFunctions("10 PRINT FN UNDEF(5)");

        var r = Assert.Single(refs);
        Assert.Equal("UNDEF", r.Name);
        Assert.False(r.IsDefinition);
    }

    [Fact]
    public void AnalyzeFunctions_FunctionCallInsideAnotherDefFnBody_IsNotMistakenForADefinition()
    {
        var refs = AnalyzeFunctions("10 DEF FN F1(P1)=P1*10\n20 DEF FN F2(X)=FN F1(X)+1");

        var f1 = refs.Where(r => r.Name == "F1").ToList();
        Assert.Equal(2, f1.Count);
        Assert.Single(f1, r => r.IsDefinition);
        Assert.Single(f1, r => !r.IsDefinition);

        var f2 = Assert.Single(refs, r => r.Name == "F2");
        Assert.True(f2.IsDefinition);
    }

    [Fact]
    public void AnalyzeFunctions_NoFnUsage_ReturnsEmpty()
    {
        Assert.Empty(AnalyzeFunctions("10 X=5"));
    }

    #endregion

    #region Private Methods

    private static List<VariableReference> Analyze(string source) =>
        VariableCrossReference.Analyze(source).ToList();

    private static List<FunctionReference> AnalyzeFunctions(string source) =>
        VariableCrossReference.AnalyzeFunctions(source).ToList();

    #endregion
}
