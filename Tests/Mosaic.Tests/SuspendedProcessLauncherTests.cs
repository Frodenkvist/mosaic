using System.Diagnostics;
using Mosaic.Services;

namespace Mosaic.Tests;

public class SuspendedProcessLauncherTests
{
    private static string Cmd => Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";

    /// <summary>
    /// The race this change closes: a launcher spawns the real game and exits IMMEDIATELY
    /// (no delay window). Because the suspended process is assigned to the job BEFORE it runs,
    /// the descendant is captured from its first instruction, so the session spans the
    /// descendant's lifetime even though the parent is long gone. A post-start assignment
    /// would lose this race and miss the child entirely.
    /// </summary>
    [Fact]
    public async Task SuspendedLaunch_TracksDescendant_EvenWhenParentExitsImmediately()
    {
        using var tracker = new JobObjectTracker();

        // Parent spawns a detached child that pings ~5s, then exits at once (no leading delay).
        var suspended = SuspendedProcessLauncher.LaunchSuspended(
            Cmd, "/c \"start /b ping -n 6 127.0.0.1 >nul\"", workingDirectory: null);

        tracker.Assign(suspended.Process); // assign WHILE suspended — before anything runs
        suspended.Resume();                // only now does the parent (and its child) execute

        var sw = Stopwatch.StartNew();
        var treeExit = tracker.WaitForTreeExitAsync();

        await suspended.Process.WaitForExitAsync();
        var parentExitMs = sw.ElapsedMilliseconds;

        var completed = await Task.WhenAny(treeExit, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(treeExit, completed); // did not time out
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds > parentExitMs + 1500,
            $"Tree exit ({sw.ElapsedMilliseconds}ms) should outlast the immediately-exiting parent ({parentExitMs}ms) by the child's lifetime.");
    }

    /// <summary>
    /// The launched process must receive its configured arguments and run in the configured
    /// working directory through the raw CreateProcess path, exactly as the old
    /// Process.Start path did.
    /// </summary>
    [Fact]
    public async Task LaunchSuspended_HonorsArgumentsAndWorkingDirectory()
    {
        var dir = Directory.CreateTempSubdirectory("mosaic_wd_");
        var outFile = Path.Combine(dir.FullName, "out.txt");
        const string marker = "MOSAIC_ARG_OK";
        try
        {
            // Arguments echo a marker; the RELATIVE redirect target proves the working dir.
            var suspended = SuspendedProcessLauncher.LaunchSuspended(
                Cmd, $"/c echo {marker}>out.txt", dir.FullName);
            suspended.Resume();
            await suspended.Process.WaitForExitAsync();

            Assert.True(File.Exists(outFile), "Output file should be created in the configured working directory.");
            Assert.Contains(marker, await File.ReadAllTextAsync(outFile));
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
