// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Windows.Data;
using ReadyCode.Debugger;
using ReadyCode.Tokenizer;

namespace ReadyCode.Converters;

/// <summary>
/// Formats a <see cref="BasicVariable"/> row's value for display in the Variables panel: a float
/// to 9 significant digits (matching C64 BASIC's own display precision), a string in quotes (with
/// any embedded PETSCII control-code bytes shown as their real C64 glyphs via
/// <see cref="PetsciiScreenCodeMap.ToDisplayText"/>, same as the code editor - the "Pet Me 64"
/// font applied to the Value column in MainWindow.xaml is what actually renders them), and an
/// integer as a plain signed decimal.
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
            BasicVariableType.String when variable.Value is ResolvedStringValue s => $"\"{PetsciiScreenCodeMap.ToDisplayText(s.Text)}\"",
            BasicVariableType.String => string.Empty, // not yet resolved
            _ => variable.Value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Not supported - the Variables panel's Value column edits are applied by
    /// MainWindow.xaml.cs's DebugVariablesGrid_CellEditEnding (reading the editing TextBox's raw
    /// text and encoding it via <see cref="ReadyCode.Debugger.VariableWriteBack"/>), not by any
    /// two-way binding through this converter.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    #endregion
}
