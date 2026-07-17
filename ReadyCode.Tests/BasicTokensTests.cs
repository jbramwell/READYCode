// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Editor;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="BasicTokens"/>, including the shared keyword-scanning logic used by the
/// tokenizer, syntax colorizers, hover tooltips, GOTO/GOSUB navigation, and the prettifier.
/// </summary>
public class BasicTokensTests
{
    #region Public Methods

    [Fact]
    public void TryMatchKeyword_LongestMatchWins()
    {
        // PRINT# (6 chars) must be preferred over PRINT (5 chars) at the same position.
        bool found = BasicTokens.TryMatchKeyword("PRINT#1,A", 0, BasicTokens.WordKeywordsLongestFirst, out string keyword);
        Assert.True(found);
        Assert.Equal("PRINT#", keyword);
    }

    [Fact]
    public void TryMatchKeyword_IsCaseInsensitive()
    {
        bool found = BasicTokens.TryMatchKeyword("goto10", 0, BasicTokens.WordKeywordsLongestFirst, out string keyword);
        Assert.True(found);
        Assert.Equal("GOTO", keyword);
    }

    [Fact]
    public void TryMatchKeyword_NoMatchReturnsFalse()
    {
        bool found = BasicTokens.TryMatchKeyword("XYZ", 0, BasicTokens.WordKeywordsLongestFirst, out string keyword);
        Assert.False(found);
        Assert.Equal("", keyword);
    }

    [Fact]
    public void TryMatchKeyword_RespectsStartPosition()
    {
        // "AGOTO10": no keyword starts at position 0 ("A" isn't one), but GOTO matches at position 1.
        Assert.False(BasicTokens.TryMatchKeyword("AGOTO10", 0, BasicTokens.WordKeywordsLongestFirst, out _));
        bool found = BasicTokens.TryMatchKeyword("AGOTO10", 1, BasicTokens.WordKeywordsLongestFirst, out string keyword);
        Assert.True(found);
        Assert.Equal("GOTO", keyword);
    }

    [Fact]
    public void WordKeywordsLongestFirst_ExcludesSingleCharacterOperators()
    {
        Assert.DoesNotContain("+", BasicTokens.WordKeywordsLongestFirst);
        Assert.DoesNotContain("=", BasicTokens.WordKeywordsLongestFirst);
    }

    [Fact]
    public void AllKeywordsLongestFirst_IncludesSingleCharacterOperators()
    {
        Assert.Contains("+", BasicTokens.AllKeywordsLongestFirst);
        Assert.Contains("=", BasicTokens.AllKeywordsLongestFirst);
    }

    // ── Keyword-set parity ───────────────────────────────────────────────────────
    // BasicCompletionProvider.AllItems is derived from BasicTokens.Keywords, so this can't
    // drift by construction - kept as a regression guard against that derivation breaking.

    [Fact]
    public void CompletionProvider_KeywordSetMatchesTokenMap()
    {
        var tokenKeywords = BasicTokens.WordKeywordsLongestFirst
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completionKeywords = BasicCompletionProvider.AllItems
            .Select(i => i.Text)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(tokenKeywords, completionKeywords);
    }

    // ── Keywords data integrity ──────────────────────────────────────────────────

    [Fact]
    public void Keywords_ContainsSeventySixEntries()
    {
        Assert.Equal(76, BasicTokens.Keywords.Count);
    }

    [Fact]
    public void Keywords_OperatorsHaveNoCompletionMetadata()
    {
        foreach (string op in new[] { "+", "-", "*", "/", "^", ">", "=", "<" })
        {
            var info = BasicTokens.Keywords[op];
            Assert.Null(info.Snippet);
            Assert.Null(info.Description);
            Assert.Null(info.Category);
        }
    }

    [Fact]
    public void Keywords_WordKeywordsAllHaveCompletionMetadata()
    {
        foreach (string keyword in BasicTokens.WordKeywordsLongestFirst)
        {
            var info = BasicTokens.Keywords[keyword];
            Assert.NotNull(info.Snippet);
            Assert.NotNull(info.Description);
            Assert.NotNull(info.Category);
        }
    }

    [Theory]
    [InlineData("MID$", 0xCA, "MID$(|,,)", "String Functions")]
    [InlineData("GO", 0xCB, "GO TO |", "Control Flow")]
    [InlineData("DEF", 0x96, "DEF FN |(|)=", "Variables & Data")]
    [InlineData("PRINT#", 0x98, "PRINT# |,", "Input & Output")]
    public void Keywords_SpotCheckKnownEntries(string keyword, byte token, string snippet, string category)
    {
        var info = BasicTokens.Keywords[keyword];
        Assert.Equal(token, info.Token);
        Assert.Equal(snippet, info.Snippet);
        Assert.Equal(category, info.Category);
    }

    #endregion
}
