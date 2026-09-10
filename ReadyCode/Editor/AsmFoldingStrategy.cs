// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace ReadyCode.Editor;

/// <summary>
/// Computes collapsible fold regions for 6502 assembly source: runs of 2+ consecutive full-line
/// ";" comments. The analysis itself lives in the cross-platform <see cref="AsmFoldRegionFinder"/>;
/// this class adapts it to AvalonEdit.
/// </summary>
public class AsmFoldingStrategy
{
    #region Public Methods

    /// <summary>
    /// Recomputes fold regions for <paramref name="document"/> and applies them to <paramref name="manager"/>.
    /// </summary>
    /// <param name="manager">The folding manager to update.</param>
    /// <param name="document">The document to compute fold regions for.</param>
    public void UpdateFoldings(FoldingManager manager, TextDocument document) =>
        manager.UpdateFoldings(CreateNewFoldings(document), -1);

    /// <summary>
    /// Computes every fold region in <paramref name="document"/>, sorted by start offset (required
    /// by <see cref="FoldingManager.UpdateFoldings"/>).
    /// </summary>
    /// <param name="document">The document to compute fold regions for.</param>
    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document) =>
        AsmFoldRegionFinder.Find(document.ToSourceLines())
            .Select(r => new NewFolding(r.StartOffset, r.EndOffset));

    #endregion
}
