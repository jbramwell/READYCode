// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Highlights colon-terminated labels at the start of a line (e.g. "LOOP:" in "LOOP: LDA $00"),
/// the one label form understood consistently across assembler dialects. Colon-less label
/// conventions (disambiguated by indentation in some assemblers) aren't recognized, since that
/// convention isn't universal.
/// </summary>
public class AsmLabelColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Regex _labelPattern = new(@"^\s*([A-Za-z_][A-Za-z0-9_]*):", RegexOptions.Compiled);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw a label and its trailing colon.
    /// </summary>
    public Brush LabelBrush { get; set; } = Brushes.DarkCyan;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colorizes a leading label on the given line, if one is present.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        Match m = _labelPattern.Match(text);
        if (!m.Success) return;

        var nameGroup = m.Groups[1];
        int start = line.Offset + nameGroup.Index;
        int end = start + nameGroup.Length + 1; // include the trailing colon
        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(LabelBrush));
    }

    #endregion
}
