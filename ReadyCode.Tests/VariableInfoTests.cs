// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="VariableInfo"/> and <see cref="VariableOccurrenceInfo"/>.
/// </summary>
public class VariableInfoTests
{
    #region Public Methods

    // ── TypeBadge ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_PlainName_TypeBadgeIsFloat()
    {
        Assert.Equal("FLT", new VariableInfo("X").TypeBadge);
    }

    [Fact]
    public void Constructor_StringSuffix_TypeBadgeIsString()
    {
        Assert.Equal("STR", new VariableInfo("A$").TypeBadge);
    }

    [Fact]
    public void Constructor_IntegerSuffix_TypeBadgeIsInteger()
    {
        Assert.Equal("INT", new VariableInfo("A%").TypeBadge);
    }

    [Fact]
    public void Constructor_IsFunction_TypeBadgeIsFunctionRegardlessOfName()
    {
        var info = new VariableInfo("F1", isFunction: true);

        Assert.Equal("FN", info.TypeBadge);
        Assert.True(info.IsFunction);
    }

    [Fact]
    public void Constructor_NotAFunction_IsFunctionIsFalse()
    {
        Assert.False(new VariableInfo("X").IsFunction);
    }

    // ── DisplayName / LocalToFunction ─────────────────────────────────────────

    [Fact]
    public void Constructor_GlobalVariable_DisplayNameIsJustTheName()
    {
        var info = new VariableInfo("P1");

        Assert.Equal("P1", info.DisplayName);
        Assert.Null(info.LocalToFunction);
    }

    [Fact]
    public void Constructor_LocalToFunction_DisplayNameIncludesTheFunction()
    {
        var info = new VariableInfo("P1", localToFunction: "F3");

        Assert.Equal("P1", info.Name); // raw identifier - used for document text edits
        Assert.Equal("P1 (local to FN F3)", info.DisplayName);
        Assert.Equal("F3", info.LocalToFunction);
    }

    // ── VariableOccurrenceInfo.DisplayText ───────────────────────────────────

    [Fact]
    public void DisplayText_VariableWrite_SaysSet()
    {
        var occurrence = new VariableOccurrenceInfo(1, 10, isWrite: true);
        Assert.Equal("Line 10 — Set", occurrence.DisplayText);
    }

    [Fact]
    public void DisplayText_VariableRead_SaysRead()
    {
        var occurrence = new VariableOccurrenceInfo(1, 10, isWrite: false);
        Assert.Equal("Line 10 — Read", occurrence.DisplayText);
    }

    [Fact]
    public void DisplayText_FunctionDefinition_SaysDefined()
    {
        var occurrence = new VariableOccurrenceInfo(1, 10, isWrite: true, isFunction: true);
        Assert.Equal("Line 10 — Defined", occurrence.DisplayText);
    }

    [Fact]
    public void DisplayText_FunctionCall_SaysCalled()
    {
        var occurrence = new VariableOccurrenceInfo(1, 20, isWrite: false, isFunction: true);
        Assert.Equal("Line 20 — Called", occurrence.DisplayText);
    }

    #endregion
}
