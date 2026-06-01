using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Mosaic.Services;

/// <summary>A Steam application (id + name) candidate for achievement linking.</summary>
public record SteamApp(int AppId, string Name);

/// <summary>One achievement definition from a game's Steam schema.</summary>
public record SteamAchievementDef(
    string ApiName,
    string DisplayName,
    string? Description,
    bool Hidden,
    string? IconUrl,
    string? IconGrayUrl);

/// <summary>
/// Thin client over the public Steam Web API used for achievements: app name search (no key) and
/// the per-game achievement schema (requires the user's Steam Web API key). Structurally mirrors
/// <see cref="SteamGridDbClient"/> — simple GET with 429 back-off — but the key is passed as a query
/// parameter rather than a bearer header, as the Steam Web API expects.
/// </summary>
public class SteamWebApiClient
{
    private const string ApiBase = "https://api.steampowered.com/";
    private const string SearchAppsBase = "https://steamcommunity.com/actions/SearchApps/";
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;

    public SteamWebApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Searches Steam's app list by name (no API key required) and returns id+name candidates,
    /// best first as Steam orders them. Used to propose an appid for a game.
    /// </summary>
    public async Task<IReadOnlyList<SteamApp>> SearchAppsAsync(string term, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Array.Empty<SteamApp>();

        var url = $"{SearchAppsBase}{Uri.EscapeDataString(term)}";
        var result = await GetAsync<List<SearchAppDto>>(url, ct);
        if (result is null)
            return Array.Empty<SteamApp>();

        return result
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && int.TryParse(a.AppId, out _))
            .Select(a => new SteamApp(int.Parse(a.AppId!), a.Name!))
            .ToList();
    }

    /// <summary>
    /// Returns the achievement definitions for an appid from the Steam Web API, or an empty list
    /// when the game has no achievements. Returns null when the request fails (e.g. a rejected key
    /// or network error) so callers can distinguish "no achievements" from "could not resolve".
    /// </summary>
    public async Task<IReadOnlyList<SteamAchievementDef>?> GetSchemaForGameAsync(int appId, string apiKey, CancellationToken ct)
    {
        var url = $"{ApiBase}ISteamUserStats/GetSchemaForGame/v2/?key={Uri.EscapeDataString(apiKey)}&appid={appId}&l=english";
        var result = await GetAsync<SchemaResponse>(url, ct);
        if (result is null)
            return null;

        var achievements = result.Game?.AvailableGameStats?.Achievements;
        if (achievements is null)
            return Array.Empty<SteamAchievementDef>();

        return achievements
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new SteamAchievementDef(
                a.Name!,
                string.IsNullOrWhiteSpace(a.DisplayName) ? a.Name! : a.DisplayName!,
                a.Description,
                a.Hidden != 0,
                a.Icon,
                a.IconGray))
            .ToList();
    }

    public async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        using var response = await SendWithBackoffAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (response is null || !response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var response = await SendWithBackoffAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
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

    private class SearchAppDto
    {
        [JsonPropertyName("appid")] public string? AppId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class SchemaResponse
    {
        [JsonPropertyName("game")] public SchemaGame? Game { get; set; }
    }

    private class SchemaGame
    {
        [JsonPropertyName("availableGameStats")] public AvailableGameStats? AvailableGameStats { get; set; }
    }

    private class AvailableGameStats
    {
        [JsonPropertyName("achievements")] public List<SchemaAchievement>? Achievements { get; set; }
    }

    private class SchemaAchievement
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("hidden")] public int Hidden { get; set; }
        [JsonPropertyName("icon")] public string? Icon { get; set; }
        [JsonPropertyName("icongray")] public string? IconGray { get; set; }
    }
}
