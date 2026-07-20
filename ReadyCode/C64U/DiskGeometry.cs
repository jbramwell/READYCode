// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Models;

namespace ReadyCode.C64U;

/// <summary>
/// The track/sector layout of a CBM DOS disk image format, letting <see cref="DiskImage"/> read
/// (and, eventually, write) both .d64 and .d81 images with one shared implementation.
/// </summary>
/// <param name="SectorsPerTrack">
/// Sectors per track, 1-indexed (index 0 unused, matching the on-disk track numbering).
/// </param>
/// <param name="DirectoryTrack">The track the directory sector chain starts on.</param>
/// <param name="DirectorySector">The sector the directory chain starts at (after the BAM).</param>
/// <param name="StandardImageSize">The exact expected size, in bytes, of a well-formed image.</param>
/// <param name="MaxChainSteps">
/// A generous upper bound on sector-chain length, comfortably above the sector count of the
/// whole disk, used to guard against an infinite loop from a corrupt/malicious chain.
/// </param>
public sealed record DiskGeometry(
    int[] SectorsPerTrack,
    int DirectoryTrack,
    int DirectorySector,
    int StandardImageSize,
    int MaxChainSteps)
{
    /// <summary>
    /// The geometry of a standard 35-track 1541 disk image (.d64): 21 sectors/track for tracks
    /// 1-17, 19 for 18-24, 18 for 25-30, 17 for 31-35. Directory starts at track 18, sector 1
    /// (sector 0 is the BAM).
    /// </summary>
    public static readonly DiskGeometry D64 = new(
        SectorsPerTrack:
        [
            0, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21,
               19, 19, 19, 19, 19, 19, 19,
               18, 18, 18, 18, 18, 18,
               17, 17, 17, 17, 17,
        ],
        DirectoryTrack: 18,
        DirectorySector: 1,
        StandardImageSize: 174_848,
        MaxChainSteps: 700);

    /// <summary>
    /// The geometry of a standard 1581 disk image (.d81): 80 tracks, a uniform 40 sectors/track,
    /// double-sided. Directory starts at track 40, sector 3 (track 40 sectors 0-2 hold the disk
    /// header and BAM).
    /// </summary>
    public static readonly DiskGeometry D81 = new(
        SectorsPerTrack: BuildUniform(tracks: 80, sectorsPerTrack: 40),
        DirectoryTrack: 40,
        DirectorySector: 3,
        StandardImageSize: 819_200,
        MaxChainSteps: 3300);

    /// <summary>
    /// Gets the geometry for the given file kind.
    /// </summary>
    /// <param name="kind">A disk image kind (<see cref="C64UFileKind.D64"/> or <see cref="C64UFileKind.D81"/>).</param>
    /// <exception cref="InvalidOperationException"><paramref name="kind"/> is not a disk image kind.</exception>
    public static DiskGeometry ForKind(C64UFileKind kind) => kind switch
    {
        C64UFileKind.D64 => D64,
        C64UFileKind.D81 => D81,
        _ => throw new InvalidOperationException($"{kind} is not a disk image kind."),
    };

    private static int[] BuildUniform(int tracks, int sectorsPerTrack)
    {
        var table = new int[tracks + 1];
        for (int t = 1; t <= tracks; t++)
            table[t] = sectorsPerTrack;
        return table;
    }
}
