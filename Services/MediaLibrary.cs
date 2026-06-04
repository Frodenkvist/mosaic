using System.IO;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;

namespace Mosaic.Services;

public class MediaLibrary : IMediaLibrary
{
    /// <summary>Default minimum file size for a scan candidate; filters tiny clips. Tests pass 0.</summary>
    public const long DefaultMinFileSizeBytes = 10L * 1024 * 1024; // 10 MB

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".mpg", ".mpeg", ".flv", ".webm", ".ts", ".m2ts",
    };

    // File-name fragments (spaces removed) that mark a non-content extra rather than the feature.
    private static readonly string[] JunkNameFragments =
    {
        "sample", "trailer", "featurette", "behindthescenes", "deletedscene", "bonus",
    };

    // Folder names whose contents are extras, not the feature itself.
    private static readonly HashSet<string> JunkPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "extras", "featurettes", "behind the scenes", "deleted scenes", "trailers", "sample", "samples",
    };

    private readonly IDbContextFactory<MosaicDbContext> _contextFactory;
    private readonly AppPaths _paths;
    private readonly IMediaArtworkService _artwork;

    public MediaLibrary(
        IDbContextFactory<MosaicDbContext> contextFactory,
        AppPaths paths,
        IMediaArtworkService artwork)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _artwork = artwork;
    }

    public async Task<IReadOnlyList<MediaListItem>> GetLibraryAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var items = await TopLevelWithArtworkQuery(db).ToListAsync();
        var progress = await EpisodeProgressAsync(db);
        var lastWatched = await LastWatchedByTopLevelAsync(db);
        return items
            .Select(m => ToListItem(m, progress, lastWatched))
            .OrderBy(i => i.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<MediaListItem>> GetRecentlyWatchedAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var items = await TopLevelWithArtworkQuery(db).ToListAsync();
        var progress = await EpisodeProgressAsync(db);
        var lastWatched = await LastWatchedByTopLevelAsync(db);
        return items
            .Select(m => ToListItem(m, progress, lastWatched))
            .Where(i => i.LastWatched is not null)
            .OrderByDescending(i => i.LastWatched)
            .ToList();
    }

    public async Task<MediaItem?> GetMediaItemAsync(int id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.MediaItems.AsNoTracking()
            .Include(m => m.Artwork)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<IReadOnlyList<MediaItem>> GetEpisodesAsync(int seriesId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var episodes = await db.MediaItems.AsNoTracking()
            .Include(m => m.Artwork)
            .Where(m => m.ParentId == seriesId)
            .ToListAsync();
        return episodes
            .OrderBy(e => e.SeasonNumber ?? 0)
            .ThenBy(e => e.EpisodeNumber ?? 0)
            .ToList();
    }

    public async Task<IReadOnlyList<MediaScanCandidate>> ScanFoldersAsync(
        IEnumerable<string> folders, long minFileSizeBytes = DefaultMinFileSizeBytes)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var known = (await db.MediaItems.AsNoTracking()
                .Where(m => m.FilePath != null)
                .Select(m => m.FilePath!)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<MediaScanCandidate>();
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            var root = Path.GetFullPath(folder);
            foreach (var file in EnumerateVideoFiles(root))
            {
                var full = Path.GetFullPath(file);
                if (known.Contains(full) || IsJunk(root, full))
                    continue;
                if (minFileSizeBytes > 0 && FileSize(full) < minFileSizeBytes)
                    continue;

                var folderPath = Path.GetDirectoryName(full) ?? root;
                if (MediaNameParser.TryParseEpisode(full) is EpisodeInfo e)
                {
                    candidates.Add(new MediaScanCandidate(
                        MediaCandidateKind.Episode,
                        Title: $"S{e.Season:D2}E{e.Episode:D2}",
                        FilePath: full,
                        FolderPath: folderPath,
                        SeriesTitle: e.ShowName,
                        SeasonNumber: e.Season,
                        EpisodeNumber: e.Episode));
                }
                else
                {
                    var movie = ResolveMovie(root, full);
                    var title = string.IsNullOrWhiteSpace(movie.Title)
                        ? MediaNameParser.CleanTitle(Path.GetFileNameWithoutExtension(full))
                        : movie.Title;
                    candidates.Add(new MediaScanCandidate(
                        MediaCandidateKind.Movie,
                        Title: title,
                        FilePath: full,
                        FolderPath: folderPath,
                        Year: movie.Year));
                }
            }
        }

        return candidates
            .OrderBy(c => c.SeriesTitle ?? c.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.SeasonNumber ?? 0)
            .ThenBy(c => c.EpisodeNumber ?? 0)
            .ToList();
    }

    public async Task<IReadOnlyList<MediaItem>> AddConfirmedAsync(IEnumerable<MediaScanCandidate> confirmed)
    {
        var list = confirmed.ToList();
        var added = new List<MediaItem>();
        var now = DateTimeOffset.UtcNow;

        await using var db = await _contextFactory.CreateDbContextAsync();
        var knownFiles = (await db.MediaItems.AsNoTracking()
                .Where(m => m.FilePath != null)
                .Select(m => m.FilePath!)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Movies.
        foreach (var c in list.Where(c => c.Kind == MediaCandidateKind.Movie))
        {
            if (!knownFiles.Add(c.FilePath))
                continue;
            var movie = new MediaItem
            {
                Kind = MediaKind.Movie,
                Title = c.Title,
                FilePath = c.FilePath,
                FolderPath = c.FolderPath,
                Year = c.Year,
                DateAdded = now,
            };
            db.MediaItems.Add(movie);
            added.Add(movie);
        }

        // Episodes, grouped under a created-or-reused series.
        foreach (var group in list
                     .Where(c => c.Kind == MediaCandidateKind.Episode)
                     .GroupBy(c => c.SeriesTitle ?? "Unknown", StringComparer.OrdinalIgnoreCase))
        {
            var seriesTitle = group.Key;
            var series = await db.MediaItems
                .FirstOrDefaultAsync(m => m.Kind == MediaKind.Series && m.Title == seriesTitle);
            if (series is null)
            {
                series = new MediaItem
                {
                    Kind = MediaKind.Series,
                    Title = seriesTitle,
                    FolderPath = SeriesFolder(group.First().FolderPath),
                    DateAdded = now,
                };
                db.MediaItems.Add(series);
                added.Add(series);
            }

            foreach (var c in group)
            {
                if (!knownFiles.Add(c.FilePath))
                    continue;
                var episode = new MediaItem
                {
                    Kind = MediaKind.Episode,
                    Title = c.Title,
                    FilePath = c.FilePath,
                    FolderPath = c.FolderPath,
                    SeasonNumber = c.SeasonNumber,
                    EpisodeNumber = c.EpisodeNumber,
                    DateAdded = now,
                };
                series.Episodes.Add(episode);
                added.Add(episode);
            }
        }

        await db.SaveChangesAsync();

        // Best-effort artwork per movie/series (episodes get their stills via the series fetch).
        foreach (var item in added.Where(i => i.Kind != MediaKind.Episode))
            _ = FetchArtworkSafelyAsync(item.Id);

        return added;
    }

    public async Task UpdateMediaItemAsync(MediaItem item)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var existing = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == item.Id)
            ?? throw new InvalidOperationException($"Media item {item.Id} not found.");

        existing.Title = item.Title.Trim();
        existing.Year = item.Year;
        existing.SeasonNumber = item.SeasonNumber;
        existing.EpisodeNumber = item.EpisodeNumber;
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(int id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var item = await db.MediaItems
            .Include(m => m.Artwork)
            .Include(m => m.Episodes).ThenInclude(e => e.Artwork)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (item is null)
            return;

        // Delete cached image files (only those inside our data dir); never touch the video files.
        foreach (var art in item.Artwork)
            TryDeleteCachedFile(art.LocalPath);
        foreach (var episode in item.Episodes)
            foreach (var art in episode.Artwork)
                TryDeleteCachedFile(art.LocalPath);

        db.MediaItems.Remove(item); // episodes, watch sessions & artwork rows cascade-delete
        await db.SaveChangesAsync();
    }

    private static IQueryable<MediaItem> TopLevelWithArtworkQuery(MosaicDbContext db) =>
        db.MediaItems.AsNoTracking()
            .Include(m => m.Artwork)
            .Where(m => m.Kind == MediaKind.Movie || m.Kind == MediaKind.Series);

    /// <summary>Per-series (watched, total) episode counts.</summary>
    private static async Task<Dictionary<int, (int Watched, int Total)>> EpisodeProgressAsync(MosaicDbContext db)
    {
        var episodes = await db.MediaItems.AsNoTracking()
            .Where(m => m.Kind == MediaKind.Episode && m.ParentId != null)
            .Select(m => new { ParentId = m.ParentId!.Value, Watched = m.WatchedAt != null })
            .ToListAsync();

        return episodes
            .GroupBy(e => e.ParentId)
            .ToDictionary(g => g.Key, g => (g.Count(e => e.Watched), g.Count()));
    }

    /// <summary>Most-recent watch time keyed by top-level item id (an episode's watch rolls up to its series).</summary>
    private static async Task<Dictionary<int, DateTimeOffset>> LastWatchedByTopLevelAsync(MosaicDbContext db)
    {
        var parentOf = await db.MediaItems.AsNoTracking()
            .Select(m => new { m.Id, m.ParentId })
            .ToDictionaryAsync(m => m.Id, m => m.ParentId);

        var sessions = await db.WatchSessions.AsNoTracking()
            .Select(w => new { w.MediaItemId, w.StartedAt })
            .ToListAsync();

        var result = new Dictionary<int, DateTimeOffset>();
        foreach (var s in sessions)
        {
            // Roll an episode's watch up to its series; a movie maps to itself.
            var topId = parentOf.TryGetValue(s.MediaItemId, out var pid) && pid is int p ? p : s.MediaItemId;
            if (!result.TryGetValue(topId, out var existing) || s.StartedAt > existing)
                result[topId] = s.StartedAt;
        }
        return result;
    }

    private static MediaListItem ToListItem(
        MediaItem item,
        Dictionary<int, (int Watched, int Total)> progress,
        Dictionary<int, DateTimeOffset> lastWatched)
    {
        var poster = item.Artwork.FirstOrDefault(a => a.Kind == MediaArtworkKind.Poster)?.LocalPath;
        var (watched, total) = progress.TryGetValue(item.Id, out var p) ? p : (0, 0);
        DateTimeOffset? last = lastWatched.TryGetValue(item.Id, out var lw) ? lw : null;
        return new MediaListItem(item, poster, watched, total, last);
    }

    private static IEnumerable<string> EnumerateVideoFiles(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(root, "*", options)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f)));
    }

    private static bool IsJunk(string root, string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(" ", string.Empty);
        if (JunkNameFragments.Any(f => name.Contains(f)))
            return true;

        var rel = Path.GetRelativePath(root, path);
        var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => JunkPathSegments.Contains(s));
    }

    /// <summary>
    /// Resolves a movie's title/year. Prefers the file name (which usually carries the title), and
    /// borrows the parent folder's year — and its title when richer (a dedicated "Movie (Year)"
    /// folder around a generically-named file) — without mistaking a shared bucket folder for a title.
    /// </summary>
    private static MovieInfo ResolveMovie(string root, string file)
    {
        var info = MediaNameParser.ParseMovie(Path.GetFileName(file));
        var title = info.Title;
        var year = info.Year;

        var dir = Path.GetDirectoryName(file);
        if (dir is not null && !PathEquals(dir, root))
        {
            var folder = MediaNameParser.ParseMovie(
                Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            year ??= folder.Year;
            if (folder.Title.Length > title.Length && (folder.Year is not null || title.Length < 3))
                title = folder.Title;
        }
        return new MovieInfo(title, year);
    }

    /// <summary>The show folder for a set of episodes: the parent of a "Season N" folder, else the folder itself.</summary>
    private static string SeriesFolder(string episodeFolder)
    {
        var name = Path.GetFileName(episodeFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^(?:season|series|s)[\s._\-]*\d{1,2}$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return Path.GetDirectoryName(episodeFolder) ?? episodeFolder;
        }
        return episodeFolder;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static long FileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private void TryDeleteCachedFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && _paths.IsInsideDataDirectory(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Non-fatal: an orphaned cache file is acceptable.
        }
    }

    private async Task FetchArtworkSafelyAsync(int mediaItemId)
    {
        try
        {
            await _artwork.FetchArtworkAsync(mediaItemId);
        }
        catch
        {
            // Best-effort: a background artwork fetch must never crash the app.
        }
    }
}
