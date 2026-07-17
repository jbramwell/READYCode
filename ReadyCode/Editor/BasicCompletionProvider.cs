// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;

namespace ReadyCode.Editor;

/// <summary>
/// Full C64 BASIC V2 keyword completion table.
/// Snippets use '|' to mark the initial caret position after insertion.
/// </summary>
public static class BasicCompletionProvider
{
    #region Public Properties

    /// <summary>
    /// Gets the full list of completion entries for the C64 BASIC V2 keyword set.
    /// </summary>
    public static readonly IReadOnlyList<BasicCompletionData> AllItems = Build();

    /// <summary>
    /// Gets the display order for keyword categories (e.g. for the "BASIC Keywords" reference panel).
    /// </summary>
    public static readonly IReadOnlyList<string> CategoryOrder =
    [
        "Control Flow",
        "Program Editing",
        "Variables & Data",
        "Math Functions",
        "String Functions",
        "Input & Output",
        "Files & Devices",
        "System & Memory",
        "Logical Operators",
    ];

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns all items whose Text starts with <paramref name="prefix"/> (case-insensitive),
    /// sorted alphabetically so the first entry is always the predictable ghost-text suggestion.
    /// </summary>
    public static List<BasicCompletionData> GetMatches(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return [];
        return [.. AllItems
            .Where(i => i.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase)];
    }

    #endregion

    #region Private Methods

    // Built from BasicTokens.Keywords, the single source of truth for keyword metadata, so
    // completion can never drift out of sync with the token table. Keywords with no completion
    // metadata (the single-character operators) are excluded.
    private static BasicCompletionData[] Build() =>
    [
        .. BasicTokens.Keywords
            .Where(kv => kv.Value.Snippet != null)
            .Select(kv => new BasicCompletionData(kv.Key, kv.Value.Snippet!, kv.Value.Description!, kv.Value.Category!))
    ];

    #endregion
}
