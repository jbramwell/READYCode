// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace ReadyCode.Search;

/// <summary>
/// A search query - plain text or a regular expression, plus its match-case/whole-word options -
/// prepared once and then run against any number of texts. Shared by the single-tab Find bar and
/// the project-wide Search panel so both use one matching algorithm, and so a project search
/// compiles its regex once rather than once per file. Unlike a bare match list, it also says
/// why a search found nothing: an invalid pattern (<see cref="TryCreate"/>) or one that took too
/// long to evaluate (<see cref="FindMatches"/>).
/// </summary>
public sealed class SearchPattern
{
    #region Private Fields

    // Upper bound on a single regex match attempt. The pattern is user-typed, so a
    // catastrophically backtracking one (e.g. "(a+)+$") would otherwise run effectively forever -
    // far beyond anything a legitimate search of a source file needs.
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly string _searchText;
    private readonly StringComparison _comparison;
    private readonly bool _wholeWord;
    private readonly Regex? _regex;

    #endregion

    #region Constructors

    private SearchPattern(string searchText, bool matchCase, bool wholeWord, Regex? regex)
    {
        _searchText = searchText;
        _comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        _wholeWord = wholeWord;
        _regex = regex;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Prepares a search for <paramref name="searchText"/>.
    /// </summary>
    /// <param name="searchText">The text or pattern to search for.</param>
    /// <param name="matchCase">Whether the search is case-sensitive.</param>
    /// <param name="wholeWord">
    /// For a plain (non-regex) search, whether a match must not be adjacent to another word
    /// character on either side.
    /// </param>
    /// <param name="useRegex">Whether <paramref name="searchText"/> is a regular expression.</param>
    /// <param name="error">Why the regular expression is invalid, when that's why this returns null.</param>
    /// <returns>
    /// The prepared search, or <c>null</c> if <paramref name="searchText"/> is empty or (with
    /// <paramref name="error"/> set) not a valid regular expression.
    /// </returns>
    public static SearchPattern? TryCreate(string searchText, bool matchCase, bool wholeWord, bool useRegex, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(searchText)) return null;

        Regex? regex = null;
        if (useRegex)
        {
            try
            {
                var options = (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.Multiline;
                regex = new Regex(searchText, options, RegexMatchTimeout);
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        return new SearchPattern(searchText, matchCase, wholeWord, regex);
    }

    /// <summary>
    /// Finds every match in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="timedOut">
    /// Whether a regular expression search was abandoned for taking too long (see
    /// <see cref="RegexMatchTimeout"/>) - in which case the result is empty rather than partial.
    /// </param>
    /// <returns>Each match's offset and length, in document order.</returns>
    public List<(int Offset, int Length)> FindMatches(string text, out bool timedOut)
    {
        timedOut = false;
        var matches = new List<(int Offset, int Length)>();

        if (_regex != null)
        {
            try
            {
                foreach (Match m in _regex.Matches(text))
                    if (m.Length > 0) matches.Add((m.Index, m.Length));
            }
            catch (RegexMatchTimeoutException)
            {
                // Matches are enumerated lazily, so some may already have been added - drop them
                // rather than report a partial result as if it were complete.
                matches.Clear();
                timedOut = true;
            }
            return matches;
        }

        int idx = 0;
        while (idx < text.Length)
        {
            int pos = text.IndexOf(_searchText, idx, _comparison);
            if (pos < 0) break;

            if (_wholeWord)
            {
                bool okStart = pos == 0 || !IsWordChar(text[pos - 1]);
                bool okEnd = pos + _searchText.Length >= text.Length || !IsWordChar(text[pos + _searchText.Length]);
                if (!okStart || !okEnd) { idx = pos + 1; continue; }
            }

            matches.Add((pos, _searchText.Length));
            idx = pos + 1;
        }

        return matches;
    }

    #endregion

    #region Private Methods

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion
}
