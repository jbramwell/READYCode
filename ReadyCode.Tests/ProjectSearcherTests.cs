// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Search;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="ProjectSearcher"/>.
/// </summary>
public class ProjectSearcherTests
{
    #region Public Methods

    // ── FindMatches: plain text ───────────────────────────────────────────────

    [Fact]
    public void FindMatches_PlainSubstring_FindsAllOccurrences()
    {
        var matches = ProjectSearcher.FindMatches("CAT SAT ON THE CAT MAT", "CAT", matchCase: true, wholeWord: false, useRegex: false);

        Assert.Equal(2, matches.Count);
        Assert.Equal((0, 3), matches[0]);
        Assert.Equal((15, 3), matches[1]);
    }

    [Fact]
    public void FindMatches_EmptySearchText_ReturnsNoMatches()
    {
        var matches = ProjectSearcher.FindMatches("SOME TEXT", "", matchCase: true, wholeWord: false, useRegex: false);
        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_NoOccurrences_ReturnsEmpty()
    {
        var matches = ProjectSearcher.FindMatches("HELLO WORLD", "GOODBYE", matchCase: true, wholeWord: false, useRegex: false);
        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_OverlappingCandidates_AdvancesByOneNotByMatchLength()
    {
        // "AAAA" searching for "AA" finds 3 overlapping matches (offsets 0,1,2), not 2
        // non-overlapping ones - confirms the scan advances past the match start, not its end.
        var matches = ProjectSearcher.FindMatches("AAAA", "AA", matchCase: true, wholeWord: false, useRegex: false);
        Assert.Equal(3, matches.Count);
    }

    // ── FindMatches: case sensitivity ─────────────────────────────────────────

    [Fact]
    public void FindMatches_MatchCaseTrue_IsCaseSensitive()
    {
        var matches = ProjectSearcher.FindMatches("Hello hello HELLO", "hello", matchCase: true, wholeWord: false, useRegex: false);
        Assert.Single(matches);
    }

    [Fact]
    public void FindMatches_MatchCaseFalse_IsCaseInsensitive()
    {
        var matches = ProjectSearcher.FindMatches("Hello hello HELLO", "hello", matchCase: false, wholeWord: false, useRegex: false);
        Assert.Equal(3, matches.Count);
    }

    // ── FindMatches: whole word ───────────────────────────────────────────────

    [Fact]
    public void FindMatches_WholeWordTrue_ExcludesPartialWordMatches()
    {
        var matches = ProjectSearcher.FindMatches("CAT CATALOG SCAT CAT", "CAT", matchCase: true, wholeWord: true, useRegex: false);
        // Only the two standalone "CAT" tokens match; "CATALOG" and "SCAT" don't.
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void FindMatches_WholeWordFalse_IncludesPartialWordMatches()
    {
        var matches = ProjectSearcher.FindMatches("CATALOG", "CAT", matchCase: true, wholeWord: false, useRegex: false);
        Assert.Single(matches);
    }

    // ── FindMatches: regex ────────────────────────────────────────────────────

    [Fact]
    public void FindMatches_Regex_FindsPatternMatches()
    {
        var matches = ProjectSearcher.FindMatches("LINE10 LINE20 LINE99", @"LINE\d+", matchCase: true, wholeWord: false, useRegex: true);
        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public void FindMatches_InvalidRegex_ReturnsEmptyRatherThanThrowing()
    {
        var matches = ProjectSearcher.FindMatches("SOME TEXT", "[unclosed", matchCase: true, wholeWord: false, useRegex: true);
        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_RegexZeroLengthMatch_IsExcluded()
    {
        // A pattern that can match zero-width against text with no actual hit shouldn't produce
        // a phantom zero-length match.
        var matches = ProjectSearcher.FindMatches("XYZ", "A*", matchCase: true, wholeWord: false, useRegex: true);
        Assert.Empty(matches);
    }

    // ── EnumerateSearchableFiles ──────────────────────────────────────────────

    [Fact]
    public void EnumerateSearchableFiles_OnlyReturnsSearchableExtensions()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.bas"), "");
            File.WriteAllText(Path.Combine(dir, "b.asm"), "");
            File.WriteAllText(Path.Combine(dir, "c.txt"), "");
            File.WriteAllText(Path.Combine(dir, "d.png"), "");
            File.WriteAllBytes(Path.Combine(dir, "e.d64"), new byte[10]);

            var found = ProjectSearcher.EnumerateSearchableFiles(dir).Select(Path.GetFileName).ToList();

            Assert.Contains("a.bas", found);
            Assert.Contains("b.asm", found);
            Assert.Contains("c.txt", found);
            Assert.DoesNotContain("d.png", found);
            Assert.DoesNotContain("e.d64", found); // disk images are read via DiskImage, not as text
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void EnumerateSearchableFiles_IncludesPrgFiles()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "f.prg"), new byte[10]);
            var found = ProjectSearcher.EnumerateSearchableFiles(dir).Select(Path.GetFileName).ToList();
            Assert.Contains("f.prg", found);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void EnumerateSearchableFiles_RecursesIntoSubfolders()
    {
        string dir = CreateTempDir();
        try
        {
            string sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "nested.bas"), "");

            var found = ProjectSearcher.EnumerateSearchableFiles(dir).Select(Path.GetFileName).ToList();
            Assert.Contains("nested.bas", found);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── ReadSearchableText / WriteSearchableText ──────────────────────────────

    [Fact]
    public void ReadSearchableText_PlainTextFile_ReturnsItsContent()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "a.bas");
            File.WriteAllText(path, "10 PRINT \"HI\"");
            Assert.Equal("10 PRINT \"HI\"", ProjectSearcher.ReadSearchableText(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadSearchableText_PrgFile_DetokenizesToBasicSource()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "a.prg");
            File.WriteAllBytes(path, new PrgConverter().ConvertToPrg("10 PRINT \"HI\""));

            string? text = ProjectSearcher.ReadSearchableText(path);

            Assert.NotNull(text);
            Assert.Contains("PRINT", text);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadSearchableText_NonBasicPrgFile_ReturnsNull()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "a.prg");
            File.WriteAllBytes(path, [0x01, 0x08, 0xA9, 0x00, 0x60]); // machine code, not BASIC

            Assert.Null(ProjectSearcher.ReadSearchableText(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WriteSearchableText_PrgPath_WritesRetokenizedBytes()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "a.prg");
            ProjectSearcher.WriteSearchableText(path, "10 PRINT \"BYE\"");

            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(new PrgConverter().IsBasicProgram(bytes));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WriteSearchableText_PlainTextPath_WritesTextVerbatim()
    {
        string dir = CreateTempDir();
        try
        {
            string path = Path.Combine(dir, "a.bas");
            ProjectSearcher.WriteSearchableText(path, "10 PRINT \"BYE\"");
            Assert.Equal("10 PRINT \"BYE\"", File.ReadAllText(path));
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
