// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text;

namespace ReadyCode.IO;

/// <summary>
/// Drop-in replacements for <see cref="File.WriteAllBytes(string, byte[])"/> and
/// <see cref="File.WriteAllText(string, string?, Encoding)"/> that don't leave a file
/// half-written if the app crashes or the machine loses power mid-save. The new content is
/// written and flushed to a temp file in the same folder first, then swapped into place in a
/// single step, so the file is always either entirely the old content or entirely the new.
/// </summary>
public static class AtomicFile
{
    #region Public Methods

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> with the given encoding
    /// (including its preamble/BOM, exactly as <see cref="File.WriteAllText(string, string?, Encoding)"/> would).
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="contents">The text to write.</param>
    /// <param name="encoding">The encoding to write it in.</param>
    public static void WriteAllText(string path, string contents, Encoding encoding) =>
        WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(contents)]);

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/>, replacing any existing file.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="bytes">The bytes to write.</param>
    /// <remarks>
    /// If the temp file can't be written (e.g. the disk is full) this throws and leaves the
    /// existing file untouched. If only the final swap fails - typically because another program
    /// (such as an emulator with a disk image attached) holds the file open in a way that allows
    /// writing but not replacing - it falls back to writing in place, as a plain
    /// <see cref="File.WriteAllBytes(string, byte[])"/> would, so that case still saves.
    /// </remarks>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        string fullPath = Path.GetFullPath(path);

        // Same folder, so the swap is a same-volume rename; per-process name so two running
        // instances can't collide. The .tmp extension keeps it out of project search.
        string tempPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $"{Path.GetFileName(fullPath)}.{Environment.ProcessId}.tmp");

        try
        {
            // Flushed through to the disk (not just the OS cache) before the swap, so a power
            // loss can't leave the swapped-in file empty or truncated.
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                // Replace (rather than delete + move) keeps the existing file's attributes,
                // permissions, and creation time.
                if (File.Exists(fullPath))
                    File.Replace(tempPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tempPath, fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Free the temp file's space first - an in-place write may need it.
                File.Delete(tempPath);
                File.WriteAllBytes(fullPath, bytes);
            }
        }
        finally
        {
            // Only still present if something above failed - don't leave it in the user's folder.
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    #endregion
}
