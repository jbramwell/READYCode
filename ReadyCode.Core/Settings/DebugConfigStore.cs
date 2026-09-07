// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReadyCode.Settings;

/// <summary>
/// A single breakpoint as persisted in <see cref="DebugConfigStore"/> - a plain record type,
/// independent of <see cref="ReadyCode.Debugger.Breakpoint"/> (which is the live, bindable,
/// in-memory representation the debug panel and gutter margin actually use).
/// </summary>
public sealed class DebugBreakpointRecord
{
    /// <summary>
    /// Gets or sets the path of the file this breakpoint belongs to.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the BASIC line number this breakpoint halts execution at.
    /// </summary>
    public ushort LineNumber { get; set; }

    /// <summary>
    /// Gets or sets whether this breakpoint is currently active.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Debug configuration persisted for a single project: its breakpoints, watch expressions
/// (reserved for a future watch-expression feature), and default debug target.
/// </summary>
public sealed class DebugProjectConfig
{
    /// <summary>
    /// Gets or sets when this project's debug config was last accessed, used to prune entries
    /// that haven't been touched in a long time.
    /// </summary>
    public DateTime LastAccessedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets every breakpoint set in this project.
    /// </summary>
    public List<DebugBreakpointRecord> Breakpoints { get; set; } = new();

    /// <summary>
    /// Gets or sets the watch expressions saved for this project. Reserved for a future
    /// expression-evaluator feature - not read or written by the current debugger.
    /// </summary>
    public List<string> WatchExpressions { get; set; } = new();

    /// <summary>
    /// Gets or sets which target this project last debugged on ("Vice" - the only target
    /// implemented so far).
    /// </summary>
    public string DefaultDebugTarget { get; set; } = "Vice";
}

/// <summary>
/// Persists BASIC debugger configuration (breakpoints, watch expressions, default target) in
/// READYCode's own application data directory, keyed by a stable per-project identifier -
/// deliberately never written into the user's project folder or a disk image, per the project's
/// "app-level storage only" convention for debug state. A single JSON file holds every known
/// project's config; entries not accessed in 30+ days are pruned on load.
/// </summary>
public sealed class DebugConfigStore
{
    #region Private Fields

    private static readonly TimeSpan _pruneAge = TimeSpan.FromDays(30);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets every known project's debug config, keyed by its stable project key (see
    /// <see cref="GetFolderProjectKey"/>).
    /// </summary>
    [JsonPropertyName("projects")]
    public Dictionary<string, DebugProjectConfig> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Public Methods

    /// <summary>
    /// Builds the stable project key for a local, folder-based project - the only kind this
    /// release supports. The scheme is deliberately extensible (a distinct prefix, not a nested
    /// object) so a future disk-image/C64U project key (e.g. <c>"disk:" + fileName + "@" + host</c>)
    /// can be added without changing this file's shape.
    /// </summary>
    public static string GetFolderProjectKey(string folderPath) =>
        "folder:" + Path.GetFullPath(folderPath).ToLowerInvariant();

    /// <summary>
    /// Loads the store from disk, pruning (and immediately re-saving without) any project entry
    /// whose <see cref="DebugProjectConfig.LastAccessedUtc"/> is 30 or more days old. Falls back
    /// to an empty store if the file is missing or corrupt.
    /// </summary>
    public static DebugConfigStore Load()
    {
        DebugConfigStore store;
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                store = JsonSerializer.Deserialize<DebugConfigStore>(json) ?? new DebugConfigStore();
            }
            else
            {
                store = new DebugConfigStore();
            }
        }
        catch (Exception)
        {
            store = new DebugConfigStore();
        }

        if (PruneStaleProjects(store.Projects, DateTime.UtcNow))
            store.Save();

        return store;
    }

    /// <summary>
    /// Removes every project entry whose <see cref="DebugProjectConfig.LastAccessedUtc"/> is 30
    /// or more days before <paramref name="utcNow"/> - the pure logic behind <see cref="Load"/>'s
    /// pruning, split out so it's testable without touching disk.
    /// </summary>
    /// <returns>True if anything was removed.</returns>
    public static bool PruneStaleProjects(Dictionary<string, DebugProjectConfig> projects, DateTime utcNow)
    {
        DateTime cutoff = utcNow - _pruneAge;
        var stale = projects.Where(kvp => kvp.Value.LastAccessedUtc < cutoff).Select(kvp => kvp.Key).ToList();

        foreach (string key in stale)
            projects.Remove(key);

        return stale.Count > 0;
    }

    /// <summary>
    /// Gets the breakpoints saved for the given project, or an empty list if none are saved.
    /// </summary>
    public IReadOnlyList<DebugBreakpointRecord> GetBreakpoints(string projectKey) =>
        Projects.TryGetValue(projectKey, out var config) ? config.Breakpoints : Array.Empty<DebugBreakpointRecord>();

    /// <summary>
    /// Replaces the given project's breakpoints and updates its last-accessed time, in memory
    /// only - callers that want this persisted immediately should call <see cref="Save"/>
    /// afterward (kept separate so tests can exercise this without touching disk).
    /// </summary>
    public void UpdateBreakpoints(string projectKey, IEnumerable<DebugBreakpointRecord> breakpoints)
    {
        if (!Projects.TryGetValue(projectKey, out var config))
        {
            config = new DebugProjectConfig();
            Projects[projectKey] = config;
        }

        config.Breakpoints = breakpoints.ToList();
        config.LastAccessedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Replaces the given project's breakpoints, updates its last-accessed time, and saves to disk.
    /// </summary>
    public void SaveBreakpoints(string projectKey, IEnumerable<DebugBreakpointRecord> breakpoints)
    {
        UpdateBreakpoints(projectKey, breakpoints);
        Save();
    }

    /// <summary>
    /// Persists the store to disk.
    /// </summary>
    public void Save()
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    #endregion

    #region Private Properties

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "READYCode", "debug-config.json");

    #endregion
}
