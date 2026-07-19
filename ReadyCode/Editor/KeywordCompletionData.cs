// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace ReadyCode.Editor;

/// <summary>
/// Minimal <see cref="ISegment"/> implementation for inline completions that don't use a CompletionWindow.
/// </summary>
public sealed class EditorSegment(int offset, int length) : ISegment
{
    #region Public Properties

    /// <summary>
    /// Gets the start offset of the segment.
    /// </summary>
    public int Offset { get; } = offset;

    /// <summary>
    /// Gets the length of the segment.
    /// </summary>
    public int Length { get; } = length;

    /// <summary>
    /// Gets the end offset of the segment.
    /// </summary>
    public int EndOffset => Offset + Length;

    #endregion
}

/// <summary>
/// A single keyword/mnemonic completion list entry, shared by the BASIC and Assembly completion
/// providers. The snippet string uses '|' to mark where the caret lands after insertion.
/// </summary>
public class KeywordCompletionData : ICompletionData
{
    #region Private Fields

    private readonly string _snippet;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="KeywordCompletionData"/> class.
    /// </summary>
    /// <param name="text">The keyword text inserted when this entry is selected.</param>
    /// <param name="snippet">The snippet to insert, with '|' marking the caret position.</param>
    /// <param name="description">The description shown for this entry.</param>
    /// <param name="category">The reference-panel category this keyword is grouped under.</param>
    public KeywordCompletionData(string text, string snippet, string description, string category)
    {
        Text = text;
        _snippet = snippet;
        Description = description;
        Category = category;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the icon shown next to the entry in the completion list. Always null; no icons are used.
    /// </summary>
    public ImageSource? Image => null;

    /// <summary>
    /// Gets the keyword text inserted when this entry is selected.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the content displayed in the completion list (same as <see cref="Text"/>).
    /// </summary>
    public object Content => Text;

    /// <summary>
    /// Gets the description shown for this entry.
    /// </summary>
    public object Description { get; }

    /// <summary>
    /// Gets the reference-panel category this keyword is grouped under (e.g. "Math Functions").
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the sort priority used by the completion window. Always zero.
    /// </summary>
    public double Priority => 0;

    /// <summary>Snippet text with the '|' cursor marker removed.</summary>
    public string Snippet => _snippet.Replace("|", "");

    #endregion

    #region Public Methods

    /// <summary>
    /// Inserts this entry's snippet into the document and positions the caret at the '|' marker.
    /// </summary>
    /// <param name="textArea">The text area to insert the completion into.</param>
    /// <param name="completionSegment">The segment of text being replaced by the completion.</param>
    /// <param name="insertionRequestEventArgs">The event that triggered the insertion request.</param>
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        int cursorMark = _snippet.IndexOf('|');
        string insertText = _snippet.Replace("|", "");

        textArea.Document.Replace(completionSegment, insertText);

        textArea.Caret.Offset = completionSegment.Offset + (cursorMark >= 0 ? cursorMark : insertText.Length);
    }

    #endregion
}
