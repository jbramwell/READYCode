// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// A gutter margin showing sequential editor line numbers (1, 2, 3, ...) for assembly source,
/// which - unlike BASIC - has no line numbers embedded in the text itself. Numbers are always
/// right-aligned; when <see cref="ZeroPadWidth"/> is greater than zero, they're additionally
/// left-padded with zeros to that many digits, mirroring the BASIC line-number padding setting.
/// </summary>
public class AsmLineNumberMargin : AbstractMargin
{
    #region Private Fields

    private const double _rightPadding = 4;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw line numbers.
    /// </summary>
    public Brush TextBrush { get; set; } = Brushes.Gray;

    /// <summary>
    /// Gets or sets the number of digits to zero-pad line numbers to, or 0 to show each number
    /// at its natural width instead.
    /// </summary>
    public int ZeroPadWidth { get; set; }

    /// <summary>
    /// Gets or sets the font size line numbers are drawn at, matching the editor's own font size.
    /// </summary>
    public double FontSize { get; set; } = 12;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Measures the margin wide enough to fit the document's largest line number (or the
    /// zero-padded width, if that's wider).
    /// </summary>
    /// <param name="availableSize">The available size.</param>
    protected override Size MeasureOverride(Size availableSize)
    {
        int lineCount = Document?.LineCount ?? 1;
        int digitCount = Math.Max(ZeroPadWidth, lineCount.ToString(CultureInfo.InvariantCulture).Length);
        double width = CreateFormattedText(new string('8', digitCount)).Width + _rightPadding;
        return new Size(width, 0);
    }

    /// <summary>
    /// Draws the line number for every currently visible line, right-aligned against the
    /// margin's right edge.
    /// </summary>
    /// <param name="drawingContext">The drawing context to render into.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        foreach (VisualLine line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber;
            string text = ZeroPadWidth > 0
                ? lineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(ZeroPadWidth, '0')
                : lineNumber.ToString(CultureInfo.InvariantCulture);

            var formattedText = CreateFormattedText(text);
            double y = line.VisualTop - textView.VerticalOffset;
            drawingContext.DrawText(formattedText, new Point(ActualWidth - _rightPadding - formattedText.Width, y));
        }
    }

    /// <summary>
    /// Hooks/unhooks the text view's redraw-triggering events so the margin's size and content
    /// stay in sync as the document scrolls or is edited.
    /// </summary>
    /// <param name="oldTextView">The text view being detached, or null.</param>
    /// <param name="newTextView">The text view being attached, or null.</param>
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

        InvalidateMeasure();
        InvalidateVisual();
    }

    #endregion

    #region Private Methods

    private void TextView_VisualLinesChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void TextView_ScrollOffsetChanged(object? sender, EventArgs e) => InvalidateVisual();

    private FormattedText CreateFormattedText(string text) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), FontSize, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    #endregion
}
