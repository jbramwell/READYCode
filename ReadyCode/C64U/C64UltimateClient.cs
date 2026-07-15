// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ReadyCode.C64U;

/// <summary>
/// Client for the Commodore 64 Ultimate's REST API.
/// </summary>
public class C64UltimateClient
{
    #region Private Fields

    private static readonly HttpClient _httpClient = new();

    #endregion

    #region Public Methods

    /// <summary>
    /// Uploads a tokenized BASIC program and runs it via POST /v1/runners:load_prg.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="prgData">The PRG-format program data to upload.</param>
    /// <returns>The response body returned by the device.</returns>
    public async Task<string> LoadPrgAsync(string baseUrl, byte[] prgData)
    {
        var endpoint = BuildEndpointUri(baseUrl, "v1/runners:load_prg");

        using var content = new ByteArrayContent(prgData);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _httpClient.PostAsync(endpoint, content);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

        return body;
    }

    /// <summary>
    /// Uploads a tokenized BASIC program and runs it immediately via POST /v1/runners:run_prg.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="prgData">The PRG-format program data to upload.</param>
    /// <returns>The response body returned by the device.</returns>
    public async Task<string> RunPrgAsync(string baseUrl, byte[] prgData)
    {
        var endpoint = BuildEndpointUri(baseUrl, "v1/runners:run_prg");

        using var content = new ByteArrayContent(prgData);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _httpClient.PostAsync(endpoint, content);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

        return body;
    }

    /// <summary>
    /// Retrieves basic device information via GET /v1/info.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <returns>The device information reported by the C64 Ultimate.</returns>
    public async Task<C64UInfo> GetInfoAsync(string baseUrl)
    {
        var endpoint = BuildEndpointUri(baseUrl, "v1/info");

        using var response = await _httpClient.GetAsync(endpoint);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

        return JsonSerializer.Deserialize<C64UInfo>(body)
            ?? throw new InvalidOperationException("The C64 Ultimate returned an empty response.");
    }

    /// <summary>
    /// Sends a machine control command via PUT /v1/machine:{action} (reset, reboot, pause, resume, poweroff).
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="action">The machine action to perform.</param>
    public async Task MachineActionAsync(string baseUrl, string action)
    {
        var endpoint = BuildEndpointUri(baseUrl, $"v1/machine:{action}");

        using var response = await _httpClient.PutAsync(endpoint, null);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    /// <summary>
    /// Retrieves the status of all drives via GET /v1/drives.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <returns>The status of each drive reported by the device.</returns>
    public async Task<List<C64UDriveStatus>> GetDrivesAsync(string baseUrl)
    {
        var endpoint = BuildEndpointUri(baseUrl, "v1/drives");

        using var response = await _httpClient.GetAsync(endpoint);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

        var drives = new List<C64UDriveStatus>();
        using var doc = JsonDocument.Parse(body);

        // Each element of the "drives" array is a single-property object whose property name
        // is the drive id (e.g. "a", "b", "IEC Drive") and whose value holds that drive's fields.
        if (doc.RootElement.TryGetProperty("drives", out var drivesArray))
        {
            foreach (var entry in drivesArray.EnumerateArray())
            {
                foreach (var drive in entry.EnumerateObject())
                {
                    drives.Add(new C64UDriveStatus
                    {
                        Id = drive.Name,
                        Enabled = drive.Value.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean(),
                        Type = drive.Value.TryGetProperty("type", out var type) ? type.GetString() : null,
                        ImageFile = drive.Value.TryGetProperty("image_file", out var imageFile) ? imageFile.GetString() ?? "" : "",
                    });
                }
            }
        }

        return drives;
    }

    /// <summary>
    /// Mounts a disk image already on the device's storage to the given drive via
    /// PUT /v1/drives/{driveId}:mount.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="driveId">The drive to mount to (e.g. "a", "b").</param>
    /// <param name="imagePath">The full path of the disk image on the device, as returned by the FTP explorer.</param>
    public async Task MountDriveAsync(string baseUrl, string driveId, string imagePath)
    {
        var endpoint = BuildEndpointUri(baseUrl, $"v1/drives/{driveId}:mount?image={Uri.EscapeDataString(imagePath)}");

        using var response = await _httpClient.PutAsync(endpoint, null);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    /// <summary>
    /// Ejects the disk image currently mounted on the given drive via PUT /v1/drives/{driveId}:remove.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="driveId">The drive to eject (e.g. "a", "b").</param>
    public async Task RemoveDriveAsync(string baseUrl, string driveId)
    {
        var endpoint = BuildEndpointUri(baseUrl, $"v1/drives/{driveId}:remove");

        using var response = await _httpClient.PutAsync(endpoint, null);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    #endregion

    #region Private Methods

    private static Uri BuildEndpointUri(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("The C64 Ultimate URL has not been configured. Set it in Settings - Preferences.");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"'{baseUrl}' is not a valid URL.");

        // Ensure the base URI is treated as a directory so the endpoint path is appended, not replaced.
        if (!baseUri.AbsoluteUri.EndsWith('/'))
            baseUri = new Uri(baseUri.AbsoluteUri + "/");

        return new Uri(baseUri, path);
    }

    #endregion
}
