using System.Diagnostics;
using Mosaic.Services;

namespace Mosaic.Tests;

public class JobObjectTrackerTests
{
    /// <summary>
    /// The canonical "launcher exits early, real game keeps running" case:
    /// a parent process spawns a longer-lived child and then exits. The tracker
    /// must keep the session open until the WHOLE tree exits, not just the parent.
    /// </summary>
    [Fact]
    public async Task WaitForTreeExit_SpansChildLifetime_AfterParentExits()
    {
        using var tracker = new JobObjectTracker();

        // Parent pings ~1s (giving us a window to assign), spawns a detached child
        // that pings ~5s, then the parent exits immediately afterwards.
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"ping -n 2 127.0.0.1 >nul & start /b ping -n 6 127.0.0.1 >nul\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process = Process.Start(psi)!;
        tracker.Assign(process);

        var sw = Stopwatch.StartNew();
        var treeExit = tracker.WaitForTreeExitAsync();

        // The launched (parent) process exits well before the child.
        await process.WaitForExitAsync();
        var parentExitMs = sw.ElapsedMilliseconds;

        // The tree-exit must complete only once the child ping finishes.
        var completed = await Task.WhenAny(treeExit, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(treeExit, completed); // did not time out
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds > parentExitMs + 1500,
            $"Tree exit ({sw.ElapsedMilliseconds}ms) should outlast parent exit ({parentExitMs}ms) by the child's lifetime.");
    }

    [Fact]
    public async Task WaitForTreeExit_CompletesPromptly_WhenSingleProcessExits()
    {
        using var tracker = new JobObjectTracker();
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"ping -n 3 127.0.0.1 >nul\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        tracker.Assign(process);

        var completed = await Task.WhenAny(tracker.WaitForTreeExitAsync(), Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(completed is Task t && t.IsCompletedSuccessfully);
    }
}
