// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Draws a 1px top and bottom border on the line that contains the caret,
/// matching VS Code's current-line indicator style.
/// </summary>
public sealed class CurrentLineBorderRenderer : IBackgroundRenderer
{
    #region Private Fields

    private readonly TextEditor _editor;
    private Pen _pen;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentLineBorderRenderer"/> class.
    /// </summary>
    /// <param name="editor">The text editor whose current line should be bordered.</param>
    public CurrentLineBorderRenderer(TextEditor editor)
    {
        _editor = editor;
        _pen    = MakePen(Color.FromRgb(0x45, 0x45, 0x45));
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the rendering layer this renderer draws into.
    /// </summary>
    public KnownLayer Layer => KnownLayer.Background;

    #endregion

    #region Public Methods

    /// <summary>
    /// Changes the border color used for the current line.
    /// </summary>
    /// <param name="color">The new border color.</param>
    public void SetColor(Color color) => _pen = MakePen(color);

    /// <summary>
    /// Draws the top and bottom border around the line that contains the caret.
    /// </summary>
    /// <param name="textView">The text view being rendered.</param>
    /// <param name="drawingContext">The drawing context to draw into.</param>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_editor.Document == null) return;

        textView.EnsureVisualLines();

        var caretLine = _editor.Document.GetLineByOffset(_editor.CaretOffset);

        foreach (var vl in textView.VisualLines)
        {
            if (vl.FirstDocumentLine.LineNumber > caretLine.LineNumber) break;
            if (vl.LastDocumentLine.LineNumber  < caretLine.LineNumber) continue;

            double top, bottom;
            if (vl.TextLines.Count == 1)
            {
                // AvalonEdit's own Caret.CalcCaretRectangle() positions the caret using
                // VisualYPosition.TextTop/TextBottom, not LineTop/LineBottom - decompiled and
                // confirmed via ICSharpCode.AvalonEdit.Editing.Caret. TextTop/TextBottom are
                // baseline-anchored (TextTop = LineTop + (thisLine.Baseline - DefaultBaseline);
                // TextBottom = TextTop + DefaultLineHeight), which differs from LineTop/LineBottom
                // (LineTop; LineTop + thisLine.Height) whenever a line's own baseline/height isn't
                // exactly the "default" ('x'-measured) one - true for a genuinely empty line with
                // the "Pet Me 64" custom font, which used to leave the border a couple of pixels
                // short of the caret until the first character was typed. Matching the caret's own
                // formula exactly (rather than an approximation of it) guarantees they always agree.
                var textLine = vl.TextLines[0];
                top    = vl.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop) - textView.ScrollOffset.Y;
                bottom = vl.GetTextLineVisualYPosition(textLine, VisualYPosition.TextBottom) - textView.ScrollOffset.Y;
            }
            else
            {
                // A wrapped current line spans multiple rows - keep the border around the whole
                // logical line (VisualTop is document-relative; subtract scroll for viewport Y)
                // rather than collapsing it to just the caret's own row.
                top    = vl.VisualTop - textView.ScrollOffset.Y;
                bottom = top + vl.Height;
            }
            double width = textView.ActualWidth;

            // +0.5 / -0.5 snaps to pixel centres for a crisp 1px hairline. The top line gets an
            // extra 2px of padding above the text so it doesn't sit flush against it.
            drawingContext.DrawLine(_pen,
                new Point(0, top - 2.5),
                new Point(width, top - 2.5));
            drawingContext.DrawLine(_pen,
                new Point(0, bottom + 0.5),
                new Point(width, bottom + 0.5));

            break;
        }
    }

    #endregion

    #region Private Methods

    private static Pen MakePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.0);
        pen.Freeze();
        return pen;
    }

    #endregion
}
