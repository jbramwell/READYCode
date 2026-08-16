// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Debugger;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="BreakpointStore"/>.
/// </summary>
public class BreakpointStoreTests
{
    #region Public Methods

    [Fact]
    public void Toggle_NoExistingBreakpoint_AddsEnabledBreakpoint()
    {
        var store = new BreakpointStore();
        var added = store.Toggle("main.bas", 100);

        Assert.NotNull(added);
        Assert.True(added.IsEnabled);
        Assert.Equal((ushort)100, added.LineNumber);
        Assert.Contains((ushort)100, store.EnabledLinesFor("main.bas"));
    }

    [Fact]
    public void Toggle_ExistingBreakpoint_RemovesIt()
    {
        var store = new BreakpointStore();
        store.Toggle("main.bas", 100);
        var result = store.Toggle("main.bas", 100);

        Assert.Null(result);
        Assert.Null(store.Find("main.bas", 100));
    }

    [Fact]
    public void Find_IsCaseInsensitiveOnFilePath()
    {
        var store = new BreakpointStore();
        store.Toggle("Main.bas", 100);

        Assert.NotNull(store.Find("main.BAS", 100));
    }

    [Fact]
    public void SetEnabled_DisablesWithoutRemoving()
    {
        var store = new BreakpointStore();
        store.Toggle("main.bas", 100);
        store.SetEnabled("main.bas", 100, false);

        Assert.False(store.Find("main.bas", 100)!.IsEnabled);
        Assert.Contains((ushort)100, store.DisabledLinesFor("main.bas"));
        Assert.DoesNotContain((ushort)100, store.EnabledLinesFor("main.bas"));
    }

    [Fact]
    public void Remove_DeletesBreakpoint()
    {
        var store = new BreakpointStore();
        store.Toggle("main.bas", 100);
        store.Remove("main.bas", 100);

        Assert.Null(store.Find("main.bas", 100));
    }

    [Fact]
    public void Remove_NonExistentBreakpoint_DoesNotThrow()
    {
        var store = new BreakpointStore();
        store.Remove("main.bas", 999); // should be a no-op
        Assert.Empty(store.Breakpoints);
    }

    [Fact]
    public void EnabledLinesFor_OnlyReturnsLinesFromTheGivenFile()
    {
        var store = new BreakpointStore();
        store.Toggle("main.bas", 100);
        store.Toggle("other.bas", 200);

        Assert.Equal(new ushort[] { 100 }, store.EnabledLinesFor("main.bas"));
    }

    [Fact]
    public void ReplaceAll_DiscardsPreviousBreakpointsAndAddsNewOnes()
    {
        var store = new BreakpointStore();
        store.Toggle("old.bas", 999);

        store.ReplaceAll(new[]
        {
            new Breakpoint { FilePath = "main.bas", LineNumber = 100, IsEnabled = true },
            new Breakpoint { FilePath = "main.bas", LineNumber = 200, IsEnabled = false },
        });

        Assert.Null(store.Find("old.bas", 999));
        Assert.Equal(2, store.Breakpoints.Count);
        Assert.Contains((ushort)100, store.EnabledLinesFor("main.bas"));
        Assert.Contains((ushort)200, store.DisabledLinesFor("main.bas"));
    }

    [Fact]
    public void Clear_RemovesEveryBreakpoint()
    {
        var store = new BreakpointStore();
        store.Toggle("main.bas", 100);
        store.Toggle("other.bas", 200);
        store.Clear();

        Assert.Empty(store.Breakpoints);
    }

    #endregion
}
