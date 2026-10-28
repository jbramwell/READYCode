// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Search;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="SearchPattern"/>. Plain-text matching behavior (case, whole word,
/// overlapping candidates) is covered through <see cref="ProjectSearcher.FindMatches"/> in
/// <see cref="ProjectSearcherTests"/>, which delegates here.
/// </summary>
public class SearchPatternTests
{
    #region Public Methods

    [Fact]
    public void TryCreate_EmptySearchText_ReturnsNullWithoutError()
    {
        var pattern = SearchPattern.TryCreate("", matchCase: true, wholeWord: false, useRegex: true, out string? error);

        Assert.Null(pattern);
        Assert.Null(error);
    }

    [Fact]
    public void TryCreate_InvalidRegex_ReturnsNullWithError()
    {
        var pattern = SearchPattern.TryCreate("[unclosed", matchCase: true, wholeWord: false, useRegex: true, out string? error);

        Assert.Null(pattern);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryCreate_InvalidRegexTextAsPlainSearch_IsValid()
    {
        // Only regex mode parses the text - the same characters are fine as a literal search.
        var pattern = SearchPattern.TryCreate("[unclosed", matchCase: true, wholeWord: false, useRegex: false, out string? error);

        Assert.NotNull(pattern);
        Assert.Null(error);
        Assert.Equal([(3, 9)], pattern.FindMatches("ARR[unclosed", out _));
    }

    [Fact]
    public void FindMatches_SamePatternAcrossTexts_MatchesEachIndependently()
    {
        var pattern = SearchPattern.TryCreate(@"GOTO \d+", matchCase: true, wholeWord: false, useRegex: true, out _)!;

        var first = pattern.FindMatches("10 GOTO 20", out bool firstTimedOut);
        var second = pattern.FindMatches("GOTO 5: GOTO 60", out bool secondTimedOut);

        Assert.Equal([(3, 7)], first);
        Assert.Equal([(0, 6), (8, 7)], second);
        Assert.False(firstTimedOut);
        Assert.False(secondTimedOut);
    }

    [Fact]
    public void FindMatches_RegexMatchCaseOff_IgnoresCase()
    {
        var pattern = SearchPattern.TryCreate("print", matchCase: false, wholeWord: false, useRegex: true, out _)!;

        Assert.Single(pattern.FindMatches("10 PRINT", out _));
    }

    [Fact]
    public void FindMatches_CatastrophicRegex_ReportsTimeoutWithNoMatches()
    {
        var pattern = SearchPattern.TryCreate("(X+X+)+Y", matchCase: true, wholeWord: false, useRegex: true, out _)!;

        var matches = pattern.FindMatches("XXY " + new string('X', 40), out bool timedOut);

        Assert.True(timedOut);
        Assert.Empty(matches);
    }

    #endregion
}
