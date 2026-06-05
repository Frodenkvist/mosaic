using System.IO;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;

namespace Mosaic.Services;

public class MediaArtworkService : IMediaArtworkService
{
    // Image sizes from the TMDB image CDN.
    private const string PosterSize = "w500";
    private const string BackdropSize = "w780";
    private const string StillSize = "w300";

    // Minimum title similarity to accept a TMDB match.
    private const double MatchThreshold = 0.5;

    // Serializes TMDB access so a batch scan isn't rate-limited (mirrors ArtworkService).
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

    private readonly IDbContextFactory<MosaicDbContext> _contextFactory;
    private readonly AppPaths _paths;
    private readonly ISettingsService _settings;
    private readonly TmdbClient _client;

    public MediaArtworkService(
        IDbContextFactory<MosaicDbContext> contextFactory,
        AppPaths paths,
        ISettingsService settings,
        TmdbClient client)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _settings = settings;
        _client = client;
    }

    public event EventHandler<int>? MediaArtworkUpdated;
    public event EventHandler<int>? MediaArtworkFetchStarted;
    public event EventHandler<int>? MediaArtworkFetchFailed;

    public Task FetchArtworkAsync(int mediaItemId, bool refetch = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Current.TmdbApiKey))
            return Task.CompletedTask; // Graceful degradation: no key, no auto-fetch.

        return Task.Run(() => FetchArtworkCoreAsync(mediaItemId, refetch, cancellationToken), cancellationToken);
    }

    private async Task FetchArtworkCoreAsync(int mediaItemId, bool refetch, CancellationToken ct)
    {
        var apiKey = _settings.Current.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var item = await db.MediaItems
            .Include(m => m.Artwork)
            .FirstOrDefaultAsync(m => m.Id == mediaItemId, ct);
        if (item is null || item.Kind == MediaKind.Episode)
            return; // episodes are enriched via their series

        var neededKinds = new[] { MediaArtworkKind.Poster, MediaArtworkKind.Backdrop }
            .Where(k => refetch ? !HasManualOverride(item, k) : !HasUsableArtwork(item, k))
            .ToList();
        var needSeriesEpisodes = item.Kind == MediaKind.Series;
        if (neededKinds.Count == 0 && !needSeriesEpisodes)
            return;

        MediaArtworkFetchStarted?.Invoke(this, mediaItemId);

        var changed = false;
        try
        {
            await FetchGate.WaitAsync(ct);
            try
            {
                var match = await ResolveMatchAsync(item, apiKey, ct);
                if (match is not null)
                {
                    // First successful link: adopt the matched id, title and year.
                    if (item.TmdbId is null)
                    {
                        item.TmdbId = match.Id;
                        if (!refetch && !string.IsNullOrWhiteSpace(match.Title))
                            item.Title = match.Title;
                        item.Year ??= match.Year;
                        changed = true;
                    }

                    if (neededKinds.Contains(MediaArtworkKind.Poster)
                        && await DownloadAndCacheAsync(item, MediaArtworkKind.Poster, match.PosterPath, PosterSize, ct))
                        changed = true;
                    if (neededKinds.Contains(MediaArtworkKind.Backdrop)
                        && await DownloadAndCacheAsync(item, MediaArtworkKind.Backdrop, match.BackdropPath, BackdropSize, ct))
                        changed = true;

                    if (needSeriesEpisodes)
                        changed |= await FillEpisodesAsync(db, item, match.Id, apiKey, ct);
                }
            }
            finally
            {
                FetchGate.Release();
            }

            if (changed)
            {
                await db.SaveChangesAsync(ct);
                MediaArtworkUpdated?.Invoke(this, mediaItemId);
            }
            else
            {
                MediaArtworkFetchFailed?.Invoke(this, mediaItemId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            MediaArtworkFetchFailed?.Invoke(this, mediaItemId);
        }
    }

    public async Task FetchMissingForAllAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Current.TmdbApiKey))
            return;

        List<int> ids;
        await using (var db = await _contextFactory.CreateDbContextAsync(cancellationToken))
            ids = await db.MediaItems
                .Where(m => m.Kind == MediaKind.Movie || m.Kind == MediaKind.Series)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await FetchArtworkAsync(id, refetch: false, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { /* best effort per item */ }
        }
    }

    public async Task<string> SetManualOverrideAsync(int mediaItemId, MediaArtworkKind kind, string sourceImagePath)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var item = await db.MediaItems
            .Include(m => m.Artwork)
            .FirstOrDefaultAsync(m => m.Id == mediaItemId)
            ?? throw new InvalidOperationException($"Media item {mediaItemId} not found.");

        var localPath = BuildCachePath(mediaItemId, kind, sourceImagePath);
        File.Copy(sourceImagePath, localPath, overwrite: true);

        UpsertArtworkRow(item, kind, localPath, isManual: true, sourceId: null);
        await db.SaveChangesAsync();
        MediaArtworkUpdated?.Invoke(this, mediaItemId);
        return localPath;
    }

    // How many top-ranked candidates to probe for alternative titles before giving up.
    private const int AltTitleProbeLimit = 3;

    private async Task<TmdbMatch?> ResolveMatchAsync(MediaItem item, string apiKey, CancellationToken ct)
    {
        var isTv = item.Kind == MediaKind.Series;

        async Task<IReadOnlyList<TmdbMatch>> Search(int? year) => isTv
            ? await _client.SearchTvAsync(item.Title, year, apiKey, ct)
            : await _client.SearchMoviesAsync(item.Title, year, apiKey, ct);

        var results = await Search(item.Year);
        if (results.Count == 0 && item.Year is not null)
            results = await Search(null); // retry without the year constraint
        if (results.Count == 0)
            return null;

        var ranked = results
            .Select(r => new { Match = r, Score = TitleScore(item, r) })
            .OrderByDescending(x => x.Score)
            .ToList();

        // 1) Confident match: the localized or original title is similar enough.
        if (ranked[0].Score >= MatchThreshold)
            return ranked[0].Match;

        // 2) Foreign/romanized titles (e.g. anime) are often filed under an alternative title TMDB only
        //    exposes via /alternative_titles — not as its localized name or native original name. Re-score
        //    the strongest candidates against those before giving up.
        foreach (var candidate in ranked.Take(AltTitleProbeLimit))
        {
            var alts = await _client.GetAlternativeTitlesAsync(candidate.Match.Id, isTv, apiKey, ct);
            if (alts.Any(t => ArtworkService.Similarity(item.Title, t) >= MatchThreshold))
                return candidate.Match;
        }

        // 3) Last resort: a specific query that TMDB resolves to a single result is almost certainly correct.
        return results.Count == 1 ? results[0] : null;
    }

    // Best title similarity across TMDB's localized and original titles, plus a small year-agreement bonus.
    private static double TitleScore(MediaItem item, TmdbMatch r)
    {
        var yearBonus = item.Year is int y && r.Year == y ? 0.2 : 0;
        var localized = ArtworkService.Similarity(item.Title, r.Title);
        var original = string.IsNullOrWhiteSpace(r.OriginalTitle)
            ? 0
            : ArtworkService.Similarity(item.Title, r.OriginalTitle!);
        return Math.Max(localized, original) + yearBonus;
    }

    private async Task<bool> FillEpisodesAsync(MosaicDbContext db, MediaItem series, int tvId, string apiKey, CancellationToken ct)
    {
        var episodes = await db.MediaItems
            .Include(e => e.Artwork)
            .Where(e => e.ParentId == series.Id)
            .ToListAsync(ct);

        var changed = false;
        foreach (var seasonGroup in episodes
                     .Where(e => e.SeasonNumber is not null)
                     .GroupBy(e => e.SeasonNumber!.Value))
        {
            var tmdbEpisodes = (await _client.GetSeasonEpisodesAsync(tvId, seasonGroup.Key, apiKey, ct))
                .ToDictionary(t => t.EpisodeNumber);

            foreach (var episode in seasonGroup)
            {
                if (episode.EpisodeNumber is not int n || !tmdbEpisodes.TryGetValue(n, out var te))
                    continue; // unmatched episode keeps its filename-derived title

                if (!string.IsNullOrWhiteSpace(te.Name))
                {
                    episode.Title = te.Name!;
                    changed = true;
                }
                if (await DownloadAndCacheAsync(episode, MediaArtworkKind.EpisodeStill, te.StillPath, StillSize, ct))
                    changed = true;
            }
        }
        return changed;
    }

    private async Task<bool> DownloadAndCacheAsync(
        MediaItem item, MediaArtworkKind kind, string? tmdbPath, string size, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tmdbPath))
            return false;
        try
        {
            var bytes = await _client.DownloadImageAsync(tmdbPath, size, ct);
            if (bytes is null || bytes.Length == 0)
                return false;
            var localPath = BuildCachePath(item.Id, kind, tmdbPath);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
            UpsertArtworkRow(item, kind, localPath, isManual: false, sourceId: item.TmdbId);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false; // a single image failing must not abort the rest
        }
    }

    private static bool HasUsableArtwork(MediaItem item, MediaArtworkKind kind)
    {
        var existing = item.Artwork.FirstOrDefault(a => a.Kind == kind);
        if (existing is null)
            return false;
        return existing.IsManualOverride || File.Exists(existing.LocalPath);
    }

    private static bool HasManualOverride(MediaItem item, MediaArtworkKind kind) =>
        item.Artwork.FirstOrDefault(a => a.Kind == kind)?.IsManualOverride == true;

    private static void UpsertArtworkRow(MediaItem item, MediaArtworkKind kind, string localPath, bool isManual, long? sourceId)
    {
        var existing = item.Artwork.FirstOrDefault(a => a.Kind == kind);
        if (existing is null)
        {
            item.Artwork.Add(new MediaArtwork
            {
                MediaItemId = item.Id,
                Kind = kind,
                LocalPath = localPath,
                SourceId = sourceId,
                IsManualOverride = isManual,
            });
        }
        else
        {
            existing.LocalPath = localPath;
            existing.SourceId = sourceId;
            existing.IsManualOverride = isManual;
        }
    }

    private string BuildCachePath(int mediaItemId, MediaArtworkKind kind, string sourceUrlOrPath)
    {
        var ext = Path.GetExtension(sourceUrlOrPath);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5)
            ext = ".jpg";
        return Path.Combine(_paths.MediaArtworkDirectory, $"{mediaItemId}_{kind}{ext}".ToLowerInvariant());
    }
}
