// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using FluentFTP;

namespace ReadyCode.C64U;

/// <summary>
/// Client for browsing and managing files on the Commodore 64 Ultimate's FTP file service.
/// </summary>
public class C64UFtpClient : IDisposable
{
    #region Private Fields

    private AsyncFtpClient? _client;

    #endregion

    #region Public Methods

    /// <summary>
    /// Connects to the C64 Ultimate's FTP server on the given host, using its default
    /// "admin" account with a blank password.
    /// </summary>
    /// <param name="host">The C64 Ultimate's host name or IP address (no scheme or path).</param>
    public async Task ConnectAsync(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("The C64 Ultimate URL has not been configured. Set it in Settings - Preferences.");

        var client = new AsyncFtpClient(host, new NetworkCredential("admin", ""), 21, null, null);

        try
        {
            await client.Connect();
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new InvalidOperationException($"Could not connect to the C64 Ultimate at '{host}': {ex.Message}", ex);
        }

        _client = client;
    }

    /// <summary>
    /// Lists the immediate children of the given remote directory.
    /// </summary>
    /// <param name="path">The full remote directory path.</param>
    /// <returns>The child files and folders, folders first, then alphabetically by name.</returns>
    public async Task<List<(string Name, string FullPath, bool IsFolder, long Size)>> ListDirectoryAsync(string path)
    {
        EnsureConnected();
        var items = await _client!.GetListing(path);

        return items
            .Where(i => i.Type == FtpObjectType.File || i.Type == FtpObjectType.Directory)
            .Select(i => (i.Name, i.FullName, i.Type == FtpObjectType.Directory, i.Size))
            .OrderBy(i => !i.Item3)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Downloads a remote file's contents as a byte array.
    /// </summary>
    /// <param name="path">The full remote file path.</param>
    public async Task<byte[]> DownloadBytesAsync(string path)
    {
        EnsureConnected();
        return await _client!.DownloadBytes(path, CancellationToken.None);
    }

    /// <summary>
    /// Uploads a byte array to the server, overwriting any existing file at that path.
    /// </summary>
    /// <param name="path">The full remote file path to write to.</param>
    /// <param name="data">The file contents to upload.</param>
    public async Task UploadBytesAsync(string path, byte[] data)
    {
        EnsureConnected();
        await _client!.UploadBytes(data, path, FtpRemoteExists.Overwrite);
    }

    /// <summary>
    /// Creates a new remote directory.
    /// </summary>
    /// <param name="path">The full remote directory path to create.</param>
    public async Task CreateFolderAsync(string path)
    {
        EnsureConnected();
        await _client!.CreateDirectory(path);
    }

    /// <summary>
    /// Deletes a remote file.
    /// </summary>
    /// <param name="path">The full remote file path to delete.</param>
    public async Task DeleteFileAsync(string path)
    {
        EnsureConnected();
        await _client!.DeleteFile(path);
    }

    /// <summary>
    /// Deletes a remote directory and all of its contents.
    /// </summary>
    /// <param name="path">The full remote directory path to delete.</param>
    public async Task DeleteFolderAsync(string path)
    {
        EnsureConnected();
        await _client!.DeleteDirectory(path);
    }

    /// <summary>
    /// Renames or moves a remote file or directory.
    /// </summary>
    /// <param name="oldPath">The current full remote path.</param>
    /// <param name="newPath">The new full remote path.</param>
    public async Task RenameAsync(string oldPath, string newPath)
    {
        EnsureConnected();
        await _client!.Rename(oldPath, newPath);
    }

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_client == null) return;
        await _client.Disconnect();
    }

    /// <summary>
    /// Disconnects and releases the underlying FTP connection.
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Private Methods

    private void EnsureConnected()
    {
        if (_client == null || !_client.IsConnected)
            throw new InvalidOperationException("Not connected to the C64 Ultimate's FTP server.");
    }

    #endregion
}
