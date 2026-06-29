// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace ReadyCode.C64U;

/// <summary>
/// Response payload for GET /v1/info.
/// </summary>
public class C64UInfo
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the product name reported by the device.
    /// </summary>
    [JsonPropertyName("product")]
    public string? Product { get; set; }

    /// <summary>
    /// Gets or sets the installed firmware version.
    /// </summary>
    [JsonPropertyName("firmware_version")]
    public string? FirmwareVersion { get; set; }

    /// <summary>
    /// Gets or sets the installed FPGA version.
    /// </summary>
    [JsonPropertyName("fpga_version")]
    public string? FpgaVersion { get; set; }

    /// <summary>
    /// Gets or sets the running core version.
    /// </summary>
    [JsonPropertyName("core_version")]
    public string? CoreVersion { get; set; }

    /// <summary>
    /// Gets or sets the device's network hostname.
    /// </summary>
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    /// <summary>
    /// Gets or sets the device's unique identifier.
    /// </summary>
    [JsonPropertyName("unique_id")]
    public string? UniqueId { get; set; }

    /// <summary>
    /// Gets or sets any error messages reported by the device.
    /// </summary>
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }

    #endregion
}
