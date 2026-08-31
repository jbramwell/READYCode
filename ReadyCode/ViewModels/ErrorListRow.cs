// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Models;

namespace ReadyCode.ViewModels;

/// <summary>
/// A single row in the Errors panel's <c>DataGrid</c>: one <see cref="Diagnostics.EditorDiagnostic"/>
/// resolved to a display line number and its owning tab, for cross-tab display and
/// double-click-to-navigate. Rebuilt by <c>MainWindow.RefreshErrorsPanel</c> from every open
/// tab's <see cref="EditorTab.Diagnostics"/> whenever any tab's diagnostics change.
/// </summary>
public sealed class ErrorListRow
{
    #region Public Properties

    /// <summary>
    /// Gets the severity label. Always "Error" for now - <see cref="Diagnostics.EditorDiagnostic"/>
    /// carries no severity field yet, and every current check is a hard failure, not a "soft" one.
    /// </summary>
    public string Severity { get; init; } = "Error";

    /// <summary>
    /// Gets the diagnostic's human-readable message.
    /// </summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// Gets the owning tab's display file name, for the File column.
    /// </summary>
    public string FileName { get; init; } = "";

    /// <summary>
    /// Gets the owning tab's full file path, or null for a tab with no backing file.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Gets the 1-based document line number the diagnostic falls on, for the Line column.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// Gets the BASIC program's own line number label (e.g. 4510) for the diagnostic's line, or
    /// null for an assembly diagnostic (which has no BASIC line numbering at all) - distinct from
    /// <see cref="Line"/>, which is the physical row position in the document, not the number the
    /// user's GOTO/GOSUB targets actually reference.
    /// </summary>
    public int? BasicLineNumber { get; init; }

    /// <summary>
    /// Gets the tab this row belongs to, so a double-click can activate it even if it isn't the
    /// currently active tab.
    /// </summary>
    public required EditorTab Tab { get; init; }

    /// <summary>
    /// Gets the diagnostic's original document offset, for placing the caret precisely on
    /// double-click (rather than just the start of <see cref="Line"/>).
    /// </summary>
    public int Offset { get; init; }

    #endregion
}
