// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Media;

namespace ReadyCode.Editor;

/// <summary>
/// The two hues every File Compare visual (<see cref="DiffLineColorizer"/>,
/// <see cref="DiffPrefixMargin"/>, <see cref="DiffChangeIndicatorStrip"/>) tints an insertion or
/// deletion with - kept in one place so retuning the palette (e.g. for accessibility/contrast)
/// only ever needs to happen here, and so the three visuals can't silently drift apart.
/// </summary>
public static class DiffColors
{
    #region Public Properties

    /// <summary>The hue used for inserted/added content.</summary>
    public static Color Inserted { get; } = Color.FromRgb(46, 160, 67);

    /// <summary>The hue used for deleted/removed content.</summary>
    public static Color Deleted { get; } = Color.FromRgb(220, 53, 69);

    #endregion
}
