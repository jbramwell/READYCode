// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Settings;

namespace ReadyCode.ViewModels;

/// <summary>
/// Wraps the currently open folder ("project") root path, backed by
/// <see cref="AppSettings.LastFolderPath"/>. Owned by <see cref="MainViewModel"/>; gives callers
/// one place to check or change which folder is open instead of reaching into
/// <see cref="AppSettings.LastFolderPath"/> directly at each call site. Persistence is unchanged -
/// callers still call <see cref="AppSettings.Save"/> themselves after setting <see cref="RootPath"/>.
/// </summary>
public class ProjectContext
{
    #region Private Fields

    private readonly AppSettings _settings;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectContext"/> class over the given
    /// persisted settings.
    /// </summary>
    /// <param name="settings">The application settings backing <see cref="RootPath"/>.</param>
    public ProjectContext(AppSettings settings) => _settings = settings;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets whether a folder is currently open.
    /// </summary>
    public bool IsOpen => !string.IsNullOrEmpty(RootPath);

    /// <summary>
    /// Gets or sets the currently open folder's full path, or "" if no folder is open.
    /// </summary>
    public string RootPath
    {
        get => _settings.LastFolderPath;
        set => _settings.LastFolderPath = value;
    }

    #endregion
}
