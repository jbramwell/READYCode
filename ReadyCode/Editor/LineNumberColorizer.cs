// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Colors the BASIC line number at the start of each editor line (e.g. the "10" in "10 PRINT").
/// </summary>
public class LineNumberColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Regex _pattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used for line numbers on inactive lines.
    /// </summary>
    public Brush LineNumberBrush       { get; set; } = Brushes.Gray;

    /// <summary>
    /// Gets or sets the brush used for the line number on the active line.
    /// </summary>
    public Brush ActiveLineNumberBrush { get; set; } = Brushes.White;

    /// <summary>
    /// Gets or sets the document line number considered active, or -1 if none.
    /// </summary>
    public int   ActiveDocumentLineNumber { get; set; } = -1;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colors the BASIC line number at the start of the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        Match match = _pattern.Match(text);
        if (!match.Success) return;

        int start = line.Offset + match.Groups[2].Index;
        int end   = start + match.Groups[2].Length;
        bool isActive = line.LineNumber == ActiveDocumentLineNumber;
        var brush = isActive ? ActiveLineNumberBrush : LineNumberBrush;
        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
    }

    #endregion
}
