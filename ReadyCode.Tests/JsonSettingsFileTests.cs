// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Settings;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="JsonSettingsFile"/>.
/// </summary>
public class JsonSettingsFileTests
{
    #region Public Methods

    // ── Save ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_RoundTripsValue()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            JsonSettingsFile.Save(path, new SampleSettings { Name = "C64", Count = 64 });

            var loaded = JsonSettingsFile.Load<SampleSettings>(path);

            Assert.NotNull(loaded);
            Assert.Equal("C64", loaded.Name);
            Assert.Equal(64, loaded.Count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "nested", "settings.json");
            JsonSettingsFile.Save(path, new SampleSettings());

            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_KeepsPreviousContentAsBackup()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            JsonSettingsFile.Save(path, new SampleSettings { Name = "first" });
            JsonSettingsFile.Save(path, new SampleSettings { Name = "second" });

            Assert.Contains("second", File.ReadAllText(path));
            Assert.Contains("first", File.ReadAllText(JsonSettingsFile.GetBackupPath(path)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_LeavesOnlySettingsAndBackupFiles()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            JsonSettingsFile.Save(path, new SampleSettings { Name = "first" });
            JsonSettingsFile.Save(path, new SampleSettings { Name = "second" });

            var files = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(f => f).ToArray();
            Assert.Equal(["settings.json", "settings.json.bak"], files);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_FailsPartway_LeavesOriginalIntactAndNoTempFile()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            JsonSettingsFile.Save(path, new SampleSettings { Name = "original" });
            string originalJson = File.ReadAllText(path);

            // Holding the file open exclusively makes the backup/swap step fail after the temp
            // file has already been written - the point where a naive in-place write would have
            // already destroyed the original.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => JsonSettingsFile.Save(path, new SampleSettings { Name = "replacement" }));
            }

            Assert.Equal(originalJson, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_NoFiles_ReturnsNullWithoutReportingAnError()
    {
        string dir = CreateTempDir();
        try
        {
            var errors = new List<string>();
            var loaded = JsonSettingsFile.Load<SampleSettings>(Path.Combine(dir, "settings.json"), (_, source) => errors.Add(source));

            Assert.Null(loaded);
            Assert.Empty(errors);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_CorruptMainFile_FallsBackToBackupAndReportsError()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            JsonSettingsFile.Save(path, new SampleSettings { Name = "good" });
            JsonSettingsFile.Save(path, new SampleSettings { Name = "newer" });
            File.WriteAllText(path, "{ \"Name\": \"trunc"); // simulate a damaged file

            var errors = new List<(Exception Exception, string Source)>();
            var loaded = JsonSettingsFile.Load<SampleSettings>(path, (ex, source) => errors.Add((ex, source)));

            Assert.NotNull(loaded);
            Assert.Equal("good", loaded.Name);
            var error = Assert.Single(errors);
            Assert.Equal("Reading settings.json", error.Source);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_CorruptMainFileAndNoBackup_ReturnsNull()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "not json at all");

            Assert.Null(JsonSettingsFile.Load<SampleSettings>(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_EmptyMainFile_FallsBackToBackup()
    {
        // A zero-length file is what an interrupted in-place write typically leaves behind.
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            JsonSettingsFile.Save(path, new SampleSettings { Name = "good" });
            JsonSettingsFile.Save(path, new SampleSettings { Name = "newer" });
            File.WriteAllText(path, "");

            Assert.Equal("good", JsonSettingsFile.Load<SampleSettings>(path)?.Name);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    #endregion

    #region Private Methods

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ReadyCodeTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    #endregion

    #region Private Classes

    private sealed class SampleSettings
    {
        public string Name { get; set; } = "";

        public int Count { get; set; }
    }

    #endregion
}
