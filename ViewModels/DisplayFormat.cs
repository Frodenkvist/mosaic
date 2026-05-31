using Mosaic.Services;

namespace Mosaic.ViewModels;

/// <summary>Shared formatting helpers for play stats shown in the UI.</summary>
public static class DisplayFormat
{
    public static string PlayTime(GameStats stats)
    {
        var t = stats.TotalPlayTime;
        if (t < TimeSpan.FromMinutes(1))
            return stats.HasBeenPlayed ? "< 1 min" : "Not played";
        if (t < TimeSpan.FromHours(1))
            return $"{(int)t.TotalMinutes} min";
        return $"{(int)t.TotalHours} h {t.Minutes} min";
    }

    public static string LastPlayed(GameStats stats)
    {
        if (stats.LastPlayed is null)
            return "Never played";

        var local = stats.LastPlayed.Value.ToLocalTime();
        var ago = DateTimeOffset.Now - stats.LastPlayed.Value;
        if (ago < TimeSpan.FromMinutes(1)) return "Just now";
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes} min ago";
        if (ago < TimeSpan.FromDays(1)) return $"{(int)ago.TotalHours} h ago";
        if (ago < TimeSpan.FromDays(7)) return $"{(int)ago.TotalDays} d ago";
        return local.ToString("yyyy-MM-dd");
    }
}
