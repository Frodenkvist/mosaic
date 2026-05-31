using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Mosaic.Services;

/// <summary>A SteamGridDB game search result.</summary>
public record SgdbGame(long Id, string Name);

/// <summary>Thin client over the SteamGridDB v2 HTTP API with simple 429 back-off.</summary>
public class SteamGridDbClient
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2/";
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;

    public SteamGridDbClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>True if the API key is accepted (HTTP authorized) by SteamGridDB.</summary>
    public async Task<bool> ValidateKeyAsync(string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}search/autocomplete/portal");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await _http.SendAsync(request, ct);
            return response.StatusCode != HttpStatusCode.Unauthorized
                && response.StatusCode != HttpStatusCode.Forbidden
                && response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Returns the SteamGridDB autocomplete matches (id + name) for a term.</summary>
    public async Task<IReadOnlyList<SgdbGame>> SearchGamesAsync(string name, string apiKey, CancellationToken ct)
    {
        var url = $"{BaseUrl}search/autocomplete/{Uri.EscapeDataString(name)}";
        var result = await GetAsync<ListResponse<GameDto>>(url, apiKey, ct);
        if (result?.Data is null)
            return Array.Empty<SgdbGame>();
        return result.Data
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new SgdbGame(g.Id, g.Name!))
            .ToList();
    }

    /// <summary>Returns the URL of the first asset of a kind for a game, or null.</summary>
    public async Task<string?> GetFirstAssetUrlAsync(string endpoint, long gameId, string apiKey, CancellationToken ct)
    {
        var url = $"{BaseUrl}{endpoint}/game/{gameId}";
        var result = await GetAsync<ListResponse<AssetDto>>(url, apiKey, ct);
        return result?.Data?.FirstOrDefault()?.Url;
    }

    public async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        using var response = await SendWithBackoffAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (response is null || !response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<T?> GetAsync<T>(string url, string apiKey, CancellationToken ct) where T : class
    {
        using var response = await SendWithBackoffAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return req;
        }, ct);

        if (response is null || !response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private async Task<HttpResponseMessage?> SendWithBackoffAsync(
        Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = requestFactory();
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException)
            {
                return null;
            }

            if (response.StatusCode != HttpStatusCode.TooManyRequests)
                return response;

            var delay = response.Headers.RetryAfter?.Delta ?? DefaultBackoff * attempt;
            response.Dispose();
            if (attempt == maxAttempts)
                return null;
            await Task.Delay(delay, ct);
        }
        return null;
    }

    private class ListResponse<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("data")] public List<T>? Data { get; set; }
    }

    private class GameDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class AssetDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
