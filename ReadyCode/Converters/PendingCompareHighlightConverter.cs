// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Windows.Data;
using ReadyCode.Models;
using ReadyCode.ViewModels;

namespace ReadyCode.Converters;

/// <summary>
/// Returns whether a tree item (the first bound value, a <see cref="FileTreeItem"/> or
/// <see cref="C64UFileItem"/>) is the file currently pending as the "left" side of a File
/// Compare (<see cref="MainViewModel.PendingCompareFile"/>, the second bound value) - used to
/// highlight it in the Folder Explorer/C64U Explorer tree, the same way
/// <see cref="C64UMountedPathConverter"/> highlights a mounted disk image.
/// </summary>
public class PendingCompareHighlightConverter : IMultiValueConverter
{
    #region Public Methods

    /// <inheritdoc/>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not ComparableFileRef pending)
            return false;

        var candidate = values[0] switch
        {
            FileTreeItem local => ComparableFileRef.FromLocal(local),
            C64UFileItem remote => ComparableFileRef.FromC64U(remote),
            _ => null,
        };

        return candidate != null && pending.IsSameFile(candidate);
    }

    /// <inheritdoc/>
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    #endregion
}
