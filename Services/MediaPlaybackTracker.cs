using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;

namespace Mosaic.Services;

public class MediaPlaybackTracker : IMediaPlaybackTracker
{
    private readonly IDbContextFactory<MosaicDbContext> _contextFactory;
    private readonly ISettingsService _settings;

    public MediaPlaybackTracker(
        IDbContextFactory<MosaicDbContext> contextFactory,
        ISettingsService settings)
    {
        _contextFactory = contextFactory;
        _settings = settings;
    }

    public event EventHandler<int>? WatchStarted;
    public event EventHandler<int>? WatchStateChanged;

    public async Task<bool> PlayAsync(int mediaItemId)
    {
        MediaItem? item;
        await using (var db = await _contextFactory.CreateDbContextAsync())
            item = await db.MediaItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaItemId);

        if (item?.FilePath is null || !File.Exists(item.FilePath))
            return false;

        // Use the configured player only when it is set AND its exe still exists; otherwise fall
        // back to the OS default association. (The File.Exists check is the only I/O here.)
        var preferred = _settings.Current.PreferredMediaPlayerPath;
        if (string.IsNullOrWhiteSpace(preferred) || !File.Exists(preferred))
            preferred = null;

        if (!TryStart(ResolveLaunch(item.FilePath, preferred)))
        {
            // A configured player that exists but fails to start: fall back to the default once.
            if (preferred is null || !TryStart(ResolveLaunch(item.FilePath, null)))
                return false;
        }

        await using (var db = await _contextFactory.CreateDbContextAsync())
        {
            db.WatchSessions.Add(new WatchSession { MediaItemId = mediaItemId, StartedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        WatchStarted?.Invoke(this, mediaItemId);
        return true;
    }

    /// <summary>
    /// Builds the launch for a media file: a non-null player path launches that player with the
    /// file as a quoted argument; a null path uses the OS default file association. Pure (no I/O).
    /// </summary>
    internal static ProcessStartInfo ResolveLaunch(string filePath, string? preferredPlayerPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPlayerPath))
            return new ProcessStartInfo
            {
                FileName = preferredPlayerPath,
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false,
            };

        return new ProcessStartInfo { FileName = filePath, UseShellExecute = true };
    }

    private static bool TryStart(ProcessStartInfo psi)
    {
        try
        {
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SetWatchedAsync(int mediaItemId, bool watched)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var item = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item is null)
            return;

        item.WatchedAt = watched ? DateTimeOffset.UtcNow : null;
        if (!watched)
            item.ResumePositionSeconds = null; // clearing watched also clears any "left off" marker
        await db.SaveChangesAsync();
        WatchStateChanged?.Invoke(this, mediaItemId);
    }

    public async Task<MediaItem?> MarkWatchedAndAdvanceAsync(int episodeId)
    {
        int? seriesId;
        await using (var db = await _contextFactory.CreateDbContextAsync())
            seriesId = await db.MediaItems.AsNoTracking()
                .Where(m => m.Id == episodeId).Select(m => m.ParentId).FirstOrDefaultAsync();

        await SetWatchedAsync(episodeId, true);
        return seriesId is int id ? await GetResumeEpisodeAsync(id) : null;
    }

    public async Task<MediaItem?> GetResumeEpisodeAsync(int seriesId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var episodes = await db.MediaItems.AsNoTracking()
            .Where(m => m.ParentId == seriesId && m.WatchedAt == null)
            .ToListAsync();
        return episodes
            .OrderBy(e => e.SeasonNumber ?? 0)
            .ThenBy(e => e.EpisodeNumber ?? 0)
            .FirstOrDefault();
    }

    /// <summary>
    /// Applies an observed playback position (from the Tier-1 system-media-controls observer) via the
    /// monotonic <see cref="WatchProgress"/> decision: auto-marks watched at the threshold and records
    /// a resume position, never clearing an existing watched state. Internal — not part of the public API.
    /// </summary>
    internal async Task ApplyObservedPositionAsync(int mediaItemId, double positionSeconds, double endTimeSeconds)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var item = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item is null)
            return;

        var result = WatchProgress.Evaluate(positionSeconds, endTimeSeconds, item.IsWatched);
        var changed = false;

        if (result.MarkWatched && item.WatchedAt is null)
        {
            item.WatchedAt = DateTimeOffset.UtcNow;
            changed = true;
        }
        if (result.ResumePositionSeconds is double rp && Math.Abs((item.ResumePositionSeconds ?? -1) - rp) >= 1)
        {
            item.ResumePositionSeconds = rp;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
            WatchStateChanged?.Invoke(this, mediaItemId);
        }
    }

    /// <summary>Title + file name used by the Tier-1 observer to correlate a playing session to an item.</summary>
    internal async Task<(string Title, string FileName)?> GetMatchInfoAsync(int mediaItemId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var item = await db.MediaItems.AsNoTracking()
            .Where(m => m.Id == mediaItemId)
            .Select(m => new { m.Title, m.FilePath })
            .FirstOrDefaultAsync();
        if (item is null)
            return null;
        var fileName = string.IsNullOrEmpty(item.FilePath) ? string.Empty : Path.GetFileNameWithoutExtension(item.FilePath);
        return (item.Title, fileName);
    }
}
