// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.C64U;

/// <summary>
/// Status of a single drive reported by GET /v1/drives.
/// </summary>
public class C64UDriveStatus
{
    #region Public Properties

    /// <summary>
    /// Gets the drive identifier (e.g. "a", "b", "IEC Drive", "Printer Emulation").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets whether the drive is enabled on the device.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the emulated drive type (e.g. "1541").
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Gets the full path of the currently mounted disk image, or empty if nothing is mounted.
    /// </summary>
    public string ImageFile { get; init; } = "";

    #endregion
}
