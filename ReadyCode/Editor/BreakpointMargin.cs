// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// A gutter margin showing breakpoint markers for a BASIC tab: a filled red dot on lines with an
/// enabled breakpoint, a hollow grey ring for a disabled one. Clicking a line raises
/// <see cref="BreakpointToggleRequested"/> with that document line number - this control only
/// renders and reports clicks, the host resolves the click through the tab's line address table
/// and updates the breakpoint store, mirroring <see cref="AsmLineNumberMargin"/>'s "control
/// renders, host owns the data" division of labor.
/// </summary>
public sealed class BreakpointMargin : AbstractMargin
{
    #region Private Fields

    private const double _width = 16;
    private const double _dotDiameter = 10;

    private static readonly Brush _enabledFill = new SolidColorBrush(Color.FromRgb(0xE5, 0x1C, 0x23));
    private static readonly Pen _disabledPen = MakePen(Color.FromRgb(0x9A, 0x9A, 0x9A));

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the document lines (1-based) with an enabled breakpoint.
    /// </summary>
    public IReadOnlySet<int> EnabledBreakpointLines { get; set; } = new HashSet<int>();

    /// <summary>
    /// Gets or sets the document lines (1-based) with a disabled breakpoint.
    /// </summary>
    public IReadOnlySet<int> DisabledBreakpointLines { get; set; } = new HashSet<int>();

    /// <summary>
    /// Occurs when the user clicks a line in this margin, carrying that line's 1-based document
    /// line number. Raised for every click, even on a line with no breakpoint yet (a new one) or
    /// one that can't validly hold a breakpoint (the host decides that, not this control).
    /// </summary>
    public event EventHandler<int>? BreakpointToggleRequested;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Measures a fixed-width margin, wide enough for the breakpoint dot.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize) => new(_width, 0);

    /// <summary>
    /// Draws a breakpoint marker for every currently visible line that has one.
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        // WPF only hit-tests areas something was actually drawn into - most of this margin has
        // nothing to draw (no breakpoint on most lines), so without this, clicking anywhere
        // except directly on an existing dot would silently miss and never reach OnMouseDown,
        // making it impossible to ever SET a new breakpoint by clicking.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        foreach (VisualLine line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber;
            bool enabled = EnabledBreakpointLines.Contains(lineNumber);
            bool disabled = !enabled && DisabledBreakpointLines.Contains(lineNumber);
            if (!enabled && !disabled) continue;

            double y = line.VisualTop - textView.VerticalOffset + (line.Height - _dotDiameter) / 2;
            var center = new Point(_width / 2, y + _dotDiameter / 2);

            if (enabled)
                drawingContext.DrawEllipse(_enabledFill, null, center, _dotDiameter / 2, _dotDiameter / 2);
            else
                drawingContext.DrawEllipse(null, _disabledPen, center, _dotDiameter / 2, _dotDiameter / 2);
        }
    }

    /// <summary>
    /// Hooks/unhooks the text view's redraw-triggering events so the margin stays in sync as the
    /// document scrolls or is edited.
    /// </summary>
    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
            oldTextView.ScrollOffsetChanged -= TextView_ScrollOffsetChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);

        if (newTextView != null)
        {
            newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
            newTextView.ScrollOffsetChanged += TextView_ScrollOffsetChanged;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Resolves a click to the document line under the pointer and raises
    /// <see cref="BreakpointToggleRequested"/>.
    /// </summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        var textView = TextView;
        if (textView != null && textView.VisualLinesValid)
        {
            double clickY = e.GetPosition(this).Y;

            foreach (VisualLine line in textView.VisualLines)
            {
                double top = line.VisualTop - textView.VerticalOffset;
                double bottom = top + line.Height;

                if (clickY >= top && clickY < bottom)
                {
                    BreakpointToggleRequested?.Invoke(this, line.FirstDocumentLine.LineNumber);
                    e.Handled = true;
                    break;
                }
            }
        }

        base.OnMouseDown(e);
    }

    #endregion

    #region Private Methods

    private void TextView_VisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void TextView_ScrollOffsetChanged(object? sender, EventArgs e) => InvalidateVisual();

    private static Pen MakePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.0);
        pen.Freeze();
        return pen;
    }

    #endregion
}
