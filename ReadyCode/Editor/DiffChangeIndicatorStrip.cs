// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Media;

namespace ReadyCode.Editor;

/// <summary>
/// A vertical strip showing a whole-document overview of change marks - a miniature map of where
/// changes are and how big they are, similar to Notepad++'s Compare plugin "Location Pane". Purely
/// visual/non-interactive: the draggable "viewport" thumb that shows/controls which portion is
/// currently visible is a separate, wider overlay - see <see cref="DiffViewportThumb"/> - so it can
/// span (and be grabbed across) more than one file's map column at once. Paints only its change
/// marks, not a background of its own - the containing "location pane" element supplies
/// <c>ThemeCompareLocationPaneBg</c> behind it (and behind the gaps between map columns), so the
/// whole pane reads as one uniform panel rather than each column having its own separately-tinted
/// patch.
/// </summary>
public sealed class DiffChangeIndicatorStrip : FrameworkElement
{
    #region Private Fields

    // A run's mark is never drawn shorter than this, even when its proportional share of the
    // strip's height would otherwise round to a sliver - a single-line change must stay visible
    // next to a much larger one.
    private const double MinMarkHeight = 3;

    private static readonly Brush _deletedBrush;
    private static readonly Brush _insertedBrush;

    private IReadOnlyList<(int Start, int Count)> _redRuns = [];
    private IReadOnlyList<(int Start, int Count)> _greenRuns = [];
    private int _totalLines = 1;

    #endregion

    #region Constructors

    static DiffChangeIndicatorStrip()
    {
        // Same hues DiffLineColorizer tints changed lines with, at full opacity - a strip mark
        // reads as "this row is red/green" the same way the line itself does.
        _deletedBrush = new SolidColorBrush(Color.FromRgb(220, 53, 69));
        _deletedBrush.Freeze();
        _insertedBrush = new SolidColorBrush(Color.FromRgb(46, 160, 67));
        _insertedBrush.Freeze();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiffChangeIndicatorStrip"/> class.
    /// </summary>
    public DiffChangeIndicatorStrip()
    {
        IsHitTestVisible = false;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Applies newly computed run data and redraws the strip.
    /// </summary>
    /// <param name="redRuns">
    /// (Start, Count) line ranges to mark red (deletions/the old side of a modified pair).
    /// </param>
    /// <param name="greenRuns">
    /// (Start, Count) line ranges to mark green (insertions/the new side of a modified pair).
    /// </param>
    /// <param name="totalLines">The pane's total line count.</param>
    public void Update(IReadOnlyList<(int Start, int Count)> redRuns, IReadOnlyList<(int Start, int Count)> greenRuns, int totalLines)
    {
        _redRuns = redRuns;
        _greenRuns = greenRuns;
        _totalLines = Math.Max(1, totalLines);
        InvalidateVisual();
    }

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        double height = ActualHeight;
        double width = ActualWidth;
        if (height <= 0 || width <= 0 || _totalLines <= 0) return;

        foreach (var run in _redRuns) DrawRun(drawingContext, run, _deletedBrush, width, height);
        foreach (var run in _greenRuns) DrawRun(drawingContext, run, _insertedBrush, width, height);
    }

    #endregion

    #region Private Methods

    private void DrawRun(DrawingContext drawingContext, (int Start, int Count) run, Brush brush, double width, double height)
    {
        double y1 = (double)run.Start / _totalLines * height;
        double y2 = (double)(run.Start + run.Count) / _totalLines * height;
        if (y2 - y1 < MinMarkHeight)
        {
            double mid = (y1 + y2) / 2;
            y1 = mid - MinMarkHeight / 2;
            y2 = mid + MinMarkHeight / 2;
        }
        y1 = Math.Clamp(y1, 0, Math.Max(0, height - MinMarkHeight));
        y2 = Math.Clamp(y2, y1 + MinMarkHeight, height);

        drawingContext.DrawRectangle(brush, null, new Rect(1, y1, Math.Max(0, width - 2), y2 - y1));
    }

    #endregion
}
