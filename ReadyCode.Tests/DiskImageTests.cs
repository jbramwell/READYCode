// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using ReadyCode.C64U;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="DiskImage"/> and <see cref="DiskGeometry"/>. Runs the read/write
/// round-trip suite against both .d64 and .d81 geometry via <see cref="BothFormats"/>, since the
/// two formats share one implementation but differ in BAM byte layout.
/// </summary>
public class DiskImageTests
{
    #region Public Methods

    public static IEnumerable<object[]> BothFormats =>
    [
        [DiskGeometry.D64],
        [DiskGeometry.D81],
    ];

    // ── Blank image ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void CreateBlankImage_HasExactStandardSize(DiskGeometry geometry)
    {
        var image = new DiskImage(geometry).CreateBlankImage("TEST");
        Assert.Equal(geometry.StandardImageSize, image.Length);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void CreateBlankImage_HasNoDirectoryEntries(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        Assert.Empty(disk.ReadDirectory(image));
    }

    // ── Add / read round trip ─────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_SingleSectorContent_RoundTripsExactly(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        byte[] content = Encoding.ASCII.GetBytes("HELLO WORLD");

        image = disk.AddEntry(image, "HELLO", C64UFileKind.Prg, content);
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Equal("HELLO", entry.Name);
        Assert.Equal(content, entry.Content);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_MultiSectorContent_RoundTripsExactly(DiskGeometry geometry)
    {
        // 600 bytes spans three 254-byte data sectors, exercising the chain-link writing (not
        // just the single-sector terminal-marker case).
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        var content = new byte[600];
        for (int i = 0; i < content.Length; i++) content[i] = (byte)(i % 251);

        image = disk.AddEntry(image, "BIGFILE", C64UFileKind.Prg, content);
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Equal(content, entry.Content);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_ExactSectorMultipleContent_RoundTripsExactly(DiskGeometry geometry)
    {
        // Exactly 254 bytes - the boundary where the terminal sector is entirely full
        // (last-used-byte-offset byte must be 255, not overflow into a second sector).
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        var content = new byte[254];
        for (int i = 0; i < content.Length; i++) content[i] = (byte)i;

        image = disk.AddEntry(image, "EXACT", C64UFileKind.Prg, content);
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Equal(content, entry.Content);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_EmptyContent_RoundTripsAsZeroBytes(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");

        image = disk.AddEntry(image, "EMPTY", C64UFileKind.Prg, []);
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Empty(entry.Content);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_MultipleFiles_AllRoundTripIndependently(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");

        image = disk.AddEntry(image, "FIRST", C64UFileKind.Prg, Encoding.ASCII.GetBytes("AAA"));
        image = disk.AddEntry(image, "SECOND", C64UFileKind.Prg, Encoding.ASCII.GetBytes("BBBBB"));

        var entries = disk.ReadDirectory(image);
        Assert.Equal(2, entries.Count);
        Assert.Equal(Encoding.ASCII.GetBytes("AAA"), entries.Single(e => e.Name == "FIRST").Content);
        Assert.Equal(Encoding.ASCII.GetBytes("BBBBB"), entries.Single(e => e.Name == "SECOND").Content);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_DoesNotMutateTheInputArray(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var original = disk.CreateBlankImage("TEST");
        var originalCopy = (byte[])original.Clone();

        disk.AddEntry(original, "HELLO", C64UFileKind.Prg, [1, 2, 3]);

        Assert.Equal(originalCopy, original);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_MoreThanEightFiles_ExtendsDirectoryChain(DiskGeometry geometry)
    {
        // Each directory sector holds 8 entries; the 9th file forces a new sector to be
        // allocated and linked onto the chain.
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");

        for (int i = 0; i < 10; i++)
            image = disk.AddEntry(image, $"F{i}", C64UFileKind.Prg, Encoding.ASCII.GetBytes($"FILE{i}"));

        var entries = disk.ReadDirectory(image);
        Assert.Equal(10, entries.Count);
        for (int i = 0; i < 10; i++)
            Assert.Equal(Encoding.ASCII.GetBytes($"FILE{i}"), entries.Single(e => e.Name == $"F{i}").Content);
    }

    // ── Entry kind classification ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_TokenizedBasicContent_IsClassifiedAsPrg(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        byte[] basic = new PrgConverter().ConvertToPrg("10 PRINT \"HI\"");

        image = disk.AddEntry(image, "BAS", C64UFileKind.Prg, basic);
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Equal(C64UFileKind.Prg, entry.Kind);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void AddEntry_NonBasicContent_IsClassifiedAsMl(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        // Same load address header as a real PRG, but not a well-formed BASIC line chain.
        byte[] machineCode = [0x01, 0x08, 0xA9, 0x00, 0x60];

        image = disk.AddEntry(image, "ML", C64UFileKind.Prg, machineCode);
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Equal(C64UFileKind.Ml, entry.Kind);
    }

    // ── Rename ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void RenameEntry_OldNameGoneNewNamePresent(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        image = disk.AddEntry(image, "OLDNAME", C64UFileKind.Prg, [1, 2, 3]);

        image = disk.RenameEntry(image, "OLDNAME", "NEWNAME");
        var entries = disk.ReadDirectory(image);

        Assert.DoesNotContain(entries, e => e.Name == "OLDNAME");
        Assert.Contains(entries, e => e.Name == "NEWNAME");
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void RenameEntry_PreservesContent(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        byte[] content = Encoding.ASCII.GetBytes("UNCHANGED");
        image = disk.AddEntry(image, "OLDNAME", C64UFileKind.Prg, content);

        image = disk.RenameEntry(image, "OLDNAME", "NEWNAME");
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Equal(content, entry.Content);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void RenameEntry_MissingName_Throws(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        Assert.Throws<InvalidOperationException>(() => disk.RenameEntry(image, "NOSUCH", "NEW"));
    }

    // ── Replace ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void ReplaceEntry_UpdatesContentUnderSameName(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        image = disk.AddEntry(image, "FILE", C64UFileKind.Prg, [1, 2, 3]);

        image = disk.ReplaceEntry(image, "FILE", [9, 9, 9, 9]);
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Equal("FILE", entry.Name);
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, entry.Content);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void ReplaceEntry_MissingName_Throws(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        Assert.Throws<InvalidOperationException>(() => disk.ReplaceEntry(image, "NOSUCH", [1]));
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void DeleteEntry_RemovesEntryAndFreesItsSectors(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        image = disk.AddEntry(image, "FILE", C64UFileKind.Prg, new byte[600]);
        int freeBeforeDelete = disk.GetFreeSectors(image).Count;

        image = disk.DeleteEntry(image, "FILE");

        Assert.Empty(disk.ReadDirectory(image));
        Assert.True(disk.GetFreeSectors(image).Count > freeBeforeDelete);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void DeleteEntry_LeavesOtherEntriesIntact(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        image = disk.AddEntry(image, "KEEP", C64UFileKind.Prg, Encoding.ASCII.GetBytes("KEEPME"));
        image = disk.AddEntry(image, "GONE", C64UFileKind.Prg, Encoding.ASCII.GetBytes("BYE"));

        image = disk.DeleteEntry(image, "GONE");
        var entry = Assert.Single(disk.ReadDirectory(image));

        Assert.Equal("KEEP", entry.Name);
        Assert.Equal(Encoding.ASCII.GetBytes("KEEPME"), entry.Content);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void DeleteEntry_MissingName_Throws(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        Assert.Throws<InvalidOperationException>(() => disk.DeleteEntry(image, "NOSUCH"));
    }

    // ── GetFreeSectors ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void GetFreeSectors_DecreasesAsFilesAreAdded(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        var blank = disk.CreateBlankImage("TEST");
        int freeOnBlank = disk.GetFreeSectors(blank).Count;

        var withFile = disk.AddEntry(blank, "FILE", C64UFileKind.Prg, new byte[600]);
        int freeAfterAdd = disk.GetFreeSectors(withFile).Count;

        Assert.True(freeAfterAdd < freeOnBlank);
    }

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void GetFreeSectors_NeverIncludesReservedHeaderOrDirectorySectors(DiskGeometry geometry)
    {
        // The header/BAM/first-directory sectors on the reserved track are always used on a
        // freshly formatted disk - none of them should ever show up as "free".
        var disk = new DiskImage(geometry);
        var image = disk.CreateBlankImage("TEST");
        var free = disk.GetFreeSectors(image);

        Assert.DoesNotContain(free, s => s.Track == geometry.DirectoryTrack && s.Sector == 0);
        Assert.DoesNotContain(free, s => s.Track == geometry.DirectoryTrack && s.Sector == geometry.DirectorySector);
    }

    // ── ReadDirectory validation ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothFormats))]
    public void ReadDirectory_WrongSize_Throws(DiskGeometry geometry)
    {
        var disk = new DiskImage(geometry);
        Assert.Throws<InvalidOperationException>(() => disk.ReadDirectory(new byte[100]));
    }

    // ── ForKind factory ────────────────────────────────────────────────────────

    [Fact]
    public void ForKind_D64_UsesD64Geometry()
    {
        var image = DiskImage.ForKind(C64UFileKind.D64).CreateBlankImage("TEST");
        Assert.Equal(DiskGeometry.D64.StandardImageSize, image.Length);
    }

    [Fact]
    public void ForKind_D81_UsesD81Geometry()
    {
        var image = DiskImage.ForKind(C64UFileKind.D81).CreateBlankImage("TEST");
        Assert.Equal(DiskGeometry.D81.StandardImageSize, image.Length);
    }

    [Fact]
    public void ForKind_NonDiskImageKind_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DiskImage.ForKind(C64UFileKind.Prg));
    }

    #endregion
}
