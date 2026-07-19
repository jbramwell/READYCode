// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;

namespace ReadyCode.Editor;

/// <summary>
/// Full 6502 assembly mnemonic completion table.
/// Snippets use '|' to mark the initial caret position after insertion.
/// </summary>
public static class AsmCompletionProvider
{
    #region Public Properties

    /// <summary>
    /// Gets the full list of completion entries for the 6502 mnemonic set.
    /// </summary>
    public static readonly IReadOnlyList<KeywordCompletionData> AllItems = Build();

    /// <summary>
    /// Gets the display order for mnemonic categories.
    /// </summary>
    public static readonly IReadOnlyList<string> CategoryOrder =
    [
        "Load/Store",
        "Arithmetic",
        "Logical",
        "Shift/Rotate",
        "Increment/Decrement",
        "Compare",
        "Branch",
        "Jump/Subroutine",
        "Stack",
        "Transfer",
        "Flags",
        "System",
    ];

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns all items whose Text starts with <paramref name="prefix"/> (case-insensitive),
    /// sorted alphabetically so the first entry is always the predictable ghost-text suggestion.
    /// </summary>
    public static List<KeywordCompletionData> GetMatches(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return [];
        return [.. AllItems
            .Where(i => i.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase)];
    }

    #endregion

    #region Private Methods

    // Built from AsmTokens.Mnemonics, the single source of truth for mnemonic metadata, so
    // completion can never drift out of sync with the mnemonic table.
    private static KeywordCompletionData[] Build() =>
    [
        .. AsmTokens.Mnemonics
            .Select(kv => new KeywordCompletionData(kv.Key, kv.Value.Snippet, kv.Value.Description, kv.Value.Category))
    ];

    #endregion
}
