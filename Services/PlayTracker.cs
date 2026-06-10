using System.Collections.Concurrent;
using System.ComponentModel;
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

    /// <summary>Win32 <c>ERROR_CANCELLED</c>: the user dismissed the UAC elevation prompt.</summary>
    private const int ERROR_CANCELLED = 1223;

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

        // Start the game SUSPENDED, assign it to the job WHILE suspended, then resume.
        // This guarantees every descendant is captured by the job from its first
        // instruction, closing the race where a launcher could spawn the real game and
        // exit before a post-start assignment completes.
        SuspendedProcess suspended;
        try
        {
            suspended = SuspendedProcessLauncher.LaunchSuspended(
                game.ExecutablePath, game.LaunchArguments, ResolveWorkingDirectory(game));
        }
        catch (ElevationRequiredException)
        {
            // The executable's manifest demands administrator rights. CreateProcess cannot
            // elevate (only the shell can), so relaunch through ShellExecute, which raises the
            // UAC prompt — just as double-clicking it in Explorer does.
            return await LaunchElevatedAsync(game);
        }
        catch
        {
            return false; // the executable exists but could not be started; record nothing
        }

        var tracker = CreateTrackerAndAssign(suspended.Process);
        suspended.Resume();

        await StartTrackingAsync(game, tracker, suspended.Process);
        return true;
    }

    /// <summary>
    /// Launches a game whose manifest requires elevation through <c>ShellExecute</c>
    /// (<see cref="ProcessStartInfo.UseShellExecute"/>), which raises the UAC prompt. An
    /// elevated child cannot be captured by our medium-integrity job object, so the session is
    /// tracked by the launched process's own lifetime (or the <see cref="Game.RealExecutableName"/>
    /// poller, which works by process name and needs no handle).
    /// </summary>
    private async Task<bool> LaunchElevatedAsync(Game game)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = game.ExecutablePath,
                Arguments = game.LaunchArguments ?? string.Empty,
                WorkingDirectory = ResolveWorkingDirectory(game),
                UseShellExecute = true, // route through ShellExecuteEx so the manifest triggers UAC
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return false; // the user declined the UAC prompt; nothing to record
        }
        catch
        {
            return false; // the shell could not start it; record nothing
        }

        if (process is null)
            return false; // no new process was started (e.g. handed off to a running instance)

        await StartTrackingAsync(game, tracker: null, process);
        return true;
    }

    /// <summary>
    /// Records an open session for a just-launched <paramref name="process"/>, marks the game
    /// running, fires <see cref="SessionStarted"/>, and tracks the session to completion in the
    /// background. Shared by the suspended-launch and elevated-launch paths.
    /// </summary>
    private async Task StartTrackingAsync(Game game, JobObjectTracker? tracker, Process process)
    {
        // Persist an open session immediately so it survives an unexpected close.
        var startedAt = DateTimeOffset.UtcNow;
        var sessionId = await CreateOpenSessionAsync(game.Id, startedAt);
        _runningSince[game.Id] = startedAt;
        SessionStarted?.Invoke(this, game.Id);

        // Track to completion in the background; do not block the caller (the UI).
        _ = Task.Run(() => TrackToCompletionAsync(game, tracker, process, sessionId));
    }

    private async Task TrackToCompletionAsync(Game game, JobObjectTracker? tracker, Process process, int sessionId)
    {
        var startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset endedAt;
        try
        {
            // Whole-tree exit via the job; if the job could not be created/assigned, fall
            // back to the launched process's own lifetime.
            var treeExit = tracker is not null
                ? tracker.WaitForTreeExitAsync()
                : process.WaitForExitAsync();

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
            tracker?.Dispose();
            process.Dispose();
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

    private static string ResolveWorkingDirectory(Game game) =>
        string.IsNullOrWhiteSpace(game.WorkingDirectory)
            ? Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty
            : game.WorkingDirectory;

    /// <summary>
    /// Creates the job tracker and assigns the (suspended) process to it. Returns null if
    /// the job could not be created or the assignment failed (rare): the session is then
    /// still measured via the real-exe poller or the process's own lifetime — assignment
    /// failure never aborts the session.
    /// </summary>
    private static JobObjectTracker? CreateTrackerAndAssign(Process process)
    {
        JobObjectTracker? tracker = null;
        try
        {
            tracker = new JobObjectTracker();
            tracker.Assign(process);
            return tracker;
        }
        catch
        {
            tracker?.Dispose();
            return null;
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
