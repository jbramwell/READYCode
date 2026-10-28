// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text;
using System.Text.Json;

namespace ReadyCode.Settings;

/// <summary>
/// Crash-safe load/save of a JSON settings file, shared by <see cref="AppSettings"/> and
/// <see cref="DebugConfigStore"/>. A save never leaves a half-written file behind: the new
/// content is written and flushed to a temp file first, the previous good file is kept as
/// <c>.bak</c>, and only then is the temp file swapped into place. A load falls back to that
/// <c>.bak</c> if the main file is missing or unreadable, so a damaged file doesn't silently
/// reset everything to defaults.
/// </summary>
public static class JsonSettingsFile
{
    #region Private Fields

    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    // Serializes saves within this process. The temp file name is also per-process, so two
    // running instances saving at once can't trample each other's temp file.
    private static readonly object _saveLock = new();

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads <typeparamref name="T"/> from <paramref name="path"/>, falling back to its
    /// <c>.bak</c> backup if the main file is missing, unreadable, or not valid JSON.
    /// </summary>
    /// <typeparam name="T">The settings type to deserialize.</typeparam>
    /// <param name="path">The settings file path.</param>
    /// <param name="onReadError">
    /// Called with the exception and a short description for each file that exists but couldn't
    /// be read - e.g. <c>ErrorLog.Write</c> - so a damaged file leaves a trace.
    /// </param>
    /// <returns>The loaded value, or <c>null</c> if neither file could be loaded.</returns>
    public static T? Load<T>(string path, Action<Exception, string>? onReadError = null) where T : class
    {
        foreach (string candidate in new[] { path, GetBackupPath(path) })
        {
            if (!File.Exists(candidate)) continue;

            try
            {
                var value = JsonSerializer.Deserialize<T>(File.ReadAllText(candidate));
                if (value != null) return value;
            }
            catch (Exception ex)
            {
                onReadError?.Invoke(ex, $"Reading {Path.GetFileName(candidate)}");
            }
        }

        return null;
    }

    /// <summary>
    /// Saves <paramref name="value"/> to <paramref name="path"/> as indented JSON, replacing the
    /// file only once the new content is fully on disk and keeping the previous file as
    /// <c>.bak</c>.
    /// </summary>
    /// <typeparam name="T">The settings type to serialize.</typeparam>
    /// <param name="path">The settings file path.</param>
    /// <param name="value">The value to save.</param>
    public static void Save<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, _writeOptions);

        lock (_saveLock)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = $"{path}.{Environment.ProcessId}.tmp";
            try
            {
                // Flushed through to the disk (not just the OS cache) before the swap below, so
                // a power loss can't leave the swapped-in file empty or truncated.
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json));
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                    File.Copy(path, GetBackupPath(path), overwrite: true);

                // A same-volume move with overwrite is a single atomic rename, so the file at
                // path is always either the complete old content or the complete new content.
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                // Only still present if something above failed - don't leave it lying around.
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }

    /// <summary>Gets the backup file path kept alongside <paramref name="path"/>.</summary>
    /// <param name="path">The settings file path.</param>
    public static string GetBackupPath(string path) => path + ".bak";

    #endregion
}
