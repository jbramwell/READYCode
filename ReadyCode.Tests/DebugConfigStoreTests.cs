// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Settings;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="DebugConfigStore"/>'s in-memory logic (pruning, breakpoint get/update,
/// project key scheme) - deliberately exercised without touching disk, mirroring how
/// <see cref="AppSettings"/>'s own real file I/O (also hardcoded to the user's real AppData
/// path) is left untested at that layer.
/// </summary>
public class DebugConfigStoreTests
{
    #region Public Methods

    // ── PruneStaleProjects ───────────────────────────────────────────────────

    [Fact]
    public void PruneStaleProjects_RemovesEntriesOlderThan30Days()
    {
        var utcNow = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
        var projects = new Dictionary<string, DebugProjectConfig>
        {
            ["folder:stale"] = new() { LastAccessedUtc = utcNow.AddDays(-31) },
            ["folder:fresh"] = new() { LastAccessedUtc = utcNow.AddDays(-1) },
        };

        bool removedAny = DebugConfigStore.PruneStaleProjects(projects, utcNow);

        Assert.True(removedAny);
        Assert.False(projects.ContainsKey("folder:stale"));
        Assert.True(projects.ContainsKey("folder:fresh"));
    }

    [Fact]
    public void PruneStaleProjects_ExactlyAtCutoff_IsKept()
    {
        // 30 days ago is the boundary - "30 or more days" prunes strictly older than this,
        // so a project accessed exactly 30 days ago (not yet 31) survives one more day.
        var utcNow = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
        var projects = new Dictionary<string, DebugProjectConfig>
        {
            ["folder:exact"] = new() { LastAccessedUtc = utcNow.AddDays(-30) },
        };

        DebugConfigStore.PruneStaleProjects(projects, utcNow);

        Assert.True(projects.ContainsKey("folder:exact"));
    }

    [Fact]
    public void PruneStaleProjects_NothingStale_ReturnsFalseAndKeepsEverything()
    {
        var utcNow = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
        var projects = new Dictionary<string, DebugProjectConfig>
        {
            ["folder:fresh"] = new() { LastAccessedUtc = utcNow },
        };

        bool removedAny = DebugConfigStore.PruneStaleProjects(projects, utcNow);

        Assert.False(removedAny);
        Assert.Single(projects);
    }

    [Fact]
    public void PruneStaleProjects_EmptyDictionary_DoesNotThrow()
    {
        var projects = new Dictionary<string, DebugProjectConfig>();
        Assert.False(DebugConfigStore.PruneStaleProjects(projects, DateTime.UtcNow));
    }

    // ── GetBreakpoints / UpdateBreakpoints ────────────────────────────────────

    [Fact]
    public void GetBreakpoints_UnknownProject_ReturnsEmpty()
    {
        var store = new DebugConfigStore();
        Assert.Empty(store.GetBreakpoints("folder:nonexistent"));
    }

    [Fact]
    public void UpdateBreakpoints_ThenGetBreakpoints_RoundTrips()
    {
        var store = new DebugConfigStore();
        var records = new[]
        {
            new DebugBreakpointRecord { FilePath = "main.bas", LineNumber = 100, Enabled = true },
            new DebugBreakpointRecord { FilePath = "main.bas", LineNumber = 200, Enabled = false },
        };

        store.UpdateBreakpoints("folder:test", records);
        var result = store.GetBreakpoints("folder:test");

        Assert.Equal(2, result.Count);
        Assert.Equal((ushort)100, result[0].LineNumber);
        Assert.True(result[0].Enabled);
        Assert.False(result[1].Enabled);
    }

    [Fact]
    public void UpdateBreakpoints_ReplacesPreviousList()
    {
        var store = new DebugConfigStore();
        store.UpdateBreakpoints("folder:test", new[] { new DebugBreakpointRecord { FilePath = "a.bas", LineNumber = 10 } });
        store.UpdateBreakpoints("folder:test", new[] { new DebugBreakpointRecord { FilePath = "b.bas", LineNumber = 20 } });

        var result = store.GetBreakpoints("folder:test");
        Assert.Single(result);
        Assert.Equal("b.bas", result[0].FilePath);
    }

    [Fact]
    public void UpdateBreakpoints_UpdatesLastAccessedUtc()
    {
        var store = new DebugConfigStore();
        store.Projects["folder:test"] = new DebugProjectConfig { LastAccessedUtc = DateTime.UtcNow.AddDays(-100) };

        store.UpdateBreakpoints("folder:test", Array.Empty<DebugBreakpointRecord>());

        Assert.True(store.Projects["folder:test"].LastAccessedUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    // ── GetFolderProjectKey ───────────────────────────────────────────────────

    [Fact]
    public void GetFolderProjectKey_IsCaseInsensitive()
    {
        string key1 = DebugConfigStore.GetFolderProjectKey(@"C:\Projects\MyGame");
        string key2 = DebugConfigStore.GetFolderProjectKey(@"c:\projects\mygame");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GetFolderProjectKey_HasFolderPrefix()
    {
        Assert.StartsWith("folder:", DebugConfigStore.GetFolderProjectKey(@"C:\Projects\MyGame"));
    }

    #endregion
}
