// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Vice;

namespace ReadyCode.ViewModels;

/// <summary>
/// View model for the "About VICE" dialog, exposing the version information returned by
/// VICE's binary monitor.
/// </summary>
public class AboutViceViewModel
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViceViewModel"/> class from the
    /// version information returned by VICE.
    /// </summary>
    /// <param name="info">The version information to display.</param>
    /// <param name="emulatorPath">Full path to the VICE emulator executable.</param>
    public AboutViceViewModel(ViceInfo info, string emulatorPath)
    {
        Version = info.Version;
        EmulatorPath = emulatorPath;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the VICE version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the full path to the VICE emulator executable.
    /// </summary>
    public string EmulatorPath { get; }

    #endregion
}
