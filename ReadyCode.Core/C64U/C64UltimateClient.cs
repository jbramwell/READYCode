// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ReadyCode.C64U;

/// <summary>
/// Client for the Commodore 64 Ultimate's REST API.
/// </summary>
public class C64UltimateClient
{
    #region Private Fields

    private static readonly HttpClient _httpClient = new();

    // GET /v1/machine:readmem's length cap isn't documented, so reads are split into chunks
    // this size regardless of the requested range, rather than risk an oversized single request
    // failing or timing out on the device.
    private const int _maxReadMemoryChunk = 4096;

    // Standard C64 KERNAL zero-page addresses for the keyboard input buffer - see TypeAsync.
    private const ushort _keyboardBufferAddress = 0x0277;
    private const ushort _keyboardBufferLengthAddress = 0x00C6;

    #endregion

    #region Public Methods

    /// <summary>
    /// Uploads a tokenized BASIC program without running it via POST /v1/runners:load_prg - the
    /// user (or calling code) can start it afterward with a typed RUN, or with
    /// <see cref="TypeAsync"/>. Contrast <see cref="RunPrgAsync"/>, which runs it immediately.
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
    /// Reads a range of machine memory via GET /v1/machine:readmem, a live DMA read on the
    /// cartridge bus reflecting whatever's currently banked in - there is no bank-selection
    /// parameter, unlike VICE's binary monitor. Large ranges are read in multiple chunked
    /// requests rather than one, since the endpoint's maximum length isn't documented.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="address">The starting memory address to read from.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>The raw bytes read from memory.</returns>
    public async Task<byte[]> ReadMemoryAsync(string baseUrl, ushort address, int length)
    {
        var result = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int chunkLength = Math.Min(_maxReadMemoryChunk, length - offset);
            int chunkAddress = address + offset;
            var endpoint = BuildEndpointUri(baseUrl, $"v1/machine:readmem?address={chunkAddress:X4}&length={chunkLength}");

            using var response = await _httpClient.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }

            byte[] chunk = await response.Content.ReadAsByteArrayAsync();
            if (chunk.Length != chunkLength)
                throw new InvalidOperationException($"Expected {chunkLength} bytes from address ${chunkAddress:X4} but received {chunk.Length}.");

            Array.Copy(chunk, 0, result, offset, chunkLength);
            offset += chunkLength;
        }

        return result;
    }

    /// <summary>
    /// Writes bytes directly to machine memory via PUT /v1/machine:writemem. Limited to 128 bytes
    /// per call, the endpoint's documented cap for this URL-parameter (as opposed to binary
    /// attachment) form.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="address">The memory address to start writing at.</param>
    /// <param name="data">The bytes to write.</param>
    public async Task WriteMemoryAsync(string baseUrl, ushort address, byte[] data)
    {
        if (data.Length > 128)
            throw new ArgumentException("writemem accepts at most 128 bytes per call.", nameof(data));

        string hex = Convert.ToHexString(data);
        var endpoint = BuildEndpointUri(baseUrl, $"v1/machine:writemem?address={address:X4}&data={hex}");

        using var response = await _httpClient.PutAsync(endpoint, null);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The C64 Ultimate returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    /// <summary>
    /// Simulates typing on the C64's keyboard by stuffing the given text directly into the
    /// KERNAL's keyboard input buffer, as if a user had typed it - the standard trick for issuing
    /// an immediate-mode BASIC command (e.g. "SYS49152\r") right after a DMA load, since a
    /// standalone (no BASIC loader stub) program's origin can't otherwise be reached through the
    /// load/run REST endpoints alone. Limited to 10 characters, the real keyboard buffer's fixed
    /// size ($0277-$0280).
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="text">
    /// The characters to "type" - typically ending in "\r" (Enter) so the command executes
    /// immediately rather than just sitting on the input line unsubmitted.
    /// </param>
    public async Task TypeAsync(string baseUrl, string text)
    {
        if (text.Length > 10)
            throw new ArgumentException("The C64 keyboard buffer holds at most 10 characters.", nameof(text));

        byte[] bytes = Encoding.ASCII.GetBytes(text);
        await WriteMemoryAsync(baseUrl, _keyboardBufferAddress, bytes);
        await WriteMemoryAsync(baseUrl, _keyboardBufferLengthAddress, [(byte)bytes.Length]);
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
