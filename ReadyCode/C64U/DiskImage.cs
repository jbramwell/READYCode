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
        if (diskImage.Length != _geometry.StandardImageSize)
            throw new InvalidOperationException(
                $"Not a standard disk image ({diskImage.Length} bytes; expected {_geometry.StandardImageSize}).");

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

    #endregion
}
