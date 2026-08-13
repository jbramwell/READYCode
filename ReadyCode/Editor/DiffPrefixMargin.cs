// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Media;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// A gutter margin showing a "+"/"-" prefix for each line of the File Compare feature's unified
/// (single-pane) view, the same way GitHub's unified diff marks added/removed lines - color
/// alone (see <see cref="DiffLineColorizer"/>) is enough for the split view's two separate panes,
/// but the unified view interleaves both files' lines in one pane, so a text marker is needed too.
/// </summary>
public class DiffPrefixMargin : AbstractMargin
{
    #region Private Fields

    private const double _rightPadding = 6;

    private static readonly Typeface _typeface = new("Consolas");
    private static readonly Brush _insertedBrush = new SolidColorBrush(Color.FromRgb(46, 160, 67));
    private static readonly Brush _deletedBrush = new SolidColorBrush(Color.FromRgb(220, 53, 69));
    private static readonly Brush _neutralBrush = Brushes.Gray;

    #endregion

    #region Constructors

    static DiffPrefixMargin()
    {
        ((SolidColorBrush)_insertedBrush).Freeze();
        ((SolidColorBrush)_deletedBrush).Freeze();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the diff pieces backing the unified pane, one per document line in order.
    /// </summary>
    public IReadOnlyList<DiffPiece>? Lines { get; set; }

    /// <summary>
    /// Gets or sets the font size to draw prefixes at, matching the editor's own font size.
    /// </summary>
    public double FontSize { get; set; } = 14;

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = CreateFormattedText("+").Width + _rightPadding;
        return new Size(width, 0);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        var lines = Lines;
        if (textView == null || lines == null || !textView.VisualLinesValid) return;

        foreach (VisualLine line in textView.VisualLines)
        {
            int index = line.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= lines.Count) continue;

            var (prefix, brush) = lines[index].Type switch
            {
                ChangeType.Inserted => ("+", _insertedBrush),
                ChangeType.Deleted => ("-", _deletedBrush),
                _ => (null, _neutralBrush),
            };
            if (prefix == null) continue;

            var formattedText = CreateFormattedText(prefix, brush);
            double y = line.VisualTop - textView.VerticalOffset + (line.Height - formattedText.Height) / 2;
            drawingContext.DrawText(formattedText, new Point(ActualWidth - _rightPadding - formattedText.Width, y));
        }
    }

    /// <inheritdoc/>
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

    private FormattedText CreateFormattedText(string text, Brush? brush = null) =>
        new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface, FontSize, brush ?? _neutralBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    #endregion
}
