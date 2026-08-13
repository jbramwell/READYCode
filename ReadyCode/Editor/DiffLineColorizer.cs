// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Media;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Which side of a diff a <see cref="DiffLineColorizer"/> is painting - determines how a
/// <see cref="ChangeType.Modified"/> line is tinted, since DiffPlex reuses that one type for
/// both the "old" (deletion-flavored) and "new" (insertion-flavored) half of a paired change.
/// </summary>
public enum DiffPaneRole
{
    /// <summary>The left/old pane of a split diff.</summary>
    Old,

    /// <summary>The right/new pane of a split diff.</summary>
    New,

    /// <summary>The single pane of a unified diff.</summary>
    Unified,
}

/// <summary>
/// Colors each document line's background according to its DiffPlex <see cref="DiffPiece.Type"/>
/// - green for insertions, red for deletions, a faint hatch for DiffPlex's own alignment padding
/// rows (<see cref="ChangeType.Imaginary"/>) - and, for a <see cref="ChangeType.Modified"/> line,
/// additionally highlights just the differing word(s) via its <see cref="DiffPiece.SubPieces"/>
/// more strongly than the rest of the line. Used for both the read-only File Compare panes;
/// <see cref="Lines"/> is swapped out whenever the compare tab's diff model or view (split vs.
/// unified) changes.
/// </summary>
public class DiffLineColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Brush _insertedBg = MakeBrush(46, 160, 67, 40);
    private static readonly Brush _deletedBg = MakeBrush(220, 53, 69, 40);
    private static readonly Brush _insertedSubBg = MakeBrush(46, 160, 67, 100);
    private static readonly Brush _deletedSubBg = MakeBrush(220, 53, 69, 100);
    private static readonly Brush _imaginaryBg = MakeBrush(128, 128, 128, 35);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the diff pieces backing this pane, one per document line in order (including
    /// blank text for any <see cref="ChangeType.Imaginary"/> padding lines), or null to disable
    /// all highlighting.
    /// </summary>
    public IReadOnlyList<DiffPiece>? Lines { get; set; }

    /// <summary>
    /// Gets or sets which side of the diff this colorizer is painting.
    /// </summary>
    public DiffPaneRole Role { get; set; } = DiffPaneRole.Unified;

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override void ColorizeLine(DocumentLine line)
    {
        var pieces = Lines;
        if (pieces == null) return;

        int index = line.LineNumber - 1;
        if (index < 0 || index >= pieces.Count) return;
        var piece = pieces[index];

        Brush? lineBrush = piece.Type switch
        {
            ChangeType.Inserted => _insertedBg,
            ChangeType.Deleted => _deletedBg,
            ChangeType.Imaginary => _imaginaryBg,
            ChangeType.Modified => Role == DiffPaneRole.New ? _insertedBg : _deletedBg,
            _ => null,
        };

        if (lineBrush != null)
            ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetBackgroundBrush(lineBrush));

        if (piece.Type != ChangeType.Modified || piece.SubPieces == null || piece.SubPieces.Count == 0)
            return;

        // Sub-pieces concatenate to reconstruct the line's full text in order - walk them to find
        // each changed word's offset within the line, and paint just those more strongly.
        Brush subBrush = Role == DiffPaneRole.New ? _insertedSubBg : _deletedSubBg;
        int offset = line.Offset;
        foreach (var sub in piece.SubPieces)
        {
            int length = sub.Text?.Length ?? 0;
            if (length > 0 && sub.Type != ChangeType.Unchanged)
            {
                int start = Math.Min(offset, line.EndOffset);
                int end = Math.Min(offset + length, line.EndOffset);
                if (end > start)
                    ChangeLinePart(start, end, e => e.TextRunProperties.SetBackgroundBrush(subBrush));
            }
            offset += length;
        }
    }

    #endregion

    #region Private Methods

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    #endregion
}
