using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Mosaic.Services;

/// <summary>A TMDB movie or TV match. <see cref="OriginalTitle"/> is the untranslated title
/// (e.g. an anime's romanized/native name), which often differs from the localized <see cref="Title"/>.</summary>
public record TmdbMatch(int Id, string Title, string? OriginalTitle, int? Year, string? PosterPath, string? BackdropPath, string? Overview);

/// <summary>One episode from a TMDB season.</summary>
public record TmdbEpisode(int EpisodeNumber, string? Name, string? StillPath);

/// <summary>
/// Thin client over the TMDB v3 HTTP API (movie/TV search, season episodes, image download),
/// authenticated with a user-supplied v3 API key. Sibling of <see cref="SteamGridDbClient"/>.
/// </summary>
public class TmdbClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3/";
    private const string ImageBase = "https://image.tmdb.org/t/p/";
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;

    public TmdbClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Searches movies by title (and optional year); best-ranked first.</summary>
    public async Task<IReadOnlyList<TmdbMatch>> SearchMoviesAsync(string title, int? year, string apiKey, CancellationToken ct)
    {
        var url = $"{BaseUrl}search/movie?api_key={apiKey}&query={Uri.EscapeDataString(title)}"
                  + (year is int y ? $"&year={y}" : string.Empty);
        var result = await GetAsync<SearchResponse>(url, ct);
        return MapResults(result, isTv: false);
    }

    /// <summary>Searches TV series by title (and optional first-air year); best-ranked first.</summary>
    public async Task<IReadOnlyList<TmdbMatch>> SearchTvAsync(string title, int? year, string apiKey, CancellationToken ct)
    {
        var url = $"{BaseUrl}search/tv?api_key={apiKey}&query={Uri.EscapeDataString(title)}"
                  + (year is int y ? $"&first_air_date_year={y}" : string.Empty);
        var result = await GetAsync<SearchResponse>(url, ct);
        return MapResults(result, isTv: true);
    }

    /// <summary>
    /// Alternative titles for a movie or TV series (e.g. romanizations / regional names). Used to rescue
    /// matches where the filename uses a title TMDB only exposes here, not as its localized/original name.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAlternativeTitlesAsync(int id, bool isTv, string apiKey, CancellationToken ct)
    {
        var kind = isTv ? "tv" : "movie";
        var url = $"{BaseUrl}{kind}/{id}/alternative_titles?api_key={apiKey}";
        var result = await GetAsync<AlternativeTitlesResponse>(url, ct);
        // Movies return the list under "titles"; TV under "results".
        var titles = result?.Titles ?? result?.Results;
        if (titles is null)
            return Array.Empty<string>();
        return titles
            .Select(t => t.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .ToList();
    }

    /// <summary>Episodes (number, name, still) for one season of a TV series.</summary>
    public async Task<IReadOnlyList<TmdbEpisode>> GetSeasonEpisodesAsync(int tvId, int season, string apiKey, CancellationToken ct)
    {
        var url = $"{BaseUrl}tv/{tvId}/season/{season}?api_key={apiKey}";
        var result = await GetAsync<SeasonResponse>(url, ct);
        if (result?.Episodes is null)
            return Array.Empty<TmdbEpisode>();
        return result.Episodes
            .Select(e => new TmdbEpisode(e.EpisodeNumber, e.Name, e.StillPath))
            .ToList();
    }

    /// <summary>Downloads an image given its TMDB relative path (e.g. "/abc.jpg") at the given size.</summary>
    public async Task<byte[]?> DownloadImageAsync(string tmdbPath, string size, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tmdbPath))
            return null;
        var url = $"{ImageBase}{size}{tmdbPath}";
        using var response = await SendWithBackoffAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (response is null || !response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static IReadOnlyList<TmdbMatch> MapResults(SearchResponse? result, bool isTv)
    {
        if (result?.Results is null)
            return Array.Empty<TmdbMatch>();
        return result.Results
            .Select(r =>
            {
                var name = (isTv ? r.Name : r.Title) ?? string.Empty;
                var original = isTv ? r.OriginalName : r.OriginalTitle;
                var date = isTv ? r.FirstAirDate : r.ReleaseDate;
                return new TmdbMatch(r.Id, name, original, ParseYear(date), r.PosterPath, r.BackdropPath, r.Overview);
            })
            .Where(m => !string.IsNullOrWhiteSpace(m.Title))
            .ToList();
    }

    private static int? ParseYear(string? date) =>
        !string.IsNullOrWhiteSpace(date) && date.Length >= 4 && int.TryParse(date[..4], out var y) ? y : null;

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

    private class SearchResponse
    {
        [JsonPropertyName("results")] public List<ResultDto>? Results { get; set; }
    }

    private class ResultDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }          // movie
        [JsonPropertyName("name")] public string? Name { get; set; }            // tv
        [JsonPropertyName("original_title")] public string? OriginalTitle { get; set; } // movie
        [JsonPropertyName("original_name")] public string? OriginalName { get; set; }   // tv
        [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
        [JsonPropertyName("first_air_date")] public string? FirstAirDate { get; set; }
        [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }
        [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; set; }
        [JsonPropertyName("overview")] public string? Overview { get; set; }
    }

    private class SeasonResponse
    {
        [JsonPropertyName("episodes")] public List<EpisodeDto>? Episodes { get; set; }
    }

    private class EpisodeDto
    {
        [JsonPropertyName("episode_number")] public int EpisodeNumber { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("still_path")] public string? StillPath { get; set; }
    }

    private class AlternativeTitlesResponse
    {
        [JsonPropertyName("titles")] public List<AltTitleDto>? Titles { get; set; }   // movie
        [JsonPropertyName("results")] public List<AltTitleDto>? Results { get; set; } // tv
    }

    private class AltTitleDto
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
    }
}
