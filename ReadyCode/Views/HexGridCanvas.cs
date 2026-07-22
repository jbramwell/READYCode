// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ReadyCode.Models;

namespace ReadyCode.Views;

/// <summary>
/// Renders and drives interaction for a byte array shown as a hex grid (offset / hex bytes /
/// ASCII columns) entirely via <see cref="OnRender"/> and manual hit-testing, rather than one
/// WPF element per byte. Realizing hundreds of per-cell elements was confirmed, via a live
/// debugger break, to be dominated by WPF's UI Automation ancestor-invalidation walk, which
/// every <c>UIElement.Measure()</c> call pays regardless of element type (a plain
/// <c>TextBlock</c> pays it exactly like a <c>TextBox</c> does) - a single custom-drawn surface
/// pays that cost once, not once per byte, since <see cref="DrawingContext"/> drawing calls
/// aren't <c>UIElement</c>s and never reach <c>Measure()</c> at all.
/// Hosted inside <see cref="HexEditorControl"/>'s <c>ScrollViewer</c>, which owns the shared
/// edit-mode <c>Popup</c>/<c>TextBox</c> overlay this control positions via
/// <see cref="EditRequested"/>/<see cref="GetCellBounds"/> rather than owning directly.
/// </summary>
public sealed class HexGridCanvas : FrameworkElement
{
    #region Private Fields

    private const int BytesPerRow = 16;
    private const double RowHeight = 22;
    private const double FontSize = 13;

    // X positions mirror the removed per-row StackPanel's exact margins (Margin="6,1" on the
    // row, "6,2" on each divider, "8,0"/Width="384" on the Cells ItemsControl with Width="22"
    // Margin="1,0" cells, "8,0,0,0"/Width="10" on the AsciiChars ItemsControl and its chars) so
    // the rendered result lines up exactly like the old per-element layout did.
    private const double OffsetLabelX = 6;
    private const double OffsetLabelWidth = 64;
    private const double Divider1X = OffsetLabelX + OffsetLabelWidth + 6;
    private const double CellsAreaX = Divider1X + 1 + 6 + 8;
    private const double CellWidth = 24; // 22 content + 1,0 margin each side
    private const double CellContentWidth = 22;
    private const double CellsAreaWidth = BytesPerRow * CellWidth;
    private const double Divider2X = CellsAreaX + CellsAreaWidth + 8 + 6;
    private const double AsciiAreaX = Divider2X + 1 + 8;
    private const double AsciiCharWidth = 10;

    private byte[]? _bytes;
    private int _byteCount;
    private HexUndoStack? _undoStack;

    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private int? _selectionAnchor;
    private int _selectedOffset;
    private int? _editingOffset;

    private bool _isDragSelecting;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="HexGridCanvas"/> class.
    /// </summary>
    public HexGridCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
    }

    #endregion

    #region Public Properties

    /// <summary>Gets the byte offset of the currently selected/active cell.</summary>
    public int SelectedOffset => _selectedOffset;

    /// <summary>Gets whether one or more bytes are currently selected.</summary>
    public bool HasSelection => _bytes != null && _selectionStart >= 0 && _selectionEnd >= _selectionStart;

    /// <summary>Gets whether there is an edit available to undo.</summary>
    public bool CanUndo => _undoStack?.CanUndo ?? false;

    /// <summary>Gets whether there is an edit available to redo.</summary>
    public bool CanRedo => _undoStack?.CanRedo ?? false;

    /// <summary>
    /// Gets or sets the <see cref="ScrollViewer"/> hosting this canvas - set once by
    /// <see cref="HexEditorControl"/> right after <c>InitializeComponent()</c>. Used to know
    /// which rows are actually visible (so <see cref="OnRender"/> only draws those) and to
    /// scroll a newly selected offset into view.
    /// </summary>
    public ScrollViewer? HostScrollViewer { get; set; }

    /// <summary>
    /// Occurs when a byte is changed by the user (not when <see cref="LoadBytes"/> reloads the
    /// grid for a newly activated tab).
    /// </summary>
    public event EventHandler? ByteEdited;

    /// <summary>
    /// Occurs when the user double-clicks or presses Enter on a cell, asking the host to show
    /// its shared edit overlay there - the event argument is the byte offset to edit.
    /// </summary>
    public event EventHandler<int>? EditRequested;

    /// <summary>
    /// Occurs immediately before switching the edit overlay to a different cell while one is
    /// already being edited (double-clicking cell B while cell A's edit is still pending) - only
    /// the host can read the shared edit box's current, uncommitted text, so it must commit cell
    /// A synchronously in response before this control moves <see cref="EditRequested"/> on to B.
    /// </summary>
    public event EventHandler? EditCommitRequested;

    #endregion

    #region Public Methods

    /// <summary>
    /// Populates the grid from the given bytes, resetting selection to the given offset. Edits
    /// write directly back into <paramref name="bytes"/> (the same array reference the caller
    /// holds - e.g. <c>EditorTab.RawBytes</c>), so no separate "commit" step is needed to read
    /// edited content back out.
    /// </summary>
    /// <param name="bytes">The bytes to display and edit.</param>
    /// <param name="selectedOffset">The byte offset to remember as selected.</param>
    /// <param name="undoStack">The tab's undo/redo history, swapped in alongside its bytes.</param>
    public void LoadBytes(byte[] bytes, int selectedOffset, HexUndoStack undoStack)
    {
        _bytes = bytes;
        _byteCount = bytes.Length;
        _undoStack = undoStack;
        _selectionStart = -1;
        _selectionEnd = -1;
        _selectionAnchor = null;
        _editingOffset = null;
        _selectedOffset = Math.Clamp(selectedOffset, 0, Math.Max(0, bytes.Length - 1));

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Gets the on-canvas bounds of the given byte's cell, for positioning the host's shared
    /// edit overlay - see <see cref="EditRequested"/>.
    /// </summary>
    /// <param name="offset">The byte offset.</param>
    public Rect GetCellBounds(int offset)
    {
        int row = offset / BytesPerRow;
        int col = offset % BytesPerRow;
        return new Rect(CellsAreaX + col * CellWidth, row * RowHeight, CellContentWidth, RowHeight);
    }

    /// <summary>
    /// Gets the given byte's current value as two hex digits, for pre-filling the host's shared
    /// edit box - see <see cref="EditRequested"/>.
    /// </summary>
    /// <param name="offset">The byte offset.</param>
    public string GetHexText(int offset) => _bytes![offset].ToString("X2");

    /// <summary>
    /// Parses the host's edit box text and writes it back (raising <see cref="ByteEdited"/>) if
    /// it's a complete, valid byte; otherwise the byte is left unchanged - covers losing focus
    /// mid-edit (e.g. only one digit typed) without silently committing a garbled value.
    /// </summary>
    /// <param name="typedText">The edit box's current text.</param>
    /// <param name="advanceDelta">
    /// Null to stay on the same byte (Enter) or +/-1 to move on afterward (Tab/Shift+Tab).
    /// </param>
    public void CommitEdit(string typedText, int? advanceDelta)
    {
        if (_editingOffset is not { } offset || _bytes == null) return;

        if (typedText.Length == 2 && byte.TryParse(typedText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
            SetByte(offset, value);

        _editingOffset = null;
        InvalidateVisual();

        if (advanceDelta is { } delta)
        {
            int newOffset = offset + delta;
            if (newOffset >= 0 && newOffset < _byteCount)
                MoveSelectionTo(newOffset, extend: false);
        }
    }

    /// <summary>Exits edit mode without writing anything back.</summary>
    public void CancelEdit()
    {
        if (_editingOffset == null) return;
        _editingOffset = null;
        InvalidateVisual();
    }

    /// <summary>Copies the selected bytes to the clipboard as space-separated hex text.</summary>
    public void Copy()
    {
        if (!HasSelection) return;

        var sb = new StringBuilder();
        for (int offset = _selectionStart; offset <= _selectionEnd; offset++)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(_bytes![offset].ToString("X2"));
        }
        Clipboard.SetText(sb.ToString());
    }

    /// <summary>Copies the selected bytes, then zero-fills them - the fixed-size buffer can't shrink, so this is the closest analog to a text editor's Cut.</summary>
    public void Cut()
    {
        if (!HasSelection) return;
        Copy();
        ZeroSelection();
    }

    /// <summary>Zero-fills the selected bytes - the fixed-size buffer can't shrink, so this is the closest analog to a text editor's Delete.</summary>
    public void Delete()
    {
        if (!HasSelection) return;
        ZeroSelection();
    }

    /// <summary>
    /// Overwrites bytes starting at the current selection (or the active cell, if nothing is
    /// selected) with hex digits parsed from the clipboard, clamped to whatever room remains in
    /// the fixed-size buffer - never inserts or grows it.
    /// </summary>
    public void Paste()
    {
        if (_bytes == null || !Clipboard.ContainsText()) return;

        string hexDigits = new(Clipboard.GetText().Where(Uri.IsHexDigit).ToArray());
        if (hexDigits.Length < 2) return;

        int startOffset = HasSelection ? _selectionStart : SelectedOffset;
        int available = _byteCount - startOffset;
        if (available <= 0) return;

        int byteCount = Math.Min(hexDigits.Length / 2, available);
        var oldValues = new byte[byteCount];
        Array.Copy(_bytes, startOffset, oldValues, 0, byteCount);
        var newValues = new byte[byteCount];

        bool changed = false;
        for (int i = 0; i < byteCount; i++)
        {
            byte value = byte.Parse(hexDigits.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            newValues[i] = value;
            int offset = startOffset + i;
            if (_bytes[offset] == value) continue;
            _bytes[offset] = value;
            changed = true;
        }

        int endOffset = startOffset + byteCount - 1;
        _selectionAnchor = startOffset;
        SetSelectionRange(startOffset, endOffset);
        SetSelectedOffset(endOffset);
        ScrollIntoViewIfNeeded(endOffset);

        if (changed)
        {
            _undoStack?.Push(startOffset, oldValues, newValues);
            ByteEdited?.Invoke(this, EventArgs.Empty);
        }
        InvalidateVisual();
    }

    /// <summary>Reverts the most recent edit, if any.</summary>
    public void Undo()
    {
        if (_bytes == null) return;
        var affected = _undoStack?.Undo(_bytes);
        if (affected is not { } range) return;

        SelectAndReveal(range.Offset, range.Length);
        InvalidateVisual();
        ByteEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reapplies the most recently undone edit, if any.</summary>
    public void Redo()
    {
        if (_bytes == null) return;
        var affected = _undoStack?.Redo(_bytes);
        if (affected is not { } range) return;

        SelectAndReveal(range.Offset, range.Length);
        InvalidateVisual();
        ByteEdited?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? CellsAreaX + CellsAreaWidth + 200 : availableSize.Width;
        double totalRows = _byteCount == 0 ? 1 : Math.Ceiling(_byteCount / (double)BytesPerRow);
        return new Size(width, totalRows * RowHeight);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        if (_bytes == null || _byteCount == 0) return;

        double viewportTop = HostScrollViewer?.VerticalOffset ?? 0;
        double viewportHeight = HostScrollViewer?.ViewportHeight ?? ActualHeight;

        int totalRows = (int)Math.Ceiling(_byteCount / (double)BytesPerRow);
        int firstRow = Math.Max(0, (int)(viewportTop / RowHeight) - 1);
        int lastRow = Math.Min(totalRows - 1, (int)((viewportTop + viewportHeight) / RowHeight) + 1);
        if (firstRow > lastRow) return;

        // A transparent fill over the visible range so mouse hit-testing (based on what was
        // actually drawn, not just this element's bounds) works everywhere in the grid, not just
        // exactly on top of glyphs.
        dc.DrawRectangle(Brushes.Transparent, null,
            new Rect(0, firstRow * RowHeight, Math.Max(ActualWidth, CellsAreaX + CellsAreaWidth), (lastRow - firstRow + 1) * RowHeight));

        var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        Brush descBrush = ResolveBrush("ThemeSettingsDescFg");
        // ThemeEditorFg, not ThemeFileFg - this text is drawn directly on ThemeEditorBg, the
        // same as the BASIC/ASM editor's own text, and ThemeFileFg is only guaranteed to
        // contrast against the Explorer panel's lighter background (the two happen to be
        // identical colors in the C64 theme, which made unselected bytes invisible here).
        Brush byteBrush = ResolveBrush("ThemeEditorFg");
        Brush selectedByteBrush = ResolveBrush("ThemeHexSelectedFg");
        Brush accentBrush = ResolveBrush("ThemeSettingsAccent");
        Brush dividerBrush = ResolveBrush("ThemeSettingsDivider");
        var activePen = new Pen(accentBrush, 1);

        for (int row = firstRow; row <= lastRow; row++)
            DrawRow(dc, row, typeface, dpi, descBrush, byteBrush, selectedByteBrush, accentBrush, dividerBrush, activePen);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        int? offset = HitTestOffset(e.GetPosition(this));
        if (offset == null) return;

        if (e.ClickCount >= 2)
        {
            BeginEdit(offset.Value);
            e.Handled = true;
            return;
        }

        bool extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _selectionAnchor.HasValue;
        if (!extend) _selectionAnchor = offset;
        SetSelectionRange(_selectionAnchor!.Value, offset.Value);
        SetSelectedOffset(offset.Value);

        _isDragSelecting = true;
        CaptureMouse();

        // Explicit rather than relying on default click-to-focus behavior - CaptureMouse() above
        // competes with it for capture. Also what commits a pending edit on a different cell:
        // moving focus away from the shared edit box triggers its own LostKeyboardFocus handler
        // in HexEditorControl.
        Focus();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragSelecting || e.LeftButton != MouseButtonState.Pressed || !_selectionAnchor.HasValue) return;

        int? offset = HitTestOffset(e.GetPosition(this));
        if (offset == null) return;

        SetSelectionRange(_selectionAnchor.Value, offset.Value);
        SetSelectedOffset(offset.Value);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDragSelecting) ReleaseMouseCapture();
        _isDragSelecting = false;
    }

    /// <inheritdoc/>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        // Right-clicking inside the current selection leaves it alone, so Cut/Copy/Delete from
        // the context menu act on the whole selection - matching Editor_PreviewMouseRightButtonDown's
        // behavior for the text editor. Right-clicking outside it collapses to just that byte.
        int? offset = HitTestOffset(e.GetPosition(this));
        if (offset == null) return;
        if (HasSelection && offset >= _selectionStart && offset <= _selectionEnd) return;

        _selectionAnchor = offset;
        SetSelectionRange(offset.Value, offset.Value);
        SetSelectedOffset(offset.Value);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // While editing, the shared edit box (in its own Popup, a separate routed-event scope)
        // owns keyboard focus and handles its own Enter/Escape/Tab - this is browsing-mode only.
        if (_editingOffset != null || _bytes == null || _byteCount == 0) return;

        switch (e.Key)
        {
            case Key.A when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                e.Handled = true;
                _selectionAnchor = 0;
                SetSelectionRange(0, _byteCount - 1);
                break;

            case Key.Enter:
                e.Handled = true;
                BeginEdit(SelectedOffset);
                break;

            // Deliberately not handled - Tab/Shift+Tab are left alone so they bubble up to the
            // app's own tab-cycling behavior instead of moving the cell selection; arrow keys
            // are the only way to move between cells.
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                e.Handled = true;
                HandleArrowKey(e.Key, extend: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;

            case Key.Home:
            case Key.End:
            case Key.PageUp:
            case Key.PageDown:
            {
                e.Handled = true;
                bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                bool shiftHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                int target = e.Key switch
                {
                    Key.Home => ctrl ? 0 : RowStart(SelectedOffset),
                    Key.End => ctrl ? _byteCount - 1 : RowStart(SelectedOffset) + RowLength(SelectedOffset) - 1,
                    Key.PageUp => SelectedOffset - PageSizeInBytes(),
                    Key.PageDown => SelectedOffset + PageSizeInBytes(),
                    _ => SelectedOffset,
                };
                MoveSelectionTo(Math.Clamp(target, 0, _byteCount - 1), shiftHeld);
                break;
            }
        }
    }

    #endregion

    #region Private Methods

    private void DrawRow(DrawingContext dc, int row, Typeface typeface, double dpi, Brush descBrush, Brush byteBrush,
        Brush selectedByteBrush, Brush accentBrush, Brush dividerBrush, Pen activePen)
    {
        int rowStart = row * BytesPerRow;
        double y = row * RowHeight;

        DrawText(dc, rowStart.ToString("X6"), OffsetLabelX, y, descBrush, typeface, dpi);
        dc.DrawRectangle(dividerBrush, null, new Rect(Divider1X, y + 2, 1, RowHeight - 4));
        dc.DrawRectangle(dividerBrush, null, new Rect(Divider2X, y + 2, 1, RowHeight - 4));

        int rowLength = Math.Min(BytesPerRow, _byteCount - rowStart);
        for (int col = 0; col < rowLength; col++)
        {
            int offset = rowStart + col;
            if (offset == _editingOffset) continue; // covered by the host's edit-mode overlay

            bool selected = offset >= _selectionStart && offset <= _selectionEnd;
            double cellX = CellsAreaX + col * CellWidth;
            double asciiX = AsciiAreaX + col * AsciiCharWidth;

            if (selected)
            {
                dc.DrawRectangle(accentBrush, null, new Rect(cellX, y, CellContentWidth, RowHeight));
                dc.DrawRectangle(accentBrush, null, new Rect(asciiX, y, AsciiCharWidth, RowHeight));
            }
            if (offset == SelectedOffset)
                dc.DrawRectangle(null, activePen, new Rect(cellX + 0.5, y + 0.5, CellContentWidth - 1, RowHeight - 1));

            byte b = _bytes![offset];
            Brush fg = selected ? selectedByteBrush : byteBrush;
            DrawText(dc, b.ToString("X2"), cellX, y, fg, typeface, dpi, CellContentWidth);

            char c = b is >= 0x20 and <= 0x7E ? (char)b : '.';
            Brush asciiFg = selected ? selectedByteBrush : descBrush;
            DrawText(dc, c.ToString(), asciiX, y, asciiFg, typeface, dpi, AsciiCharWidth);
        }
    }

    // Horizontally centered within [x, x+width) if width > 0 (matching the old cells'
    // TextAlignment="Center"), otherwise left-aligned at x (matching the offset label, which
    // never had TextAlignment set); always vertically centered within the row.
    private static void DrawText(DrawingContext dc, string text, double x, double y, Brush brush, Typeface typeface, double dpi, double width = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize, brush, dpi);
        double drawX = width > 0 ? x + (width - formatted.Width) / 2 : x;
        double drawY = y + (RowHeight - formatted.Height) / 2;
        dc.DrawText(formatted, new Point(drawX, drawY));
    }

    private int? HitTestOffset(Point p)
    {
        if (_bytes == null) return null;
        if (p.X < CellsAreaX || p.X >= CellsAreaX + CellsAreaWidth) return null;

        int row = (int)(p.Y / RowHeight);
        int col = (int)((p.X - CellsAreaX) / CellWidth);
        if (row < 0 || col < 0 || col >= BytesPerRow) return null;

        int offset = row * BytesPerRow + col;
        return offset >= 0 && offset < _byteCount ? offset : null;
    }

    private void BeginEdit(int offset)
    {
        // A different cell is already being edited - only the host can read that cell's pending,
        // uncommitted text (from the shared edit box), so ask it to commit synchronously before
        // this cell takes over the overlay.
        if (_editingOffset is { } previous && previous != offset)
            EditCommitRequested?.Invoke(this, EventArgs.Empty);

        _selectionAnchor = offset;
        SetSelectionRange(offset, offset);
        SetSelectedOffset(offset);

        _editingOffset = offset;
        InvalidateVisual();
        EditRequested?.Invoke(this, offset);
    }

    private void ZeroSelection()
    {
        int length = _selectionEnd - _selectionStart + 1;
        var oldValues = new byte[length];
        Array.Copy(_bytes!, _selectionStart, oldValues, 0, length);

        bool changed = false;
        for (int offset = _selectionStart; offset <= _selectionEnd; offset++)
        {
            if (_bytes![offset] == 0) continue;
            _bytes[offset] = 0;
            changed = true;
        }

        InvalidateVisual();
        if (changed)
        {
            _undoStack?.Push(_selectionStart, oldValues, new byte[length]);
            ByteEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetByte(int offset, byte value)
    {
        if (_bytes![offset] == value) return;
        _undoStack?.Push(offset, new[] { _bytes[offset] }, new[] { value });
        _bytes[offset] = value;
        InvalidateVisual();
        ByteEdited?.Invoke(this, EventArgs.Empty);
    }

    private void SetSelectedOffset(int offset)
    {
        _selectedOffset = offset;
        InvalidateVisual();
    }

    private void SetSelectionRange(int offsetA, int offsetB)
    {
        int newStart = Math.Min(offsetA, offsetB);
        int newEnd = Math.Max(offsetA, offsetB);
        if (newStart == _selectionStart && newEnd == _selectionEnd) return;

        _selectionStart = newStart;
        _selectionEnd = newEnd;
        InvalidateVisual();
    }

    // Core of every keyboard/mouse-driven move: sets (or, with Shift, extends) the selection to
    // targetOffset and scrolls it into view. Bounds-checking/clamping targetOffset is the
    // caller's job - HandleArrowKey and the Home/End/Page Up/Down cases want different behavior
    // at the edges (no-op vs. clamp).
    private void MoveSelectionTo(int targetOffset, bool extend)
    {
        if (extend)
        {
            _selectionAnchor ??= SelectedOffset;
            SetSelectionRange(_selectionAnchor.Value, targetOffset);
        }
        else
        {
            _selectionAnchor = targetOffset;
            SetSelectionRange(targetOffset, targetOffset);
        }

        SetSelectedOffset(targetOffset);
        ScrollIntoViewIfNeeded(targetOffset);
    }

    // Selects the byte range an undo/redo just touched and scrolls it into view, mirroring what
    // a fresh edit to that range would have left selected.
    private void SelectAndReveal(int offset, int length)
    {
        int endOffset = offset + length - 1;
        _selectionAnchor = offset;
        SetSelectionRange(offset, endOffset);
        SetSelectedOffset(endOffset);
        ScrollIntoViewIfNeeded(offset);
    }

    // Moves (or, with Shift, extends the selection to) the cell one byte left/right or one row
    // up/down from SelectedOffset - bounds-checked rather than clamped, so e.g. Up from the top
    // row or Down into a short last row correctly does nothing instead of jumping to an
    // unrelated column.
    private void HandleArrowKey(Key key, bool extend)
    {
        int delta = key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -BytesPerRow,
            Key.Down => BytesPerRow,
            _ => 0,
        };

        int newOffset = SelectedOffset + delta;
        if (newOffset < 0 || newOffset >= _byteCount) return;

        MoveSelectionTo(newOffset, extend);
    }

    // Scrolls just enough to bring the given offset's row into view, if it isn't already. Unlike
    // the removed ItemsControl-based design (VirtualizingPanel.ScrollUnit="Item" made
    // VerticalOffset a row index), HostScrollViewer is a plain pixel-based ScrollViewer now, so
    // all the arithmetic here is in pixels.
    private void ScrollIntoViewIfNeeded(int offset)
    {
        if (HostScrollViewer == null) return;

        int row = offset / BytesPerRow;
        double topRow = HostScrollViewer.VerticalOffset / RowHeight;
        double visibleRows = Math.Max(1, HostScrollViewer.ViewportHeight / RowHeight);

        if (row < topRow)
            HostScrollViewer.ScrollToVerticalOffset(row * RowHeight);
        else if (row > topRow + visibleRows - 1)
            HostScrollViewer.ScrollToVerticalOffset((row - visibleRows + 1) * RowHeight);
    }

    private int RowStart(int offset) => offset - (offset % BytesPerRow);

    private int RowLength(int offset)
    {
        int rowStart = RowStart(offset);
        return Math.Min(BytesPerRow, _byteCount - rowStart);
    }

    private int PageSizeInBytes()
    {
        double viewportHeight = HostScrollViewer?.ViewportHeight ?? ActualHeight;
        return Math.Max(1, (int)(viewportHeight / RowHeight)) * BytesPerRow;
    }

    private Brush ResolveBrush(string resourceKey) => (TryFindResource(resourceKey) as Brush) ?? Brushes.Gray;

    #endregion
}
