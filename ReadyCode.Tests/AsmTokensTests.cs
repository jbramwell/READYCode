// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Editor;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="AsmTokens"/>, the 6502 mnemonic table backing assembly syntax
/// highlighting, completion, and hover tooltips.
/// </summary>
public class AsmTokensTests
{
    #region Public Methods

    [Fact]
    public void Mnemonics_ContainsFiftySixEntries()
    {
        Assert.Equal(56, AsmTokens.Mnemonics.Count);
    }

    [Fact]
    public void Mnemonics_AllEntriesHaveNonEmptyDescriptionAndCategory()
    {
        foreach (var (mnemonic, info) in AsmTokens.Mnemonics)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.Snippet), $"{mnemonic} has an empty snippet.");
            Assert.False(string.IsNullOrWhiteSpace(info.Description), $"{mnemonic} has an empty description.");
            Assert.False(string.IsNullOrWhiteSpace(info.Category), $"{mnemonic} has an empty category.");
        }
    }

    [Fact]
    public void IsMnemonic_IsCaseInsensitive()
    {
        Assert.True(AsmTokens.IsMnemonic("lda"));
        Assert.True(AsmTokens.IsMnemonic("Lda"));
        Assert.True(AsmTokens.IsMnemonic("LDA"));
    }

    [Fact]
    public void IsMnemonic_NoMatchReturnsFalse()
    {
        Assert.False(AsmTokens.IsMnemonic("XYZ"));
    }

    [Theory]
    [InlineData("LDA", "Load/Store")]
    [InlineData("STA", "Load/Store")]
    [InlineData("JMP", "Jump/Subroutine")]
    [InlineData("BNE", "Branch")]
    [InlineData("NOP", "System")]
    [InlineData("RTS", "Jump/Subroutine")]
    public void Mnemonics_SpotCheckKnownEntries(string mnemonic, string category)
    {
        var info = AsmTokens.Mnemonics[mnemonic];
        Assert.Equal(category, info.Category);
    }

    // ── Completion parity ────────────────────────────────────────────────────────
    // AsmCompletionProvider.AllItems is derived from AsmTokens.Mnemonics and AsmTokens.Directives,
    // so this can't drift by construction - kept as a regression guard against that derivation
    // breaking.

    [Fact]
    public void CompletionProvider_MnemonicAndDirectiveSetMatchesTables()
    {
        var tableEntries = AsmTokens.Mnemonics.Keys
            .Concat(AsmTokens.Directives.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completionEntries = AsmCompletionProvider.AllItems
            .Select(i => i.Text)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(tableEntries, completionEntries);
    }

    // ── Directives ────────────────────────────────────────────────────────────────

    [Fact]
    public void Directives_ContainsOrgByteTextWord()
    {
        var directiveNames = AsmTokens.Directives.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".org", ".byte", ".text", ".word" }, directiveNames);
    }

    [Fact]
    public void Directives_AllEntriesHaveNonEmptyDescriptionAndCategory()
    {
        foreach (var (directive, info) in AsmTokens.Directives)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.Snippet), $"{directive} has an empty snippet.");
            Assert.False(string.IsNullOrWhiteSpace(info.Description), $"{directive} has an empty description.");
            Assert.False(string.IsNullOrWhiteSpace(info.Category), $"{directive} has an empty category.");
        }
    }

    #endregion
}
