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

        return Ago(stats.LastPlayed.Value);
    }

    /// <summary>Relative "x ago" / date for a timestamp, used by both play and watch surfaces.</summary>
    public static string Ago(DateTimeOffset when)
    {
        var ago = DateTimeOffset.Now - when;
        if (ago < TimeSpan.FromMinutes(1)) return "Just now";
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes} min ago";
        if (ago < TimeSpan.FromDays(1)) return $"{(int)ago.TotalHours} h ago";
        if (ago < TimeSpan.FromDays(7)) return $"{(int)ago.TotalDays} d ago";
        return when.ToLocalTime().ToString("yyyy-MM-dd");
    }

    /// <summary>An in-file resume position, e.g. "Left off at 23:41" (or "1:23:41" past an hour).</summary>
    public static string Resume(double? seconds)
    {
        if (seconds is not double s || s < 1)
            return string.Empty;
        var t = TimeSpan.FromSeconds(s);
        var clock = t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
        return $"Left off at {clock}";
    }
}
