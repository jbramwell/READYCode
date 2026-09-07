// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ICSharpCode.AvalonEdit.Document;

namespace ReadyCode.Editor;

/// <summary>
/// Bridges AvalonEdit's <see cref="TextDocument"/> to the editor-independent line model used by
/// the cross-platform core analyses.
/// </summary>
public static class TextDocumentExtensions
{
    #region Public Methods

    /// <summary>
    /// Snapshots every line of <paramref name="document"/> as a <see cref="SourceLine"/>, with
    /// the same offsets AvalonEdit reports for the corresponding <see cref="DocumentLine"/>.
    /// </summary>
    public static List<SourceLine> ToSourceLines(this TextDocument document)
    {
        var lines = new List<SourceLine>(document.LineCount);
        foreach (var line in document.Lines)
            lines.Add(new SourceLine(line.Offset, document.GetText(line)));
        return lines;
    }

    #endregion
}
