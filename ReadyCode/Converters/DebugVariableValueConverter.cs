// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Windows.Data;
using ReadyCode.Debugger;

namespace ReadyCode.Converters;

/// <summary>
/// Formats a <see cref="BasicVariable"/> row's value for display in the Variables panel: a float
/// to 9 significant digits (matching C64 BASIC's own display precision), a string in quotes, and
/// an integer as a plain signed decimal.
/// </summary>
public sealed class DebugVariableValueConverter : IValueConverter
{
    #region Public Methods

    /// <summary>
    /// Converts a <see cref="BasicVariable"/> to its display string.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BasicVariable variable) return string.Empty;

        return variable.Type switch
        {
            BasicVariableType.Float when variable.Value is double d => d.ToString("G9", CultureInfo.InvariantCulture),
            BasicVariableType.String when variable.Value is ResolvedStringValue s => $"\"{s.Text}\"",
            BasicVariableType.String => string.Empty, // not yet resolved
            _ => variable.Value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Not supported - the Variables panel's Value column is read-only in this release.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    #endregion
}
