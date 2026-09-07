// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Editor;

/// <summary>
/// One line of a source text, located by character offset - the UI-toolkit-free counterpart of
/// AvalonEdit's <c>DocumentLine</c>, so line-oriented analysis (folding, etc.) can run in the
/// cross-platform core and be unit tested without a text editor control.
/// </summary>
/// <param name="Offset">Offset of the line's first character.</param>
/// <param name="Text">The line's text, without its line delimiter.</param>
public readonly record struct SourceLine(int Offset, string Text)
{
    /// <summary>
    /// Gets the offset just past the line's last character, excluding the line delimiter -
    /// matching AvalonEdit's <c>DocumentLine.EndOffset</c>.
    /// </summary>
    public int EndOffset => Offset + Text.Length;

    /// <summary>
    /// Splits <paramref name="text"/> into lines using the same delimiter rules as AvalonEdit's
    /// <c>TextDocument</c>: "\r\n", "\n", or a lone "\r" each end a line, and the text always has
    /// at least one line (an empty text is one empty line).
    /// </summary>
    public static List<SourceLine> Split(string text)
    {
        var lines = new List<SourceLine>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\r' && c != '\n') continue;

            lines.Add(new SourceLine(start, text[start..i]));

            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;

            start = i + 1;
        }

        lines.Add(new SourceLine(start, text[start..]));
        return lines;
    }
}
