namespace Mosaic.Services;

/// <summary>
/// Corrects "absolute" (cross-season cumulative) episode numbering into per-season numbering.
/// Some libraries — anime especially, and users who split a long show into <c>Season N</c> folders
/// while keeping a single running count — number episodes continuously across the whole series
/// (Season 1: 1..8, Season 2: 9..16, …) rather than restarting each season. Metadata matching,
/// ordering, and display all assume a within-season number that restarts at 1, so we detect the
/// continuous case and map each episode back to its within-season index.
/// </summary>
public static class EpisodeNumbering
{
    /// <summary>
    /// Given episodes keyed by a stable key — each with a season and a raw episode number — returns
    /// the corrected within-season episode number for every key.
    ///
    /// Seasons are walked in ascending order; a season is treated as absolute-numbered (and shifted
    /// down) only when its lowest episode number continues directly from the running absolute maximum
    /// of the earlier seasons (<c>min == prevMax + 1</c>). Anchoring on the previous season's max
    /// (not its episode count) tolerates gaps within an earlier season. A series that already restarts
    /// each season is returned unchanged, and the transform is idempotent. The first season present is
    /// never shifted — there is no earlier boundary to anchor to.
    ///
    /// Callers are responsible for excluding entries without both a season and an episode number;
    /// those are left untouched.
    /// </summary>
    public static IReadOnlyDictionary<TKey, int> NormalizeWithinSeason<TKey>(
        IEnumerable<(TKey Key, int Season, int Episode)> entries)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, int>();
        var prevAbsoluteMax = 0;

        foreach (var season in entries.GroupBy(e => e.Season).OrderBy(g => g.Key))
        {
            var minRaw = season.Min(e => e.Episode);
            var maxRaw = season.Max(e => e.Episode);

            int offset;
            if (prevAbsoluteMax > 0 && minRaw == prevAbsoluteMax + 1)
            {
                // Absolute-numbered: this season continues the running count, so its raw numbers are
                // the absolute indices and the next boundary is this season's max.
                offset = prevAbsoluteMax;
                prevAbsoluteMax = maxRaw;
            }
            else
            {
                // Already within-season (restarts at 1), or the first season present.
                offset = 0;
                prevAbsoluteMax += maxRaw;
            }

            foreach (var e in season)
                result[e.Key] = e.Episode - offset;
        }

        return result;
    }
}
