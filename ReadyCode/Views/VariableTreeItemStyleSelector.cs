// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Controls;
using ReadyCode.Models;

namespace ReadyCode.Views;

/// <summary>
/// Picks the Variable Explorer tree's <see cref="TreeViewItem"/> style by the bound data's type -
/// <see cref="VariableInfo"/> rows and <see cref="VariableOccurrenceInfo"/> rows need different
/// context menus (Rename vs. Go to Line), so they can't share one style with a single trigger the
/// way file/folder rows do (there's no shared bool flag to switch on).
/// </summary>
public class VariableTreeItemStyleSelector : StyleSelector
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the style applied to a <see cref="VariableInfo"/> row.
    /// </summary>
    public Style? VariableStyle { get; set; }

    /// <summary>
    /// Gets or sets the style applied to a <see cref="VariableOccurrenceInfo"/> row.
    /// </summary>
    public Style? OccurrenceStyle { get; set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns <see cref="VariableStyle"/> or <see cref="OccurrenceStyle"/> depending on the
    /// bound item's type.
    /// </summary>
    /// <param name="item">The bound data item.</param>
    /// <param name="container">The container the style will be applied to.</param>
    public override Style? SelectStyle(object item, DependencyObject container) => item switch
    {
        VariableInfo => VariableStyle,
        VariableOccurrenceInfo => OccurrenceStyle,
        _ => base.SelectStyle(item, container),
    };

    #endregion
}
