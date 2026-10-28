// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text;
using ReadyCode.IO;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Search;

/// <summary>
/// Enumerates, reads, writes, and searches the searchable source files of an open project folder
/// for the project-wide Search panel; matching itself is <see cref="SearchPattern"/>, shared with
/// the single-tab Find/Replace bar so both use one matching algorithm.
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
            AtomicFile.WriteAllBytes(path, new PrgConverter().ConvertToPrg(text));
        else
            AtomicFile.WriteAllText(path, text, Encoding.UTF8);
    }

    /// <summary>
    /// Finds every occurrence of <paramref name="searchText"/> in <paramref name="text"/>, either
    /// as a regular expression or a plain substring search - a one-off convenience over
    /// <see cref="SearchPattern"/>, which callers searching many texts or needing to know why
    /// nothing matched should use directly.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="searchText">The text or pattern to search for.</param>
    /// <param name="matchCase">Whether the search is case-sensitive.</param>
    /// <param name="wholeWord">
    /// For a plain (non-regex) search, whether a match must not be adjacent to another word
    /// character on either side.
    /// </param>
    /// <param name="useRegex">Whether <paramref name="searchText"/> is a regular expression.</param>
    /// <returns>
    /// Each match's offset and length, in document order. Empty if the pattern is invalid, takes
    /// too long to evaluate, or has no matches.
    /// </returns>
    public static List<(int Offset, int Length)> FindMatches(string text, string searchText, bool matchCase, bool wholeWord, bool useRegex) =>
        SearchPattern.TryCreate(searchText, matchCase, wholeWord, useRegex, out _)?.FindMatches(text, out _) ?? [];

    /// <summary>
    /// Searches every searchable file under <paramref name="rootPath"/> for
    /// <paramref name="pattern"/>. Touches no UI state, so it can run on a background thread.
    /// </summary>
    /// <param name="rootPath">The project's root folder.</param>
    /// <param name="pattern">The prepared search.</param>
    /// <param name="prepareText">
    /// Adjusts a file's text before it's searched, given its path and text - e.g. padding a
    /// detokenized .prg's line numbers the way the editor does on opening one, so a result's
    /// line/column lines up with the opened tab.
    /// </param>
    /// <param name="cancellationToken">Stops the search between files.</param>
    /// <returns>The files with matches, in search order, plus totals.</returns>
    public static ProjectSearchOutcome SearchProject(string rootPath, SearchPattern pattern, Func<string, string, string> prepareText, CancellationToken cancellationToken)
    {
        var files = new List<ProjectSearchFileResult>();
        int totalMatches = 0;
        int timedOutFiles = 0;

        foreach (string path in EnumerateSearchableFiles(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? text = ReadSearchableText(path);
            if (text == null) continue;
            text = prepareText(path, text);

            var matches = pattern.FindMatches(text, out bool timedOut);
            if (timedOut) timedOutFiles++;
            if (matches.Count == 0) continue;

            var lineStarts = ComputeLineStartOffsets(text);
            var fileResult = new ProjectSearchFileResult(path, Path.GetRelativePath(rootPath, path));
            foreach (var (offset, length) in matches)
            {
                var (lineNumber, columnOffset, preview) = LocateMatch(text, lineStarts, offset);
                fileResult.Matches.Add(new ProjectSearchMatchInfo(fileResult, lineNumber, columnOffset, length, preview));
            }
            files.Add(fileResult);
            totalMatches += matches.Count;
        }

        return new ProjectSearchOutcome(files, totalMatches, timedOutFiles);
    }

    /// <summary>
    /// Gets the 0-based offset of each line's first character, used to turn a match's absolute
    /// offset into a 1-based line number + in-line column without an O(n) scan per match.
    /// </summary>
    /// <param name="text">The text to index.</param>
    public static List<int> ComputeLineStartOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') offsets.Add(i + 1);
        return offsets;
    }

    /// <summary>
    /// Locates an absolute offset within <paramref name="text"/> as a line and column.
    /// </summary>
    /// <param name="text">The text containing the offset.</param>
    /// <param name="lineStartOffsets">The text's line start offsets (see <see cref="ComputeLineStartOffsets"/>).</param>
    /// <param name="offset">The absolute offset to locate.</param>
    /// <returns>The 1-based line number, the 0-based column within it, and the line's trimmed text.</returns>
    public static (int LineNumber, int ColumnOffset, string LinePreview) LocateMatch(string text, List<int> lineStartOffsets, int offset)
    {
        int idx = lineStartOffsets.BinarySearch(offset);
        if (idx < 0) idx = ~idx - 1;
        int lineStart = lineStartOffsets[idx];
        int lineEnd = idx + 1 < lineStartOffsets.Count ? lineStartOffsets[idx + 1] - 1 : text.Length;
        if (lineEnd < lineStart) lineEnd = lineStart;
        string lineText = text.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
        return (idx + 1, offset - lineStart, lineText.Trim());
    }

    #endregion
}

/// <summary>
/// The result of <see cref="ProjectSearcher.SearchProject"/>.
/// </summary>
/// <param name="Files">Each file with at least one match.</param>
/// <param name="TotalMatches">The number of matches across all files.</param>
/// <param name="TimedOutFiles">
/// How many files were skipped because the regular expression took too long to evaluate on them.
/// </param>
public sealed record ProjectSearchOutcome(IReadOnlyList<ProjectSearchFileResult> Files, int TotalMatches, int TimedOutFiles);
