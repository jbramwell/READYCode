// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Logging;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="ErrorLog"/>.
/// </summary>
public class ErrorLogTests
{
    #region Public Methods

    [Fact]
    public void Write_AppendsSourceAndExceptionDetails()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "error.log");
            ErrorLog.Write(path, new InvalidOperationException("first failure"), "UI thread");
            ErrorLog.Write(path, new ArgumentException("second failure"), "Unobserved task");

            string text = File.ReadAllText(path);
            Assert.Contains("UI thread", text);
            Assert.Contains("System.InvalidOperationException: first failure", text);
            Assert.Contains("Unobserved task", text);
            Assert.Contains("System.ArgumentException: second failure", text);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_CreatesMissingDirectory()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "nested", "error.log");
            ErrorLog.Write(path, new Exception("boom"), "UI thread");

            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_OversizedLog_RollsOverToOldFile()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "error.log");
            File.WriteAllText(path, new string('x', 1024 * 1024 + 1));

            ErrorLog.Write(path, new Exception("after rollover"), "UI thread");

            Assert.True(File.Exists(path + ".old"));
            string text = File.ReadAllText(path);
            Assert.Contains("after rollover", text);
            Assert.DoesNotContain("xxxx", text);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_UnwritablePath_DoesNotThrow()
    {
        // A path that names an existing directory can't be opened as a file - the write fails,
        // and that failure must be swallowed rather than escape the global exception handler.
        string dir = CreateTempDir();
        try
        {
            ErrorLog.Write(dir, new Exception("boom"), "UI thread");
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
