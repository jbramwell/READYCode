// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using ReadyCode.IO;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="AtomicFile"/>.
/// </summary>
public class AtomicFileTests
{
    #region Public Methods

    [Fact]
    public void WriteAllText_ProducesSameBytesAsFileWriteAllText()
    {
        // Callers switched from File.WriteAllText(..., Encoding.UTF8) - the on-disk format
        // (including the UTF-8 BOM that overload writes) must not change.
        string dir = CreateTempDir();
        try
        {
            const string text = "10 PRINT \"HELLO\"\r\n20 GOTO 10 ← π\r\n";
            string expectedPath = Path.Combine(dir, "expected.bas");
            string actualPath = Path.Combine(dir, "actual.bas");

            File.WriteAllText(expectedPath, text, Encoding.UTF8);
            AtomicFile.WriteAllText(actualPath, text, Encoding.UTF8);

            Assert.Equal(File.ReadAllBytes(expectedPath), File.ReadAllBytes(actualPath));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WriteAllBytes_NewFile_CreatesIt()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "new.prg");
            AtomicFile.WriteAllBytes(path, [0x01, 0x08, 0x00]);

            Assert.Equal([0x01, 0x08, 0x00], File.ReadAllBytes(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WriteAllBytes_ExistingFile_ReplacesContentAndLeavesNoTempFile()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "disk.d64");
            File.WriteAllBytes(path, [1, 2, 3, 4, 5]);

            AtomicFile.WriteAllBytes(path, [9, 8]);

            Assert.Equal([9, 8], File.ReadAllBytes(path));
            Assert.Equal(["disk.d64"], Directory.GetFiles(dir).Select(Path.GetFileName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WriteAllBytes_ExistingFile_KeepsCreationTime()
    {
        // The swap must replace the file's content, not leave what looks like a brand-new file.
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "program.bas");
            File.WriteAllText(path, "old");
            var created = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetCreationTimeUtc(path, created);

            AtomicFile.WriteAllBytes(path, [0x41]);

            Assert.Equal(created, File.GetCreationTimeUtc(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WriteAllBytes_FileHeldOpenForWriting_FallsBackToWritingInPlace()
    {
        // Mimics an emulator with a disk image attached: open for read/write sharing but not
        // delete, so the file can be written but not replaced. A plain File.WriteAllBytes
        // succeeds here, so the atomic version must too.
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "attached.d64");
            File.WriteAllBytes(path, [1, 2, 3]);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                AtomicFile.WriteAllBytes(path, [7, 7]);
            }

            Assert.Equal([7, 7], File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WriteAllBytes_FileLockedExclusively_ThrowsAndLeavesOriginalIntact()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "locked.prg");
            File.WriteAllBytes(path, [1, 2, 3]);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => AtomicFile.WriteAllBytes(path, [7, 7]));
            }

            Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WriteAllBytes_TempFileCannotBeWritten_ThrowsWithoutTouchingOriginal()
    {
        // Stands in for a full disk: the new content can't be staged, so the original must not
        // be truncated by an in-place fallback write. A directory occupying the temp file's
        // name (the name AtomicFile uses) makes creating it fail.
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "program.prg");
            File.WriteAllBytes(path, [1, 2, 3]);
            Directory.CreateDirectory($"{path}.{Environment.ProcessId}.tmp");

            Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllBytes(path, [7, 7]));

            Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
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
}
