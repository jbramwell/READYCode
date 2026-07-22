// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ReadyCode.Models;

namespace ReadyCode.Views;

/// <summary>
/// Hosts <see cref="HexGridCanvas"/> (the custom-drawn hex grid) alongside the fixed column-index
/// header and the shared edit-mode <c>Popup</c>/<c>TextBox</c> overlay - a thin wrapper that owns
/// the chrome around the grid and exposes the same public surface the grid previously exposed
/// directly, so <c>MainWindow.xaml.cs</c> needs no changes across the rendering-mechanism swap.
/// Mirrors <see cref="FindBarControl"/>'s shape as a UserControl that owns its own UI and reports
/// back via events rather than being MVVM-bound to the host's view model.
/// </summary>
public partial class HexEditorControl : UserControl
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="HexEditorControl"/> class.
    /// </summary>
    public HexEditorControl()
    {
        InitializeComponent();

        HexGrid.HostScrollViewer = RowsScrollViewer;
        HexGrid.ByteEdited += (_, _) => ByteEdited?.Invoke(this, EventArgs.Empty);
        HexGrid.EditRequested += HexGrid_EditRequested;
        HexGrid.EditCommitRequested += (_, _) => HexGrid.CommitEdit(EditBox.Text, advanceDelta: null);

        // HexGrid's OnRender only draws whatever row range RowsScrollViewer.VerticalOffset says
        // is visible right now - nothing else tells it to redraw when that offset changes on its
        // own, whether from the mouse wheel (handled entirely by ScrollViewer natively) or from
        // ScrollToVerticalOffset (called by HexGridCanvas.ScrollIntoViewIfNeeded) - the latter is
        // also what avoids a race against InvalidateVisual() calls made before the scroll offset
        // has actually settled (e.g. from a selection change that also scrolls, like Ctrl+End).
        RowsScrollViewer.ScrollChanged += (_, _) => HexGrid.InvalidateVisual();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the byte offset of the most recently selected cell, for saving/restoring position
    /// when switching away from and back to this tab - the hex-mode analog of the text
    /// editor's caret offset.
    /// </summary>
    public int SelectedOffset => HexGrid.SelectedOffset;

    /// <summary>
    /// Gets the current vertical scroll offset, for saving/restoring position across tab
    /// switches - the hex-mode analog of the text editor's scroll offset.
    /// </summary>
    public double VerticalScrollOffset => RowsScrollViewer.VerticalOffset;

    /// <summary>Gets whether one or more bytes are currently selected.</summary>
    public bool HasSelection => HexGrid.HasSelection;

    /// <summary>Gets whether there is an edit available to undo.</summary>
    public bool CanUndo => HexGrid.CanUndo;

    /// <summary>Gets whether there is an edit available to redo.</summary>
    public bool CanRedo => HexGrid.CanRedo;

    /// <summary>
    /// Occurs when a byte is changed by the user (not when <see cref="LoadBytes"/> repopulates
    /// the grid for a newly activated tab).
    /// </summary>
    public event EventHandler? ByteEdited;

    #endregion

    #region Public Methods

    /// <summary>Copies the selected bytes to the clipboard as space-separated hex text.</summary>
    public void Copy() => HexGrid.Copy();

    /// <summary>Copies the selected bytes, then zero-fills them.</summary>
    public void Cut() => HexGrid.Cut();

    /// <summary>Overwrites bytes from the current selection (or active cell) with hex digits parsed from the clipboard.</summary>
    public void Paste() => HexGrid.Paste();

    /// <summary>Zero-fills the selected bytes.</summary>
    public void Delete() => HexGrid.Delete();

    /// <summary>Reverts the most recent edit, if any.</summary>
    public void Undo() => HexGrid.Undo();

    /// <summary>Reapplies the most recently undone edit, if any.</summary>
    public void Redo() => HexGrid.Redo();

    /// <summary>
    /// Populates the grid from the given bytes, restoring the given selected offset and scroll
    /// position. Edits made through the grid write directly back into <paramref name="bytes"/>
    /// (the same array reference the caller holds - e.g. <c>EditorTab.RawBytes</c>), so no
    /// separate "commit" step is needed to read the edited content back out.
    /// </summary>
    /// <param name="bytes">The bytes to display and edit.</param>
    /// <param name="selectedOffset">The byte offset to remember as selected.</param>
    /// <param name="scrollOffsetY">The vertical scroll position to restore.</param>
    /// <param name="undoStack">The tab's undo/redo history, swapped in alongside its bytes.</param>
    public void LoadBytes(byte[] bytes, int selectedOffset, double scrollOffsetY, HexUndoStack undoStack)
    {
        EditPopup.IsOpen = false;
        HexGrid.LoadBytes(bytes, selectedOffset, undoStack);

        // Deferred until layout has run for the freshly resized HexGrid, or ScrollViewer's
        // extent isn't known yet and the offset would just get clamped to 0. Also where focus is
        // (re-)requested, rather than relying solely on MainWindow's own HexEditor.Focus() call
        // right after this method returns - that one can silently fail to land since it happens
        // in the same synchronous pass as HexEditor.Visibility flipping to Visible, before
        // HexGrid's layout has actually caught up to being focusable.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            RowsScrollViewer.ScrollToVerticalOffset(scrollOffsetY);
            HexGrid.Focus();
        });
    }

    #endregion

    #region Private Methods

    private void HexGrid_EditRequested(object? sender, int offset)
    {
        Rect bounds = HexGrid.GetCellBounds(offset);

        // Positioned relative to the viewport (RowsScrollViewer), not HexGrid's own (mostly
        // off-screen, for a scrolled-down file) coordinate space - VerticalOffset is in pixels
        // (RowsScrollViewer is CanContentScroll="False"), so subtracting it converts bounds.Y
        // from "distance from the top of the whole file" to "distance from the top of what's
        // currently visible". Horizontal scrolling stays disabled, so bounds.X needs no
        // equivalent adjustment.
        EditPopup.PlacementTarget = RowsScrollViewer;
        EditPopup.HorizontalOffset = bounds.X;
        EditPopup.VerticalOffset = bounds.Y - RowsScrollViewer.VerticalOffset;

        EditBox.Text = HexGrid.GetHexText(offset);
        EditPopup.IsOpen = true;

        // Deferred in case the Popup's content isn't fully realized synchronously within the
        // same call that opened it.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            EditBox.Focus();
            EditBox.SelectAll();
        });
    }

    private void EditBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !Regex.IsMatch(e.Text, "^[0-9A-Fa-f]+$");

    private void EditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                HexGrid.CommitEdit(EditBox.Text, advanceDelta: null);
                EditPopup.IsOpen = false;
                HexGrid.Focus();
                break;

            case Key.Escape:
                e.Handled = true;
                HexGrid.CancelEdit();
                EditPopup.IsOpen = false;
                HexGrid.Focus();
                break;

            // Deliberately not handled - Tab/Shift+Tab are left alone so they bubble up to the
            // app's own tab-cycling behavior. If that (or anything else) moves focus away from
            // EditBox as a result, EditBox_LostKeyboardFocus below still commits the edit.
        }
    }

    // Commits automatically when focus leaves EditBox for any reason not already covered by an
    // explicit Enter/Escape keypress - clicking a different cell, clicking outside the grid
    // entirely, switching tabs, etc.
    private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!EditPopup.IsOpen) return; // already closed via an explicit key above - avoid double-committing
        HexGrid.CommitEdit(EditBox.Text, advanceDelta: null);
        EditPopup.IsOpen = false;
    }

    #endregion
}
