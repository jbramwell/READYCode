// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using ReadyCode.Diagnostics;

namespace ReadyCode.Editor;

/// <summary>
/// Draws a red squiggly underline beneath each <see cref="EditorDiagnostic"/> span reported by
/// <see cref="ReadyCode.Diagnostics.BasicDiagnostics"/> (undefined GOTO/GOSUB/THEN targets,
/// unmatched FOR/NEXT, unterminated strings, duplicate line numbers) or
/// <see cref="ReadyCode.Diagnostics.AsmDiagnostics"/> (assembly errors).
/// </summary>
public sealed class ErrorSquiggleRenderer : IBackgroundRenderer
{
    #region Private Fields

    private const double SquiggleAmplitude = 1.3;
    private const double SquiggleWavelength = 3.5;

    private readonly TextEditor _editor;
    private Pen _pen;
    private IReadOnlyList<EditorDiagnostic> _diagnostics = Array.Empty<EditorDiagnostic>();

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorSquiggleRenderer"/> class.
    /// </summary>
    /// <param name="editor">The text editor whose diagnostics should be underlined.</param>
    public ErrorSquiggleRenderer(TextEditor editor)
    {
        _editor = editor;
        _pen    = MakePen(Colors.Red);
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
    /// Changes the squiggle color.
    /// </summary>
    /// <param name="color">The new squiggle color.</param>
    public void SetColor(Color color) => _pen = MakePen(color);

    /// <summary>
    /// Replaces the set of diagnostics to underline.
    /// </summary>
    /// <param name="diagnostics">The diagnostics to underline.</param>
    public void SetDiagnostics(IReadOnlyList<EditorDiagnostic> diagnostics) => _diagnostics = diagnostics;

    /// <summary>
    /// Clears all diagnostics.
    /// </summary>
    public void Clear() => _diagnostics = Array.Empty<EditorDiagnostic>();

    /// <summary>
    /// Draws a squiggly underline beneath every diagnostic that overlaps a visible line.
    /// </summary>
    /// <param name="textView">The text view being rendered.</param>
    /// <param name="drawingContext">The drawing context to draw into.</param>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_editor.Document == null || _diagnostics.Count == 0) return;

        textView.EnsureVisualLines();

        foreach (var vl in textView.VisualLines)
        {
            int lineStart = vl.FirstDocumentLine.Offset;
            int lineEnd   = vl.LastDocumentLine.EndOffset;

            foreach (var diag in _diagnostics)
            {
                if (diag.Offset + diag.Length <= lineStart || diag.Offset >= lineEnd) continue;

                int start = Math.Max(diag.Offset, lineStart);
                int end   = Math.Min(diag.Offset + diag.Length, lineEnd);
                if (end <= start) continue;

                double x1 = GetVisualX(vl, textView, start - lineStart, isAtEndOfLine: false);
                double x2 = GetVisualX(vl, textView, end - lineStart, isAtEndOfLine: end == lineEnd);
                double y  = vl.VisualTop - textView.ScrollOffset.Y + vl.Height - 2;

                DrawSquiggle(drawingContext, x1, x2, y);
            }
        }
    }

    #endregion

    #region Private Methods

    private static double GetVisualX(VisualLine vl, TextView textView, int relativeOffset, bool isAtEndOfLine)
    {
        int visualColumn = vl.GetVisualColumn(relativeOffset);
        var textLine = vl.GetTextLine(visualColumn, isAtEndOfLine);
        return vl.GetTextLineVisualXPosition(textLine, visualColumn) - textView.ScrollOffset.X;
    }

    // Each "hump" is a quadratic bezier curve back up to the baseline rather than a straight
    // zigzag line - a smooth wave reads much more clearly at small font sizes than sharp teeth,
    // matching the rounded squiggly underline VS Code and Visual Studio use for diagnostics.
    private void DrawSquiggle(DrawingContext drawingContext, double x1, double x2, double y)
    {
        if (x2 <= x1) return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x1, y), false, false);
            bool up = false;
            double x = x1;
            while (x < x2)
            {
                double nextX = Math.Min(x + SquiggleWavelength, x2);
                double midX  = (x + nextX) / 2;
                double waveY = up ? y - SquiggleAmplitude : y + SquiggleAmplitude;
                ctx.QuadraticBezierTo(new Point(midX, waveY), new Point(nextX, y), true, false);
                up = !up;
                x = nextX;
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, _pen, geometry);
    }

    private static Pen MakePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap   = PenLineCap.Round,
            LineJoin     = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    #endregion
}
