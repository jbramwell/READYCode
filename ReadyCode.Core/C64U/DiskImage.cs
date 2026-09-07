// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.C64U;

/// <summary>
/// A single file entry read from a disk image's directory.
/// </summary>
public class D64Entry
{
    #region Public Properties

    /// <summary>
    /// Gets the file's name, decoded from PETSCII.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the file kind, derived from the disk's file type byte.
    /// </summary>
    public required C64UFileKind Kind { get; init; }

    /// <summary>
    /// Gets the file's raw content, extracted by following its sector chain.
    /// </summary>
    public required byte[] Content { get; init; }

    #endregion
}

/// <summary>
/// Reads the directory and file contents of a CBM DOS disk image (.d64 or .d81), using the
/// track/sector layout supplied by a <see cref="DiskGeometry"/>. The directory entry format (32
/// bytes/entry, 8 entries/sector) and sector-chain-walk algorithm are identical between formats -
/// only the geometry differs.
/// </summary>
public class DiskImage
{
    #region Private Fields

    private readonly DiskGeometry _geometry;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskImage"/> class for the given geometry.
    /// </summary>
    /// <param name="geometry">The track/sector layout to read/write against.</param>
    public DiskImage(DiskGeometry geometry)
    {
        _geometry = geometry;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Creates a <see cref="DiskImage"/> for the given file kind's disk format.
    /// </summary>
    /// <param name="kind">A disk image kind (<see cref="C64UFileKind.D64"/> or <see cref="C64UFileKind.D81"/>).</param>
    public static DiskImage ForKind(C64UFileKind kind) => new(DiskGeometry.ForKind(kind));

    /// <summary>
    /// Reads the directory of a disk image, extracting the content of every entry.
    /// </summary>
    /// <param name="diskImage">The raw bytes of the disk image file.</param>
    /// <returns>The disk's directory entries, in on-disk order.</returns>
    public List<D64Entry> ReadDirectory(byte[] diskImage)
    {
        ValidateImageSize(diskImage);

        var entries = new List<D64Entry>();
        int track = _geometry.DirectoryTrack;
        int sector = _geometry.DirectorySector;
        int steps = 0;

        while (track != 0 && steps++ < _geometry.MaxChainSteps)
        {
            var dirSector = ReadSector(diskImage, track, sector);
            int nextTrack = dirSector[0];
            int nextSector = dirSector[1];

            for (int i = 0; i < 8; i++)
            {
                int entryOffset = 2 + i * 32;
                byte typeByte = dirSector[entryOffset];
                if ((typeByte & 0x80) == 0) continue; // not closed (scratched/invalid) - skip

                int fileTrack = dirSector[entryOffset + 1];
                int fileSector = dirSector[entryOffset + 2];
                var nameBytes = dirSector.AsSpan(entryOffset + 3, 16);

                string name = DecodeName(nameBytes);
                if (name.Length == 0) continue;

                byte[] content = ReadFileChain(diskImage, fileTrack, fileSector);

                // Only genuine, well-formed tokenized BASIC is openable in the editor - a PRG
                // file type also covers machine-language programs and BASIC "loader" stubs
                // with raw code appended, which IsBasicProgram correctly rejects.
                bool isPrgType = (typeByte & 0x0F) == 2;
                bool isBasic = isPrgType && new PrgConverter().IsBasicProgram(content);

                entries.Add(new D64Entry
                {
                    Name = name,
                    Kind = isBasic ? C64UFileKind.Prg : isPrgType ? C64UFileKind.Ml : C64UFileKind.Other,
                    Content = content,
                });
            }

            track = nextTrack;
            sector = nextSector;
        }

        return entries;
    }

    /// <summary>
    /// Builds a blank, correctly formatted disk image: a fully free BAM (except the sectors the
    /// header/BAM/first directory sector themselves occupy), a disk name, and an empty directory.
    /// </summary>
    /// <param name="diskName">The disk name to write into the header (truncated to 16 characters).</param>
    /// <returns>The new image's raw bytes.</returns>
    public byte[] CreateBlankImage(string diskName)
    {
        var image = new byte[_geometry.StandardImageSize];
        InitializeBam(image, diskName);

        int dirSectorOffset = SectorOffset(_geometry.DirectoryTrack, _geometry.DirectorySector);
        image[dirSectorOffset] = 0;
        image[dirSectorOffset + 1] = 0xFF; // end of directory chain; the 8 entries below are left zeroed (free)

        return image;
    }

    /// <summary>
    /// Gets every currently free (track, sector) pair on the disk, per the BAM.
    /// </summary>
    /// <param name="diskImage">The raw bytes of the disk image file.</param>
    public List<(int Track, int Sector)> GetFreeSectors(byte[] diskImage)
    {
        ValidateImageSize(diskImage);

        var free = new List<(int, int)>();
        int totalTracks = _geometry.SectorsPerTrack.Length - 1;
        for (int t = 1; t <= totalTracks; t++)
        {
            int sectorsOnTrack = _geometry.SectorsPerTrack[t];
            for (int s = 0; s < sectorsOnTrack; s++)
                if (IsSectorFree(diskImage, t, s))
                    free.Add((t, s));
        }
        return free;
    }

    /// <summary>
    /// Adds a new PRG entry to the disk: allocates and writes a sector chain for its content,
    /// then writes a directory entry for it in the first free directory slot (extending the
    /// directory's own sector chain if none remain).
    /// </summary>
    /// <param name="diskImage">The raw bytes of the disk image file.</param>
    /// <param name="name">The entry's name, as it will appear in the directory (PETSCII-encoded, truncated to 16 characters).</param>
    /// <param name="kind">The entry's kind. Currently always written as a PRG file on disk, matching real CBM DOS (which has no separate BASIC/machine-language file type).</param>
    /// <param name="content">The entry's raw content.</param>
    /// <returns>The updated image's raw bytes; <paramref name="diskImage"/> is left unmodified.</returns>
    /// <exception cref="InvalidOperationException">The disk has no free sectors or directory entries remaining.</exception>
    public byte[] AddEntry(byte[] diskImage, string name, C64UFileKind kind, byte[] content)
    {
        ValidateImageSize(diskImage);
        var image = (byte[])diskImage.Clone();

        var chain = AllocateSectorChain(image, SectorsNeeded(content.Length));
        WriteFileChain(image, chain, content);

        var slot = FindFreeDirectorySlotOrExtend(image);
        WriteDirectoryEntry(image, slot, name, chain[0].Track, chain[0].Sector);

        return image;
    }

    /// <summary>
    /// Removes an entry from the disk: scratches its directory entry and frees its sector chain
    /// in the BAM.
    /// </summary>
    /// <param name="diskImage">The raw bytes of the disk image file.</param>
    /// <param name="name">The entry's name, as it appears in the directory.</param>
    /// <returns>The updated image's raw bytes; <paramref name="diskImage"/> is left unmodified.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="name"/> was not found on this disk.</exception>
    public byte[] DeleteEntry(byte[] diskImage, string name)
    {
        ValidateImageSize(diskImage);
        var image = (byte[])diskImage.Clone();

        var slot = FindDirectorySlotByName(image, name)
            ?? throw new InvalidOperationException($"'{name}' was not found on this disk.");
        int entryOffset = SectorOffset(slot.Track, slot.Sector) + 2 + slot.Index * 32;
        int fileTrack = image[entryOffset + 1];
        int fileSector = image[entryOffset + 2];

        image[entryOffset] &= 0x7F; // clear the "closed" bit - scratches the entry, matching real DOS's delete
        FreeSectorChain(image, fileTrack, fileSector);

        return image;
    }

    /// <summary>
    /// Renames an entry in place - only its directory entry's name field changes; its content and
    /// sector chain are untouched.
    /// </summary>
    /// <param name="diskImage">The raw bytes of the disk image file.</param>
    /// <param name="oldName">The entry's current name.</param>
    /// <param name="newName">The entry's new name (truncated to 16 characters).</param>
    /// <returns>The updated image's raw bytes; <paramref name="diskImage"/> is left unmodified.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="oldName"/> was not found on this disk.</exception>
    public byte[] RenameEntry(byte[] diskImage, string oldName, string newName)
    {
        ValidateImageSize(diskImage);
        var image = (byte[])diskImage.Clone();

        var slot = FindDirectorySlotByName(image, oldName)
            ?? throw new InvalidOperationException($"'{oldName}' was not found on this disk.");
        int entryOffset = SectorOffset(slot.Track, slot.Sector) + 2 + slot.Index * 32;
        Array.Copy(EncodeName(newName, 16), 0, image, entryOffset + 3, 16);

        return image;
    }

    /// <summary>
    /// Replaces an entry's content in place: frees its old sector chain and directory slot, then
    /// adds it back under the same name with the new content.
    /// </summary>
    /// <param name="diskImage">The raw bytes of the disk image file.</param>
    /// <param name="name">The entry's name, as it appears in the directory.</param>
    /// <param name="newContent">The entry's new content.</param>
    /// <returns>The updated image's raw bytes; <paramref name="diskImage"/> is left unmodified.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="name"/> was not found, or the disk has no room for the new content.</exception>
    public byte[] ReplaceEntry(byte[] diskImage, string name, byte[] newContent)
    {
        var image = DeleteEntry(diskImage, name);
        return AddEntry(image, name, C64UFileKind.Prg, newContent);
    }

    #endregion

    #region Private Methods

    private byte[] ReadFileChain(byte[] diskImage, int track, int sector)
    {
        using var content = new MemoryStream();
        int steps = 0;

        while (track != 0 && steps++ < _geometry.MaxChainSteps)
        {
            var data = ReadSector(diskImage, track, sector);
            int nextTrack = data[0];
            int nextSector = data[1];

            if (nextTrack == 0)
            {
                // nextSector holds the offset of the last used byte in this final sector, inclusive.
                content.Write(data, 2, Math.Max(0, nextSector - 1));
                break;
            }

            content.Write(data, 2, 254);
            track = nextTrack;
            sector = nextSector;
        }

        return content.ToArray();
    }

    private byte[] ReadSector(byte[] diskImage, int track, int sector)
    {
        int offset = SectorOffset(track, sector);
        return diskImage.AsSpan(offset, 256).ToArray();
    }

    private int SectorOffset(int track, int sector)
    {
        var sectorsPerTrack = _geometry.SectorsPerTrack;
        if (track < 1 || track >= sectorsPerTrack.Length)
            throw new InvalidOperationException($"Invalid track {track} in disk image directory/file chain.");

        int sectorsBefore = 0;
        for (int t = 1; t < track; t++)
            sectorsBefore += sectorsPerTrack[t];

        return (sectorsBefore + sector) * 256;
    }

    // Decodes a 16-byte PETSCII filename field. Bytes 0x20-0x5F are identical to ASCII in that
    // range (digits, uppercase letters, and common punctuation), which covers the vast majority
    // of real disk filenames; anything else becomes '?'. Trailing 0xA0 padding is trimmed.
    private static string DecodeName(ReadOnlySpan<byte> raw)
    {
        int length = raw.Length;
        while (length > 0 && raw[length - 1] == 0xA0)
            length--;

        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            byte b = raw[i];
            chars[i] = b is >= 0x20 and <= 0x5F ? (char)b : '?';
        }

        return new string(chars);
    }

    private void ValidateImageSize(byte[] diskImage)
    {
        if (diskImage.Length != _geometry.StandardImageSize)
            throw new InvalidOperationException(
                $"Not a standard disk image ({diskImage.Length} bytes; expected {_geometry.StandardImageSize}).");
    }

    private void WriteSector(byte[] diskImage, int track, int sector, byte[] data)
        => Array.Copy(data, 0, diskImage, SectorOffset(track, sector), 256);

    // A file's data occupies 254 bytes per sector (256 minus the 2-byte next-sector link);
    // an empty file still needs one sector to hold its (empty) terminal marker.
    private static int SectorsNeeded(int contentLength) =>
        contentLength == 0 ? 1 : (contentLength + 253) / 254;

    // Writes a file's content across an already-allocated sector chain, in the same link format
    // ReadFileChain reads: each non-final sector is [nextTrack][nextSector][254 data bytes], and
    // the final sector is [0][lastUsedByteOffset+1][remaining data bytes] - the inverse of
    // ReadFileChain's `content.Write(data, 2, Math.Max(0, nextSector - 1))`.
    private void WriteFileChain(byte[] diskImage, List<(int Track, int Sector)> chain, byte[] content)
    {
        int offset = 0;
        for (int i = 0; i < chain.Count; i++)
        {
            var sectorData = new byte[256];
            bool isLast = i == chain.Count - 1;
            if (isLast)
            {
                int remaining = content.Length - offset;
                sectorData[0] = 0;
                sectorData[1] = (byte)(remaining + 1);
                Array.Copy(content, offset, sectorData, 2, remaining);
            }
            else
            {
                var (nextTrack, nextSector) = chain[i + 1];
                sectorData[0] = (byte)nextTrack;
                sectorData[1] = (byte)nextSector;
                Array.Copy(content, offset, sectorData, 2, 254);
                offset += 254;
            }

            var (track, sector) = chain[i];
            WriteSector(diskImage, track, sector, sectorData);
        }
    }

    // Allocates `count` free sectors for file data (marking them used in the BAM), skipping the
    // header/BAM/directory track entirely - real CBM DOS reserves that track for directory use
    // only, never file content.
    private List<(int Track, int Sector)> AllocateSectorChain(byte[] diskImage, int count)
    {
        var result = new List<(int, int)>();
        int reservedTrack = _geometry.DirectoryTrack;
        int totalTracks = _geometry.SectorsPerTrack.Length - 1;

        for (int t = 1; t <= totalTracks && result.Count < count; t++)
        {
            if (t == reservedTrack) continue;
            int sectorsOnTrack = _geometry.SectorsPerTrack[t];
            for (int s = 0; s < sectorsOnTrack && result.Count < count; s++)
                if (IsSectorFree(diskImage, t, s))
                    result.Add((t, s));
        }

        if (result.Count < count)
            throw new InvalidOperationException("Not enough free space on this disk image.");

        foreach (var (t, s) in result)
            SetSectorFree(diskImage, t, s, false);

        return result;
    }

    // Frees every sector in a file's chain (deletion), following the same links ReadFileChain does.
    private void FreeSectorChain(byte[] diskImage, int track, int sector)
    {
        int steps = 0;
        while (track != 0 && steps++ < _geometry.MaxChainSteps)
        {
            int sectorOffset = SectorOffset(track, sector);
            int nextTrack = diskImage[sectorOffset];
            int nextSector = diskImage[sectorOffset + 1];
            SetSectorFree(diskImage, track, sector, true);
            track = nextTrack;
            sector = nextSector;
        }
    }

    // Finds the first free (never-used or scratched) directory slot, walking the existing
    // directory sector chain. If every existing sector is full, allocates and links a new
    // directory sector (preferring a free sector on the header/BAM/directory track itself, like
    // real DOS, falling back to any free sector elsewhere if that track is completely full).
    private (int Track, int Sector, int Index) FindFreeDirectorySlotOrExtend(byte[] diskImage)
    {
        int track = _geometry.DirectoryTrack;
        int sector = _geometry.DirectorySector;
        int steps = 0;
        int lastTrack = track, lastSector = sector;

        while (track != 0 && steps++ < _geometry.MaxChainSteps)
        {
            int sectorOffset = SectorOffset(track, sector);
            for (int i = 0; i < 8; i++)
            {
                byte typeByte = diskImage[sectorOffset + 2 + i * 32];
                if ((typeByte & 0x80) == 0)
                    return (track, sector, i);
            }

            lastTrack = track;
            lastSector = sector;
            int nextTrack = diskImage[sectorOffset];
            int nextSector = diskImage[sectorOffset + 1];
            if (nextTrack == 0) break;
            track = nextTrack;
            sector = nextSector;
        }

        var (newTrack, newSector) = AllocateSectorOnTrack(diskImage, _geometry.DirectoryTrack);

        int lastSectorOffset = SectorOffset(lastTrack, lastSector);
        diskImage[lastSectorOffset] = (byte)newTrack;
        diskImage[lastSectorOffset + 1] = (byte)newSector;

        int newSectorOffset = SectorOffset(newTrack, newSector);
        Array.Clear(diskImage, newSectorOffset, 256);
        diskImage[newSectorOffset] = 0;
        diskImage[newSectorOffset + 1] = 0xFF;

        return (newTrack, newSector, 0);
    }

    private (int Track, int Sector) AllocateSectorOnTrack(byte[] diskImage, int track)
    {
        int sectorsOnTrack = _geometry.SectorsPerTrack[track];
        for (int s = 0; s < sectorsOnTrack; s++)
        {
            if (IsSectorFree(diskImage, track, s))
            {
                SetSectorFree(diskImage, track, s, false);
                return (track, s);
            }
        }

        return AllocateSectorChain(diskImage, 1)[0];
    }

    private void WriteDirectoryEntry(byte[] diskImage, (int Track, int Sector, int Index) slot, string name, int fileTrack, int fileSector)
    {
        int entryOffset = SectorOffset(slot.Track, slot.Sector) + 2 + slot.Index * 32;
        diskImage[entryOffset] = 0x82; // closed (0x80) + PRG type (0x02)
        diskImage[entryOffset + 1] = (byte)fileTrack;
        diskImage[entryOffset + 2] = (byte)fileSector;
        Array.Copy(EncodeName(name, 16), 0, diskImage, entryOffset + 3, 16);
    }

    private (int Track, int Sector, int Index)? FindDirectorySlotByName(byte[] diskImage, string name)
    {
        int track = _geometry.DirectoryTrack;
        int sector = _geometry.DirectorySector;
        int steps = 0;
        string target = name.ToUpperInvariant();

        while (track != 0 && steps++ < _geometry.MaxChainSteps)
        {
            int sectorOffset = SectorOffset(track, sector);
            for (int i = 0; i < 8; i++)
            {
                int entryOffset = 2 + i * 32;
                byte typeByte = diskImage[sectorOffset + entryOffset];
                if ((typeByte & 0x80) == 0) continue;

                string entryName = DecodeName(diskImage.AsSpan(sectorOffset + entryOffset + 3, 16));
                if (entryName == target)
                    return (track, sector, i);
            }

            int nextTrack = diskImage[sectorOffset];
            int nextSector = diskImage[sectorOffset + 1];
            track = nextTrack;
            sector = nextSector;
        }

        return null;
    }

    // Builds a fully free BAM (correct per-track free counts and bitmaps for every data track),
    // then reserves the header/BAM/first-directory sectors as used, and writes the disk
    // name/ID/DOS-type header fields. The BAM byte layout differs between formats (D64: one BAM
    // sector, 4 bytes/track; D81: two BAM sectors, 6 bytes/track) - everything else about
    // building a blank image is shared.
    private void InitializeBam(byte[] diskImage, string diskName)
    {
        int totalTracks = _geometry.SectorsPerTrack.Length - 1;
        int bitmapBytes = _geometry.Format == DiskFormat.D64 ? 3 : 5;

        for (int t = 1; t <= totalTracks; t++)
        {
            var (bamTrack, bamSector, byteOffset) = LocateBamEntry(t);
            int sectorOffset = SectorOffset(bamTrack, bamSector);
            int count = _geometry.SectorsPerTrack[t];
            diskImage[sectorOffset + byteOffset] = (byte)count;

            for (int b = 0; b < bitmapBytes; b++)
            {
                int bitsInThisByte = Math.Clamp(count - b * 8, 0, 8);
                diskImage[sectorOffset + byteOffset + 1 + b] = bitsInThisByte == 0 ? (byte)0 : (byte)((1 << bitsInThisByte) - 1);
            }
        }

        if (_geometry.Format == DiskFormat.D64)
        {
            SetSectorFree(diskImage, _geometry.DirectoryTrack, 0, false); // combined header/BAM sector
        }
        else
        {
            SetSectorFree(diskImage, _geometry.DirectoryTrack, 0, false); // header
            SetSectorFree(diskImage, _geometry.DirectoryTrack, 1, false); // BAM (tracks 1-40)
            SetSectorFree(diskImage, _geometry.DirectoryTrack, 2, false); // BAM (tracks 41-80)
        }
        SetSectorFree(diskImage, _geometry.DirectoryTrack, _geometry.DirectorySector, false); // first directory sector

        WriteHeader(diskImage, diskName);
    }

    private void WriteHeader(byte[] diskImage, string diskName)
    {
        byte[] nameBytes = EncodeName(diskName, 16);
        byte[] diskId = [(byte)'0', (byte)'1']; // arbitrary but valid - only matters for disk-swap prompts on real hardware

        if (_geometry.Format == DiskFormat.D64)
        {
            int bamOffset = SectorOffset(_geometry.DirectoryTrack, 0);
            diskImage[bamOffset + 0x00] = (byte)_geometry.DirectoryTrack;
            diskImage[bamOffset + 0x01] = (byte)_geometry.DirectorySector;
            diskImage[bamOffset + 0x02] = (byte)'A'; // DOS version
            diskImage[bamOffset + 0x03] = 0;
            Array.Copy(nameBytes, 0, diskImage, bamOffset + 0x90, 16);
            diskImage[bamOffset + 0xA0] = 0xA0;
            diskImage[bamOffset + 0xA1] = 0xA0;
            diskImage[bamOffset + 0xA2] = diskId[0];
            diskImage[bamOffset + 0xA3] = diskId[1];
            diskImage[bamOffset + 0xA4] = 0xA0;
            diskImage[bamOffset + 0xA5] = (byte)'2';
            diskImage[bamOffset + 0xA6] = (byte)'A';
            diskImage[bamOffset + 0xA7] = 0xA0;
            diskImage[bamOffset + 0xA8] = 0xA0;
            diskImage[bamOffset + 0xA9] = 0xA0;
            diskImage[bamOffset + 0xAA] = 0xA0;
        }
        else
        {
            int headerOffset = SectorOffset(_geometry.DirectoryTrack, 0);
            diskImage[headerOffset + 0x00] = (byte)_geometry.DirectoryTrack;
            diskImage[headerOffset + 0x01] = (byte)_geometry.DirectorySector;
            diskImage[headerOffset + 0x02] = (byte)'D'; // DOS version
            diskImage[headerOffset + 0x03] = 0;
            Array.Copy(nameBytes, 0, diskImage, headerOffset + 0x04, 16);
            diskImage[headerOffset + 0x14] = 0xA0;
            diskImage[headerOffset + 0x15] = 0xA0;
            diskImage[headerOffset + 0x16] = diskId[0];
            diskImage[headerOffset + 0x17] = diskId[1];
            diskImage[headerOffset + 0x18] = 0xA0;
            diskImage[headerOffset + 0x19] = (byte)'3';
            diskImage[headerOffset + 0x1A] = (byte)'D';
            diskImage[headerOffset + 0x1B] = 0xA0;
            diskImage[headerOffset + 0x1C] = 0xA0;

            int bam1Offset = SectorOffset(_geometry.DirectoryTrack, 1);
            diskImage[bam1Offset + 0] = (byte)_geometry.DirectoryTrack;
            diskImage[bam1Offset + 1] = 2;
            diskImage[bam1Offset + 2] = (byte)'D';
            diskImage[bam1Offset + 3] = 0;
            diskImage[bam1Offset + 4] = diskId[0];
            diskImage[bam1Offset + 5] = diskId[1];
            diskImage[bam1Offset + 6] = (byte)'D';
            diskImage[bam1Offset + 7] = 0;

            int bam2Offset = SectorOffset(_geometry.DirectoryTrack, 2);
            diskImage[bam2Offset + 0] = 0;
            diskImage[bam2Offset + 1] = 0xFF;
            diskImage[bam2Offset + 2] = (byte)'D';
            diskImage[bam2Offset + 3] = 0;
            diskImage[bam2Offset + 4] = diskId[0];
            diskImage[bam2Offset + 5] = diskId[1];
            diskImage[bam2Offset + 6] = (byte)'D';
            diskImage[bam2Offset + 7] = 0;
        }
    }

    // Maps a data track to where its BAM entry (1 free-count byte + a bitmap) lives: for D64,
    // always the single combined header/BAM sector; for D81, one of two BAM sectors depending on
    // which half (1-40 or 41-80) the track falls in.
    private (int BamTrack, int BamSector, int ByteOffset) LocateBamEntry(int track)
    {
        int metaTrack = _geometry.DirectoryTrack;
        if (_geometry.Format == DiskFormat.D64)
            return (metaTrack, 0, 4 + (track - 1) * 4);

        return track <= 40
            ? (metaTrack, 1, 16 + (track - 1) * 6)
            : (metaTrack, 2, 16 + (track - 41) * 6);
    }

    private bool IsSectorFree(byte[] diskImage, int track, int sector)
    {
        var (bamTrack, bamSector, byteOffset) = LocateBamEntry(track);
        int bitmapStart = SectorOffset(bamTrack, bamSector) + byteOffset + 1;
        int byteIndex = sector / 8;
        int bitIndex = sector % 8;
        return (diskImage[bitmapStart + byteIndex] & (1 << bitIndex)) != 0;
    }

    private void SetSectorFree(byte[] diskImage, int track, int sector, bool free)
    {
        var (bamTrack, bamSector, byteOffset) = LocateBamEntry(track);
        int sectorOffset = SectorOffset(bamTrack, bamSector);
        int bitmapStart = sectorOffset + byteOffset + 1;
        int byteIndex = sector / 8;
        int bitIndex = sector % 8;

        bool isCurrentlyFree = (diskImage[bitmapStart + byteIndex] & (1 << bitIndex)) != 0;
        if (isCurrentlyFree == free) return;

        if (free)
            diskImage[bitmapStart + byteIndex] |= (byte)(1 << bitIndex);
        else
            diskImage[bitmapStart + byteIndex] &= (byte)~(1 << bitIndex);

        int freeCountOffset = sectorOffset + byteOffset;
        diskImage[freeCountOffset] = (byte)(diskImage[freeCountOffset] + (free ? 1 : -1));
    }

    // Encodes a name into a fixed-width PETSCII field: uppercased, truncated/padded to `length`
    // with 0xA0, the inverse of DecodeName's byte mapping.
    private static byte[] EncodeName(string name, int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, (byte)0xA0);

        string upper = name.ToUpperInvariant();
        int count = Math.Min(upper.Length, length);
        for (int i = 0; i < count; i++)
        {
            char c = upper[i];
            bytes[i] = c is >= (char)0x20 and <= (char)0x5F ? (byte)c : (byte)'?';
        }

        return bytes;
    }

    #endregion
}
