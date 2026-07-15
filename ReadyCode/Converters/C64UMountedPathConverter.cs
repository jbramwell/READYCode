// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Windows.Data;

namespace ReadyCode.Converters;

/// <summary>
/// Returns whether a C64U file's full path (the first bound value) matches either of Drive
/// A/B's currently mounted image paths (the second and third bound values), used to highlight
/// the currently mounted disk image in the C64U explorer tree.
/// </summary>
public class C64UMountedPathConverter : IMultiValueConverter
{
    #region Public Methods

    /// <inheritdoc/>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not string path || string.IsNullOrEmpty(path))
            return false;

        return string.Equals(path, values[1] as string, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, values[2] as string, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    #endregion
}
