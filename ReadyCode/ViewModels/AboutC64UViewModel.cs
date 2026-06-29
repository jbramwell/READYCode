// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using ReadyCode.C64U;

namespace ReadyCode.ViewModels;

/// <summary>
/// View model for the "About my C64" dialog, exposing the device information returned by the
/// C64 Ultimate's REST API along with visibility flags for its optional fields.
/// </summary>
public class AboutC64UViewModel
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutC64UViewModel"/> class from the device
    /// information returned by the C64 Ultimate.
    /// </summary>
    /// <param name="info">The device information to display.</param>
    public AboutC64UViewModel(C64UInfo info)
    {
        Product         = info.Product;
        FirmwareVersion = info.FirmwareVersion;
        FpgaVersion     = info.FpgaVersion;
        CoreVersion     = info.CoreVersion;
        Hostname        = info.Hostname;
        UniqueId        = info.UniqueId;
        ErrorText       = info.Errors is { Count: > 0 } ? string.Join(", ", info.Errors) : null;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the product name reported by the device.
    /// </summary>
    public string? Product         { get; }

    /// <summary>
    /// Gets the installed firmware version.
    /// </summary>
    public string? FirmwareVersion { get; }

    /// <summary>
    /// Gets the installed FPGA version.
    /// </summary>
    public string? FpgaVersion     { get; }

    /// <summary>
    /// Gets the running core version, or null if not reported.
    /// </summary>
    public string? CoreVersion     { get; }

    /// <summary>
    /// Gets the device's network hostname.
    /// </summary>
    public string? Hostname        { get; }

    /// <summary>
    /// Gets the device's unique identifier.
    /// </summary>
    public string? UniqueId        { get; }

    /// <summary>
    /// Gets the combined error text reported by the device, or null if there were no errors.
    /// </summary>
    public string? ErrorText       { get; }

    /// <summary>
    /// Gets whether the core version row should be shown.
    /// </summary>
    public Visibility CoreVersionVisibility => string.IsNullOrEmpty(CoreVersion) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Gets whether the unique ID row should be shown.
    /// </summary>
    public Visibility UniqueIdVisibility    => string.IsNullOrEmpty(UniqueId)    ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Gets whether the error row should be shown.
    /// </summary>
    public Visibility ErrorVisibility       => string.IsNullOrEmpty(ErrorText)   ? Visibility.Collapsed : Visibility.Visible;

    #endregion
}
