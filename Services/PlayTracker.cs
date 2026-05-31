using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;

namespace Mosaic.Services;

public class PlayTracker : IPlayTracker
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RealExeAppearTimeout = TimeSpan.FromSeconds(120);

    private readonly IDbContextFactory<MosaicDbContext> _contextFactory;
    private readonly ConcurrentDictionary<int, DateTimeOffset> _runningSince = new();

    public PlayTracker(IDbContextFactory<MosaicDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public event EventHandler<int>? SessionStarted;
    public event EventHandler<int>? SessionEnded;

    public bool IsRunning(int gameId) => _runningSince.ContainsKey(gameId);

    public DateTimeOffset? GetRunningSince(int gameId) =>
        _runningSince.TryGetValue(gameId, out var since) ? since : null;

    public async Task<bool> LaunchAsync(int gameId)
    {
        Game? game;
        await using (var db = await _contextFactory.CreateDbContextAsync())
        {
            game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId);
        }
        if (game is null || !File.Exists(game.ExecutablePath))
            return false;

        var process = StartProcess(game);
        if (process is null)
            return false;

        // Persist an open session immediately so it survives an unexpected close.
        var startedAt = DateTimeOffset.UtcNow;
        var sessionId = await CreateOpenSessionAsync(gameId, startedAt);
        _runningSince[gameId] = startedAt;
        SessionStarted?.Invoke(this, gameId);

        // Track to completion in the background; do not block the caller (the UI).
        _ = Task.Run(() => TrackToCompletionAsync(game, process, sessionId));
        return true;
    }

    private async Task TrackToCompletionAsync(Game game, Process process, int sessionId)
    {
        var startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset endedAt;
        try
        {
            using var tracker = new JobObjectTracker();
            TryAssign(tracker, process);
            var treeExit = tracker.WaitForTreeExitAsync();

            if (string.IsNullOrWhiteSpace(game.RealExecutableName))
            {
                await treeExit;
                endedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                // Active period is defined by the real executable's presence.
                var (start, end) = await TrackRealExecutableAsync(game.RealExecutableName!, treeExit);
                startedAt = start ?? startedAt;
                endedAt = end;
            }
        }
        catch
        {
            endedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _runningSince.TryRemove(game.Id, out _);
        }

        await CloseSessionAsync(sessionId, startedAt, endedAt);
        SessionEnded?.Invoke(this, game.Id);
    }

    /// <summary>
    /// Waits for the named process to appear (or the launched tree to exit first),
    /// then waits for it to disappear. Returns the active start/end window.
    /// </summary>
    private static async Task<(DateTimeOffset? Start, DateTimeOffset End)> TrackRealExecutableAsync(
        string realExeName, Task treeExit)
    {
        var processName = Path.GetFileNameWithoutExtension(realExeName);
        var appearDeadline = DateTimeOffset.UtcNow + RealExeAppearTimeout;

        // Phase 1: wait for the real executable to appear.
        while (!IsProcessRunning(processName))
        {
            if (treeExit.IsCompleted || DateTimeOffset.UtcNow > appearDeadline)
                return (null, DateTimeOffset.UtcNow); // never appeared: fall back
            await Task.Delay(PollInterval);
        }

        var start = DateTimeOffset.UtcNow;

        // Phase 2: wait for the real executable to disappear.
        while (IsProcessRunning(processName))
        {
            await Task.Delay(PollInterval);
        }
        return (start, DateTimeOffset.UtcNow);
    }

    public async Task<int> ReconcileOpenSessionsAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var open = await db.PlaySessions.Where(s => s.EndedAt == null).ToListAsync();
        if (open.Count == 0)
            return 0;

        // Conservative policy: discard partial sessions of unknown duration.
        db.PlaySessions.RemoveRange(open);
        await db.SaveChangesAsync();
        return open.Count;
    }

    private async Task<int> CreateOpenSessionAsync(int gameId, DateTimeOffset startedAt)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var session = new PlaySession { GameId = gameId, StartedAt = startedAt };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task CloseSessionAsync(int sessionId, DateTimeOffset startedAt, DateTimeOffset endedAt)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var session = await db.PlaySessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null)
            return;

        if (endedAt < startedAt)
            endedAt = startedAt;

        session.StartedAt = startedAt;
        session.EndedAt = endedAt;
        session.DurationSeconds = (long)(endedAt - startedAt).TotalSeconds;
        await db.SaveChangesAsync();
    }

    private static Process? StartProcess(Game game)
    {
        var psi = new ProcessStartInfo
        {
            FileName = game.ExecutablePath,
            Arguments = game.LaunchArguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(game.WorkingDirectory)
                ? Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty
                : game.WorkingDirectory,
            UseShellExecute = false,
        };
        return Process.Start(psi);
    }

    private static void TryAssign(JobObjectTracker tracker, Process process)
    {
        try
        {
            tracker.Assign(process);
        }
        catch
        {
            // If assignment fails (rare), we still measure via the real-exe poller
            // or the process's own lifetime; do not abort the session.
        }
    }

    private static bool IsProcessRunning(string processName)
    {
        var procs = Process.GetProcessesByName(processName);
        try
        {
            return procs.Length > 0;
        }
        finally
        {
            foreach (var p in procs)
                p.Dispose();
        }
    }
}
