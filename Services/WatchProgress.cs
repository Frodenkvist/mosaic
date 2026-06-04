namespace Mosaic.Services;

/// <summary>Outcome of evaluating an observed playback position.</summary>
/// <param name="MarkWatched">True when the item should be (or remain) marked watched.</param>
/// <param name="ResumePositionSeconds">A resume position to record, or null to leave it unchanged.</param>
public readonly record struct WatchProgressResult(bool MarkWatched, double? ResumePositionSeconds);

/// <summary>
/// Pure decision for Tier-1 automatic watch detection: given an observed playback position, the
/// media's runtime, and whether it is already watched, decide whether to mark it watched and what
/// resume position to record. Monotonic — it never asks to clear an existing watched state.
/// </summary>
public static class WatchProgress
{
    /// <summary>Fraction of the runtime at or past which an item counts as finished.</summary>
    public const double CompletionThreshold = 0.90;

    public static WatchProgressResult Evaluate(double positionSeconds, double endTimeSeconds, bool alreadyWatched)
    {
        // Already watched: stays watched (never cleared); no resume position is meaningful.
        if (alreadyWatched)
            return new WatchProgressResult(true, null);

        // Without a known runtime (or a nonsensical position) we cannot decide anything.
        if (endTimeSeconds <= 0 || positionSeconds < 0)
            return new WatchProgressResult(false, null);

        if (positionSeconds >= endTimeSeconds * CompletionThreshold)
            return new WatchProgressResult(true, null);

        // Below the threshold: not finished, but remember where they are.
        return new WatchProgressResult(false, positionSeconds);
    }
}
