using System.Collections.Concurrent;
using System.Text;
using Windows.Media.Control;

namespace Mosaic.Services;

/// <summary>
/// Tier-1 automatic watch detection. Listens for media playback published to the Windows system
/// media controls (GSMTC) and, when it can correlate a playing session to an item Mosaic just
/// launched, feeds the position to <see cref="MediaPlaybackTracker"/> so it can auto-mark watched
/// and record a resume position. Entirely best-effort: any failure — the API being unavailable, a
/// player that publishes nothing, or an ambiguous match — is a silent no-op, and the Tier-0 manual
/// toggle carries the feature. Correlation never auto-acts on an ambiguous (multi-session) match.
/// </summary>
public sealed class SystemMediaWatchObserver : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GiveUpUnmatched = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxTrack = TimeSpan.FromHours(6);

    private readonly MediaPlaybackTracker _tracker;
    private readonly ConcurrentDictionary<int, Expectation> _expected = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _loopGate = new();
    private Task? _loop;

    public SystemMediaWatchObserver(MediaPlaybackTracker tracker)
    {
        _tracker = tracker;
        _tracker.WatchStarted += OnWatchStarted;
    }

    private void OnWatchStarted(object? sender, int mediaItemId) => _ = AddExpectationAsync(mediaItemId);

    private async Task AddExpectationAsync(int mediaItemId)
    {
        try
        {
            if (await _tracker.GetMatchInfoAsync(mediaItemId) is not { } info)
                return;
            _expected[mediaItemId] = new Expectation(
                Normalize(info.Title), Normalize(info.FileName), DateTimeOffset.UtcNow);
            EnsureLoop();
        }
        catch
        {
            // Best-effort: never let Tier-1 setup disturb playback.
        }
    }

    private void EnsureLoop()
    {
        lock (_loopGate)
        {
            if (_loop is { IsCompleted: false })
                return;
            _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        GlobalSystemMediaTransportControlsSessionManager? manager = null;
        while (!ct.IsCancellationRequested && !_expected.IsEmpty)
        {
            try
            {
                manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                await PollOnceAsync(manager);
            }
            catch
            {
                // API unavailable or a transient WinRT error: keep idling; Tier 0 still works.
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var now = DateTimeOffset.UtcNow;

        // Prune expectations that have aged out (player published nothing, or we've tracked long enough).
        foreach (var (id, exp) in _expected.ToArray())
        {
            if (now - exp.LaunchedAt > MaxTrack || (!exp.Matched && now - exp.LaunchedAt > GiveUpUnmatched))
                _expected.TryRemove(id, out _);
        }
        if (_expected.IsEmpty)
            return;

        var sessions = manager.GetSessions();
        foreach (var (id, exp) in _expected.ToArray())
        {
            var matches = new List<GlobalSystemMediaTransportControlsSession>();
            foreach (var session in sessions)
            {
                string? title = null;
                try { title = (await session.TryGetMediaPropertiesAsync())?.Title; }
                catch { /* some sessions refuse media properties */ }
                if (TitleMatches(Normalize(title), exp))
                    matches.Add(session);
            }

            // Ambiguous (more than one session looks like this item): make no automatic change.
            if (matches.Count != 1)
                continue;

            try
            {
                var timeline = matches[0].GetTimelineProperties();
                var end = timeline.EndTime.TotalSeconds;
                var position = timeline.Position.TotalSeconds;
                if (end <= 0)
                    continue;

                exp.Matched = true;
                await _tracker.ApplyObservedPositionAsync(id, position, end);
                if (position >= end * WatchProgress.CompletionThreshold)
                    _expected.TryRemove(id, out _); // finished — stop tracking it
            }
            catch
            {
                // A session can vanish mid-read; ignore and retry next poll.
            }
        }
    }

    private static bool TitleMatches(string sessionTitle, Expectation exp)
    {
        if (sessionTitle.Length < 3)
            return false;
        return Contains(sessionTitle, exp.Title) || Contains(sessionTitle, exp.FileName)
            || Contains(exp.FileName, sessionTitle) || Contains(exp.Title, sessionTitle);
    }

    private static bool Contains(string haystack, string needle) =>
        needle.Length >= 3 && haystack.Contains(needle, StringComparison.Ordinal);

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        _tracker.WatchStarted -= OnWatchStarted;
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
    }

    private sealed class Expectation(string title, string fileName, DateTimeOffset launchedAt)
    {
        public string Title { get; } = title;
        public string FileName { get; } = fileName;
        public DateTimeOffset LaunchedAt { get; } = launchedAt;
        public bool Matched { get; set; }
    }
}
