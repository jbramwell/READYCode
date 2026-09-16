// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Debugger;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="DebugVariableNode"/>.
/// </summary>
public class DebugVariableNodeTests
{
    #region Public Methods

    [Fact]
    public void Constructor_FloatVariable_IsStringValueIsFalse()
    {
        var node = new DebugVariableNode(new BasicVariable("X", BasicVariableType.Float, 3.0, 0x1000));
        Assert.False(node.IsStringValue);
    }

    [Fact]
    public void Constructor_IntegerVariable_IsStringValueIsFalse()
    {
        var node = new DebugVariableNode(new BasicVariable("N%", BasicVariableType.Integer, (short)5, 0x1000));
        Assert.False(node.IsStringValue);
    }

    [Fact]
    public void Constructor_StringVariable_IsStringValueIsTrue()
    {
        var node = new DebugVariableNode(new BasicVariable("A$", BasicVariableType.String, new StringDescriptor(3, 0x2000), 0x1000));
        Assert.True(node.IsStringValue);
    }

    [Fact]
    public void Constructor_ArrayRoot_IsStringValueIsFalse()
    {
        // No Variable of its own (see DebugVariableNode.Variable) - never the PETSCII font case.
        var node = new DebugVariableNode("A", BasicVariableType.String, [3], dataAddress: 0x1000);
        Assert.False(node.IsStringValue);
    }

    [Fact]
    public void UpdateVariable_ChangesTypeFromFloatToString_IsStringValueReflectsTheNewType()
    {
        var node = new DebugVariableNode(new BasicVariable("X", BasicVariableType.Float, 3.0, 0x1000));
        Assert.False(node.IsStringValue);

        node.UpdateVariable(new BasicVariable("X", BasicVariableType.String, new StringDescriptor(1, 0x2000), 0x1000));
        Assert.True(node.IsStringValue);
    }

    [Fact]
    public void Constructor_ArrayRoot_TypeLabelShowsDimBoundsNotElementCounts()
    {
        // DimensionSizes holds each axis's ELEMENT COUNT (DIM N -> N+1, for indices 0 through N -
        // see VariableTableParser.TryParseArrayHeader), but TypeLabel should read like the DIM
        // statement itself: DIM B%(1,2) -> stored [2,3] -> shown "Integer[1,2]", not "Integer[2,3]".
        var node = new DebugVariableNode("B%", BasicVariableType.Integer, [2, 3], dataAddress: 0x1000);
        Assert.Equal("Integer[1,2]", node.TypeLabel);
        Assert.Equal([2, 3], node.DimensionSizes);
        Assert.True(node.IsArray);
        Assert.Null(node.Variable);
    }

    [Fact]
    public void RefreshArrayShape_UpdatesDimensionsAddressAndTypeLabel()
    {
        var node = new DebugVariableNode("A", BasicVariableType.Float, [3], dataAddress: 0x1000);

        node.RefreshArrayShape([5], dataAddress: 0x2000);

        Assert.Equal([5], node.DimensionSizes);
        Assert.Equal((ushort)0x2000, node.DataAddress);
        Assert.Equal("Float[4]", node.TypeLabel); // DIM bound (5 stored elements -> DIM 4)
    }

    // ── ValueDisplayText ─────────────────────────────────────────────────────

    [Fact]
    public void ValueDisplayText_FloatVariable_FormattedToNineSignificantDigits()
    {
        var node = new DebugVariableNode(new BasicVariable("X", BasicVariableType.Float, 3.14159265358979, 0x1000));
        Assert.Equal("3.14159265", node.ValueDisplayText);
    }

    [Fact]
    public void ValueDisplayText_IntegerVariable_PlainSignedDecimal()
    {
        var node = new DebugVariableNode(new BasicVariable("N%", BasicVariableType.Integer, (short)-42, 0x1000));
        Assert.Equal("-42", node.ValueDisplayText);
    }

    [Fact]
    public void ValueDisplayText_ArrayRoot_IsEmpty()
    {
        var node = new DebugVariableNode("A", BasicVariableType.Float, [3], dataAddress: 0x1000);
        Assert.Equal(string.Empty, node.ValueDisplayText);
    }

    [Fact]
    public void ValueDisplayText_UnresolvedString_IsEmpty()
    {
        var node = new DebugVariableNode(new BasicVariable("A$", BasicVariableType.String, new StringDescriptor(3, 0x2000), 0x1000));
        Assert.Equal(string.Empty, node.ValueDisplayText);
    }

    [Fact]
    public void ValueDisplayText_ResolvedString_UpperActiveMode_ShowsAsTyped()
    {
        var node = new DebugVariableNode(new BasicVariable("A$", BasicVariableType.String, new ResolvedStringValue("HELLO", 0x2000), 0x1000));
        Assert.Equal("\"HELLO\"", node.ValueDisplayText);
    }

    [Fact]
    public void ValueDisplayText_ResolvedString_LowerCaseMode_LettersSwapToLowercase()
    {
        var node = new DebugVariableNode(new BasicVariable("A$", BasicVariableType.String, new ResolvedStringValue("HELLO", 0x2000), 0x1000))
        {
            IsUpperCaseModeActive = false,
        };
        Assert.Equal("\"hello\"", node.ValueDisplayText);
    }

    [Fact]
    public void IsUpperCaseModeActive_ChangedAfterConstruction_ValueDisplayTextReactsImmediately()
    {
        var node = new DebugVariableNode(new BasicVariable("A$", BasicVariableType.String, new ResolvedStringValue("HELLO", 0x2000), 0x1000));
        Assert.Equal("\"HELLO\"", node.ValueDisplayText);

        node.IsUpperCaseModeActive = false;
        Assert.Equal("\"hello\"", node.ValueDisplayText);

        node.IsUpperCaseModeActive = true;
        Assert.Equal("\"HELLO\"", node.ValueDisplayText);
    }

    [Fact]
    public void IsUpperCaseModeActive_Changed_RaisesPropertyChangedForValueDisplayText()
    {
        var node = new DebugVariableNode(new BasicVariable("A$", BasicVariableType.String, new ResolvedStringValue("HELLO", 0x2000), 0x1000));
        var raised = new List<string?>();
        node.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        node.IsUpperCaseModeActive = false;

        Assert.Contains(nameof(DebugVariableNode.ValueDisplayText), raised);
    }

    #endregion
}
