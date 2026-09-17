// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Vice;

/// <summary>
/// Version information reported by VICE's binary monitor.
/// </summary>
public class ViceInfo
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the VICE version (e.g. "3.5.0.0").
    /// </summary>
    public string Version { get; set; } = "";

    #endregion
}
