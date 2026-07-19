// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Colors a ";" comment and the remainder of the line in 6502 assembly source. Runs after the
/// other assembly colorizers so it overrides any mnemonic/number/label coloring inside the
/// comment text.
/// </summary>
public class AsmCommentColorizer : DocumentColorizingTransformer
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw a comment.
    /// </summary>
    public Brush CommentBrush { get; set; } = Brushes.Green;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colors from the first ";" to the end of the given line, if one is present.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int idx = text.IndexOf(';');
        if (idx < 0) return;

        int start = line.Offset + idx;
        int end = line.Offset + text.Length;
        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(CommentBrush));
    }

    #endregion
}
