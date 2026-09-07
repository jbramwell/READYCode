// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ReadyCode.Tokenizer;

namespace ReadyCode.Search;

/// <summary>
/// Finds text matches within a single document's text, and enumerates and reads the searchable
/// source files of an open project folder - shared by the single-tab Find/Replace bar and the
/// project-wide Search panel so both use one matching algorithm.
/// </summary>
public static class ProjectSearcher
{
    #region Private Fields

    // .prg is included - it's tokenized BASIC on disk, detokenized to searchable text by
    // ReadSearchableText/re-tokenized by WriteSearchableText, same round trip the editor already
    // does when opening/saving one.
    private static readonly string[] SearchableExtensions = [".bas", ".asm", ".s", ".txt", ".prg"];

    #endregion

    #region Public Methods

    /// <summary>
    /// Recursively enumerates every searchable source file under <paramref name="rootPath"/> (see
    /// <see cref="SearchableExtensions"/>). Tolerates locked or inaccessible subfolders by
    /// skipping them rather than aborting the whole walk.
    /// </summary>
    /// <param name="rootPath">The project's root folder.</param>
    public static IEnumerable<string> EnumerateSearchableFiles(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var d in subdirs) pending.Push(d);

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }
            foreach (var f in files)
                if (SearchableExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    yield return f;
        }
    }

    /// <summary>
    /// Reads a searchable file's text content: detokenizes a .prg's BASIC bytes back to source
    /// text (the same conversion the editor performs when opening one), or reads plain text
    /// source directly otherwise.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>
    /// The file's text content, or null if it couldn't be read, or - for a .prg - isn't a
    /// well-formed BASIC program (e.g. machine language), which can't be shown/edited as text.
    /// </returns>
    public static string? ReadSearchableText(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".prg", StringComparison.OrdinalIgnoreCase))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch { return null; }

            var converter = new PrgConverter();
            return converter.IsBasicProgram(bytes) ? converter.ConvertFromPrg(bytes) : null;
        }

        try { return File.ReadAllText(path, Encoding.UTF8); }
        catch { return null; }
    }

    /// <summary>
    /// Writes updated text content back to a searchable file: re-tokenizes to .prg bytes for a
    /// .prg path (the same conversion the editor performs when saving one), or writes plain text
    /// directly otherwise.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="text">The updated text content.</param>
    public static void WriteSearchableText(string path, string text)
    {
        if (string.Equals(Path.GetExtension(path), ".prg", StringComparison.OrdinalIgnoreCase))
            File.WriteAllBytes(path, new PrgConverter().ConvertToPrg(text));
        else
            File.WriteAllText(path, text, Encoding.UTF8);
    }

    /// <summary>
    /// Finds every occurrence of <paramref name="searchText"/> in <paramref name="text"/>, either
    /// as a regular expression or a plain substring search.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="searchText">The text or pattern to search for.</param>
    /// <param name="matchCase">Whether the search is case-sensitive.</param>
    /// <param name="wholeWord">
    /// For a plain (non-regex) search, whether a match must not be adjacent to another word
    /// character on either side.
    /// </param>
    /// <param name="useRegex">Whether <paramref name="searchText"/> is a regular expression.</param>
    /// <returns>Each match's offset and length, in document order. Empty if the pattern is invalid or has no matches.</returns>
    public static List<(int Offset, int Length)> FindMatches(string text, string searchText, bool matchCase, bool wholeWord, bool useRegex)
    {
        var matches = new List<(int Offset, int Length)>();
        if (string.IsNullOrEmpty(searchText)) return matches;

        if (useRegex)
        {
            try
            {
                var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                foreach (Match m in new Regex(searchText, options | RegexOptions.Multiline).Matches(text))
                    if (m.Length > 0) matches.Add((m.Index, m.Length));
            }
            catch { /* invalid regex - no matches */ }
        }
        else
        {
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int idx = 0;
            while (idx < text.Length)
            {
                int pos = text.IndexOf(searchText, idx, comparison);
                if (pos < 0) break;

                if (wholeWord)
                {
                    bool okStart = pos == 0 || !IsWordChar(text[pos - 1]);
                    bool okEnd = pos + searchText.Length >= text.Length || !IsWordChar(text[pos + searchText.Length]);
                    if (!okStart || !okEnd) { idx = pos + 1; continue; }
                }

                matches.Add((pos, searchText.Length));
                idx = pos + 1;
            }
        }

        return matches;
    }

    #endregion

    #region Private Methods

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion
}
