// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Editor;

/// <summary>
/// A single keyword/mnemonic completion list entry, shared by the BASIC and Assembly completion
/// providers. Editor-toolkit independent: each UI wraps it in its own completion-data type. The
/// snippet template uses '|' to mark where the caret lands after insertion.
/// </summary>
public sealed class KeywordCompletionItem
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="KeywordCompletionItem"/> class.
    /// </summary>
    /// <param name="text">The keyword text inserted when this entry is selected.</param>
    /// <param name="snippetTemplate">The snippet to insert, with '|' marking the caret position.</param>
    /// <param name="description">The description shown for this entry.</param>
    /// <param name="category">The reference-panel category this keyword is grouped under.</param>
    public KeywordCompletionItem(string text, string snippetTemplate, string description, string category)
    {
        Text = text;
        SnippetTemplate = snippetTemplate;
        Description = description;
        Category = category;

        int cursorMark = snippetTemplate.IndexOf('|');
        InsertText = snippetTemplate.Replace("|", "");
        CaretOffset = cursorMark >= 0 ? cursorMark : InsertText.Length;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the keyword text inserted when this entry is selected.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the raw snippet template, with '|' marking the caret position.
    /// </summary>
    public string SnippetTemplate { get; }

    /// <summary>
    /// Gets the description shown for this entry.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the reference-panel category this keyword is grouped under (e.g. "Math Functions").
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the snippet text with the '|' cursor marker removed - what actually gets inserted.
    /// </summary>
    public string InsertText { get; }

    /// <summary>
    /// Gets the snippet text with the '|' cursor marker removed (alias of <see cref="InsertText"/>).
    /// </summary>
    public string Snippet => InsertText;

    /// <summary>
    /// Gets where the caret should land after inserting <see cref="InsertText"/>, relative to the
    /// insertion point.
    /// </summary>
    public int CaretOffset { get; }

    #endregion
}
