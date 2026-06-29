// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Adds a small amount of extra vertical space between lines by inserting a zero-width
/// inline element whose height is the normal line height plus the desired extra spacing.
/// AvalonEdit has no direct line-spacing setting, so the line height is stretched this way.
/// </summary>
public class LineSpacingElementGenerator : VisualLineElementGenerator
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the extra vertical space, in device-independent pixels, added below each line.
    /// </summary>
    public double ExtraSpacing { get; set; } = 4;

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns the offset of the start of the line containing <paramref name="startOffset"/>,
    /// since a spacer element is inserted once per line.
    /// </summary>
    /// <param name="startOffset">The offset to search from.</param>
    /// <returns>The offset of the spacer element, or -1 if none is needed.</returns>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        int lineStart = CurrentContext.VisualLine.FirstDocumentLine.Offset;
        return startOffset <= lineStart ? lineStart : -1;
    }

    /// <summary>
    /// Constructs the zero-width spacer element used to stretch the line height.
    /// </summary>
    /// <param name="offset">The offset at which to insert the element.</param>
    /// <returns>The constructed spacer element.</returns>
    public override VisualLineElement? ConstructElement(int offset)
    {
        var spacer = new Rectangle
        {
            Width = 0,
            Height = CurrentContext.TextView.DefaultLineHeight + ExtraSpacing
        };

        return new InlineObjectElement(0, spacer);
    }

    #endregion
}
