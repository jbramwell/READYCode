// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ReadyCode.Editor;

/// <summary>
/// A draggable "viewport" thumb overlaid across one or more <see cref="DiffChangeIndicatorStrip"/>
/// change-map columns, showing which portion of the document is currently visible and letting the
/// user scroll by dragging it - replacing an AvalonEdit pane's own vertical scrollbar entirely (see
/// <c>FileCompareControl</c>, which sets <c>VerticalScrollBarVisibility="Hidden"</c> on the panes
/// this drives). Deliberately a separate, wider element rather than each strip drawing its own
/// thumb, so a single grab spans - and looks like it spans - every column at once rather than a
/// series of thin, easy-to-miss individual thumbs. There are no up/down arrow buttons; the mouse
/// wheel over the editor panes is the only other way to scroll.
/// </summary>
public sealed class DiffViewportThumb : FrameworkElement
{
    #region Private Fields

    private const double MinThumbHeight = 6;
    private const double CornerRadius = 8;
    private const double BorderThickness = 1;

    // Tune these to adjust how see-through the thumb is - dragging is drawn a bit more opaque
    // than idle, as a visual "yes, you've grabbed it" cue.
    private const double NormalOpacity = 0.20;
    private const double DraggingOpacity = 0.3;

    private static readonly Brush _thumbBrush;
    private static readonly Brush _thumbBrushDragging;
    private static readonly Pen _thumbBorderPen;
    private static readonly Pen _thumbBorderPenDragging;

    // Where the mouse grabbed the thumb, as an offset from the thumb's own top edge, so a drag
    // moves the thumb by the same delta as the mouse rather than re-centering it under the cursor
    // on every move - the same feel as dragging a normal ScrollBar's Thumb.
    private double _dragGrabOffset;
    private bool _isDragging;

    // While dragging, this is the authoritative rendered top position, computed directly from the
    // mouse and used instead of ViewportStart - see the drag methods' remarks for why.
    private double _dragRenderTop;

    #endregion

    #region Constructors

    static DiffViewportThumb()
    {
        _thumbBrush = new SolidColorBrush(Color.FromArgb((byte)(NormalOpacity * 255), 128, 128, 128));
        _thumbBrush.Freeze();
        _thumbBrushDragging = new SolidColorBrush(Color.FromArgb((byte)(DraggingOpacity * 255), 128, 128, 128));
        _thumbBrushDragging.Freeze();

        // A darker shade of the same gray as the fill, at the same opacity as its respective fill
        // brush, so the border reads as "an outline on the thumb" rather than a separately-tuned
        // element of its own.
        var borderBrush = new SolidColorBrush(Color.FromArgb((byte)(NormalOpacity * 255), 90, 90, 90));
        borderBrush.Freeze();
        _thumbBorderPen = new Pen(borderBrush, BorderThickness);
        _thumbBorderPen.Freeze();

        var borderBrushDragging = new SolidColorBrush(Color.FromArgb((byte)(DraggingOpacity * 255), 90, 90, 90));
        borderBrushDragging.Freeze();
        _thumbBorderPenDragging = new Pen(borderBrushDragging, BorderThickness);
        _thumbBorderPenDragging.Freeze();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiffViewportThumb"/> class.
    /// </summary>
    public DiffViewportThumb()
    {
        Cursor = Cursors.Arrow;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the normalized (0-1) position of the top of the current viewport within the document.
    /// Only meaningful while not actively being dragged - see <see cref="OnRender"/>.
    /// </summary>
    public double ViewportStart { get; private set; }

    /// <summary>
    /// Gets the normalized (0-1) fraction of the document currently visible.
    /// </summary>
    public double ViewportFraction { get; private set; } = 1;

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs while the user drags the thumb, or clicks elsewhere in its track - carries the
    /// normalized (0-1) position the top of the viewport should scroll to.
    /// </summary>
    public event Action<double>? ScrollRequested;

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates the thumb's position/size to match the editor pane's current scroll state.
    /// </summary>
    /// <param name="start">The normalized (0-1) position of the top of the visible viewport.</param>
    /// <param name="fraction">The normalized (0-1) fraction of the document currently visible.</param>
    public void UpdateViewport(double start, double fraction)
    {
        ViewportFraction = Math.Clamp(fraction, 0, 1);
        ViewportStart = Math.Clamp(start, 0, 1 - ViewportFraction);
        InvalidateVisual();
    }

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    /// <remarks>
    /// While dragging, the rendered position comes from <see cref="_dragRenderTop"/> (set directly
    /// from the mouse in the drag handlers below) rather than from <see cref="ViewportStart"/>.
    /// <see cref="ViewportStart"/> is driven by <see cref="UpdateViewport"/>, which the hosting
    /// <c>FileCompareControl</c> calls in response to the target editor's own
    /// <c>ScrollOffsetChanged</c>/<c>VisualLinesChanged</c> events - i.e. it lags behind the mouse
    /// by however long AvalonEdit takes to lay the new scroll position out and fire those events
    /// back. Rendering from that round trip instead of straight from the mouse was the actual
    /// source of the reported jumpiness/"gets confused" behavior (worse the faster or heavier the
    /// re-layout), not anything about how often <see cref="ScrollRequested"/> itself fires.
    /// </remarks>
    protected override void OnRender(DrawingContext drawingContext)
    {
        double height = ActualHeight;
        double width = ActualWidth;
        if (height <= 0 || width <= 0) return;

        double thumbHeight = Math.Max(MinThumbHeight, ViewportFraction * height);
        double top = _isDragging
            ? Math.Clamp(_dragRenderTop, 0, Math.Max(0, height - thumbHeight))
            : Math.Clamp(ViewportStart * height, 0, Math.Max(0, height - thumbHeight));

        drawingContext.DrawRoundedRectangle(
            _isDragging ? _thumbBrushDragging : _thumbBrush,
            _isDragging ? _thumbBorderPenDragging : _thumbBorderPen,
            new Rect(0, top, width, thumbHeight), CornerRadius, CornerRadius);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (ActualHeight <= 0) return;

        double clickY = e.GetPosition(this).Y;
        double thumbHeight = Math.Max(MinThumbHeight, ViewportFraction * ActualHeight);
        double thumbTop = Math.Clamp(ViewportStart * ActualHeight, 0, Math.Max(0, ActualHeight - thumbHeight));

        // Grabbing the thumb itself preserves the exact point you grabbed it at; clicking
        // elsewhere in the track jumps the thumb so its center lands under the cursor, then lets
        // the same drag continue from there without needing to release and re-grab.
        _dragGrabOffset = clickY >= thumbTop && clickY <= thumbTop + thumbHeight
            ? clickY - thumbTop
            : thumbHeight / 2;

        _isDragging = true;
        CaptureMouse();
        ApplyDrag(clickY - _dragGrabOffset);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging) return;

        ApplyDrag(e.GetPosition(this).Y - _dragGrabOffset);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_isDragging) return;

        // A fast drag can end with the pointer having moved past wherever the last MouseMove left
        // it - WPF coalesces rapid mouse-move messages under load - so snap to the button-up
        // event's own (always current) position once more before ending the drag.
        ApplyDrag(e.GetPosition(this).Y - _dragGrabOffset);

        _isDragging = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    #endregion

    #region Private Methods

    // Renders immediately from the mouse position and asks the hosting control to scroll to match
    // - see OnRender's remarks for why the render half doesn't wait for that scroll to complete.
    private void ApplyDrag(double rawThumbTop)
    {
        if (ActualHeight <= 0) return;

        double thumbHeight = Math.Max(MinThumbHeight, ViewportFraction * ActualHeight);
        _dragRenderTop = Math.Clamp(rawThumbTop, 0, Math.Max(0, ActualHeight - thumbHeight));
        InvalidateVisual();

        ScrollRequested?.Invoke(Math.Clamp(_dragRenderTop / ActualHeight, 0, 1));
    }

    #endregion
}
