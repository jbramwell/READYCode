// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Highlights find/replace matches in the editor, drawing the current match in a distinct color
/// from the other matches.
/// </summary>
public class FindHighlightColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    // AnchorSegment (rather than raw offset/length) so a highlight tracks its matched text through
    // edits elsewhere in the document between debounced re-searches - typing above a match pushes
    // its anchors forward with it, instead of the highlight staying at a now-stale numeric offset
    // and appearing to drift onto whatever text ends up there.
    private readonly List<AnchorSegment> _matches = new();
    private int _currentIndex = -1;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the background brush used for non-current matches.
    /// </summary>
    public Brush MatchBrush          { get; set; } = Brushes.Yellow;

    /// <summary>
    /// Gets or sets the foreground brush used for non-current matches.
    /// </summary>
    public Brush MatchFgBrush        { get; set; } = Brushes.Black;

    /// <summary>
    /// Gets or sets the background brush used for the current match.
    /// </summary>
    public Brush CurrentMatchBrush   { get; set; } = Brushes.Orange;

    /// <summary>
    /// Gets or sets the foreground brush used for the current match.
    /// </summary>
    public Brush CurrentMatchFgBrush { get; set; } = Brushes.Black;

    #endregion

    #region Public Methods

    /// <summary>
    /// Replaces the set of highlighted matches and marks which one is current.
    /// </summary>
    /// <param name="document">The document the offsets belong to, used to anchor each match so it
    /// keeps tracking its matched text through subsequent edits.</param>
    /// <param name="matches">The offset/length pairs of all matches to highlight.</param>
    /// <param name="currentIndex">The index within <paramref name="matches"/> of the current match, or -1 for none.</param>
    public void SetMatches(TextDocument document, IEnumerable<(int Offset, int Length)> matches, int currentIndex)
    {
        _matches.Clear();
        foreach (var (offset, length) in matches)
            _matches.Add(new AnchorSegment(document, offset, length));
        _currentIndex = currentIndex;
    }

    /// <summary>
    /// Clears all highlighted matches.
    /// </summary>
    public void Clear()
    {
        _matches.Clear();
        _currentIndex = -1;
    }

    /// <summary>
    /// Colorizes any matches that overlap the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        if (_matches.Count == 0) return;

        int lineStart = line.Offset;
        int lineEnd   = line.EndOffset;

        for (int i = 0; i < _matches.Count; i++)
        {
            var segment = _matches[i];
            int offset = segment.Offset;
            int length = segment.Length;
            if (offset + length <= lineStart || offset >= lineEnd) continue;

            int start = Math.Max(offset, lineStart);
            int end   = Math.Min(offset + length, lineEnd);

            bool isCurrent = i == _currentIndex;
            var bg = isCurrent ? CurrentMatchBrush   : MatchBrush;
            var fg = isCurrent ? CurrentMatchFgBrush : MatchFgBrush;
            ChangeLinePart(start, end, e =>
            {
                e.TextRunProperties.SetBackgroundBrush(bg);
                e.TextRunProperties.SetForegroundBrush(fg);
            });
        }
    }

    #endregion
}
