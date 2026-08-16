// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Fills the currently-halted line with a highlight background during a debug session -
/// the "current execution line" indicator, updated from a debug session's stopped notification
/// rather than the caret (compare <see cref="CurrentLineBorderRenderer"/>, which follows the
/// caret and draws only a border rather than a fill).
/// </summary>
public sealed class DebugCurrentLineRenderer : IBackgroundRenderer
{
    #region Private Fields

    private static readonly Brush _fill = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xD7, 0x00));

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the rendering layer this renderer draws into.
    /// </summary>
    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>
    /// Gets or sets the 1-based document line to highlight, or null while no debug session is
    /// halted (nothing is drawn in that case).
    /// </summary>
    public int? CurrentLine { get; set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Draws a full-width highlight across <see cref="CurrentLine"/>, if set and visible.
    /// </summary>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (CurrentLine is not { } currentLine) return;

        textView.EnsureVisualLines();

        foreach (var vl in textView.VisualLines)
        {
            if (vl.FirstDocumentLine.LineNumber > currentLine) break;
            if (vl.LastDocumentLine.LineNumber < currentLine) continue;

            double top = vl.VisualTop - textView.ScrollOffset.Y;
            double width = textView.ActualWidth;

            drawingContext.DrawRectangle(_fill, null, new Rect(0, top, width, vl.Height));
            break;
        }
    }

    #endregion
}
