// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// WPF Adorner that draws an inline "ghost text" suggestion starting at the current caret
/// position.  Adorners render above all visual-line DrawingVisual children, which guarantees
/// the text is always visible on top of the editor content.
/// </summary>
public sealed class GhostTextRenderer : Adorner
{
    #region Private Fields

    private static readonly Brush _ghostBrush;

    private readonly TextArea _textArea;

    #endregion

    #region Constructors

    static GhostTextRenderer()
    {
        _ghostBrush = new SolidColorBrush(Color.FromArgb(180, 140, 140, 140));
        _ghostBrush.Freeze();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GhostTextRenderer"/> class.
    /// </summary>
    /// <param name="textArea">The text area to render the ghost text suggestion over.</param>
    public GhostTextRenderer(TextArea textArea) : base(textArea)
    {
        _textArea       = textArea;
        IsHitTestVisible = false;

        // Re-draw whenever AvalonEdit rebuilds visual lines or the user scrolls.
        textArea.TextView.VisualLinesChanged  += (_, _) => InvalidateVisual();
        textArea.TextView.ScrollOffsetChanged += (_, _) => InvalidateVisual();
    }

    #endregion

    #region Public Properties

    /// <summary>Text to render after the caret.  Empty string = no suggestion.</summary>
    public string GhostText { get; set; } = string.Empty;

    #endregion

    #region Public Methods

    /// <summary>
    /// Draws the current <see cref="GhostText"/> suggestion at the caret position, if any.
    /// </summary>
    /// <param name="drawingContext">The drawing context to draw into.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(GhostText)) return;

        var textView = _textArea.TextView;
        if (!textView.VisualLinesValid) return;

        try
        {
            var caretPos = _textArea.Caret.Position; // 1-based Line, 1-based Column

            // Search the already-built visible lines — the caret is always on one of them.
            var visualLine = textView.VisualLines.FirstOrDefault(vl =>
                vl.FirstDocumentLine.LineNumber <= caretPos.Line &&
                vl.LastDocumentLine.LineNumber  >= caretPos.Line);

            if (visualLine == null) return;

            // Resolve visual column.  caretPos.Column is 1-based; GetVisualColumn wants 0-based.
            int vc = caretPos.VisualColumn >= 0
                ? caretPos.VisualColumn
                : visualLine.GetVisualColumn(caretPos.Column - 1);

            var textLine = visualLine.GetTextLine(vc, caretPos.IsAtEndOfLine);

            // Coordinates relative to the textView's virtual canvas (not yet scroll-adjusted).
            double x = visualLine.GetTextLineVisualXPosition(textLine, vc)
                       - textView.ScrollOffset.X;
            double y = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop)
                       - textView.ScrollOffset.Y;

            // Transform from textView coordinates into this adorner's coordinate space,
            // which is the TextArea's coordinate space.
            Point origin = textView.TranslatePoint(new Point(x, y), _textArea);

            var typeface = new Typeface(
                _textArea.FontFamily,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);

            var ft = new FormattedText(
                GhostText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                _textArea.FontSize,
                _ghostBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(ft, origin);
        }
        catch
        {
            // Layout not yet ready — VisualLinesChanged will trigger another InvalidateVisual().
        }
    }

    #endregion
}
