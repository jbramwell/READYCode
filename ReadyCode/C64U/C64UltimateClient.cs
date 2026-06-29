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
