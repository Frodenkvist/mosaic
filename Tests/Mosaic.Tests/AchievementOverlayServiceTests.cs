using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

public class AchievementOverlayServiceTests
{
    [Fact]
    public void SessionStarted_WhenOverlayEnabled_CreatesOverlay()
    {
        var h = new Harness();
        h.CreateService();

        h.Tracker.RaiseStarted(1);

        Assert.Equal(1, h.Factory.CreateCount);
    }

    [Fact]
    public void SessionStarted_WhenOverlayDisabled_DoesNotCreateOverlay()
    {
        var h = new Harness();
        h.Settings.Current.GameOverlayEnabled = false;
        h.CreateService();

        h.Tracker.RaiseStarted(1);

        Assert.Equal(0, h.Factory.CreateCount);
    }

    [Fact]
    public void SessionEnded_TearsDownOverlay()
    {
        var h = new Harness();
        h.CreateService();
        h.Tracker.RaiseStarted(1);
        var overlay = h.Factory.Last!;

        h.Tracker.RaiseEnded(1);

        Assert.Equal(1, overlay.DisposeCount);

        // A later unlock must not render on the torn-down overlay nor resurrect one.
        h.Achievements.RaiseUnlocked(1, "Game", "Ach");
        Assert.Empty(overlay.Toasts);
        Assert.Equal(1, h.Factory.CreateCount);
    }

    [Fact]
    public void ConcurrentSessions_KeepOverlayUntilLastEnds()
    {
        var h = new Harness();
        h.CreateService();

        h.Tracker.RaiseStarted(1);
        h.Tracker.RaiseStarted(2);
        var overlay = h.Factory.Last!;

        Assert.Equal(1, h.Factory.CreateCount);   // a single shared overlay

        h.Tracker.RaiseEnded(1);
        Assert.Equal(0, overlay.DisposeCount);     // second session still running

        h.Tracker.RaiseEnded(2);
        Assert.Equal(1, overlay.DisposeCount);     // torn down only when the last ends
    }

    [Fact]
    public void Unlock_ShowsToastOnActiveOverlay()
    {
        var h = new Harness();
        h.CreateService();
        h.Tracker.RaiseStarted(7);

        h.Achievements.RaiseUnlocked(7, "Hollow Knight", "Grimm Defeated", "C:\\art\\grimm.png");

        var toast = Assert.Single(h.Factory.Last!.Toasts);
        Assert.Equal("Grimm Defeated", toast.Title);
        Assert.Equal("Unlocked in Hollow Knight", toast.Subtitle);
        Assert.Equal("C:\\art\\grimm.png", toast.IconPath);
    }

    [Fact]
    public void Unlock_PlaysSound_WhenEnabled()
    {
        var h = new Harness();
        h.CreateService();
        h.Tracker.RaiseStarted(1);

        h.Achievements.RaiseUnlocked(1, "Game", "Ach");

        Assert.Equal(1, h.Sound.PlayCount);
    }

    [Fact]
    public void Unlock_DoesNotPlaySound_WhenDisabled()
    {
        var h = new Harness();
        h.Settings.Current.AchievementSoundEnabled = false;
        h.CreateService();
        h.Tracker.RaiseStarted(1);

        h.Achievements.RaiseUnlocked(1, "Game", "Ach");

        Assert.Equal(0, h.Sound.PlayCount);
    }

    [Fact]
    public void Unlock_WithNoOverlay_StillPlaysSound()
    {
        // Overlay disabled (exclusive-fullscreen / opted-out): the unlock must never be silent.
        var h = new Harness();
        h.Settings.Current.GameOverlayEnabled = false;
        h.CreateService();
        h.Tracker.RaiseStarted(1);

        h.Achievements.RaiseUnlocked(1, "Game", "Ach");

        Assert.Equal(0, h.Factory.CreateCount);
        Assert.Equal(1, h.Sound.PlayCount);
    }

    // ---- Test harness + fakes ----

    private sealed class Harness
    {
        public FakeTracker Tracker { get; } = new();
        public FakeAchievements Achievements { get; } = new();
        public FakeSettings Settings { get; } = new();
        public FakeOverlayFactory Factory { get; } = new();
        public FakeSound Sound { get; } = new();

        public AchievementOverlayService CreateService() =>
            new(Tracker, Achievements, Settings, Factory, Sound);
    }

    private sealed class FakeOverlay : IAchievementOverlay
    {
        public List<(string Title, string Subtitle, string? IconPath)> Toasts { get; } = new();
        public int DisposeCount { get; private set; }

        public void ShowToast(string title, string subtitle, string? iconPath) =>
            Toasts.Add((title, subtitle, iconPath));

        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeOverlayFactory : IAchievementOverlayFactory
    {
        public int CreateCount { get; private set; }
        public FakeOverlay? Last { get; private set; }

        public IAchievementOverlay Create()
        {
            CreateCount++;
            return Last = new FakeOverlay();
        }
    }

    private sealed class FakeSound : IAchievementSoundPlayer
    {
        public int PlayCount { get; private set; }
        public void Play() => PlayCount++;
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public event EventHandler? Changed;
        public Task SaveAsync() { Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    }

    private sealed class FakeTracker : IPlayTracker
    {
        public event EventHandler<int>? SessionStarted;
        public event EventHandler<int>? SessionEnded;

        public void RaiseStarted(int gameId) => SessionStarted?.Invoke(this, gameId);
        public void RaiseEnded(int gameId) => SessionEnded?.Invoke(this, gameId);

        public DateTimeOffset? GetRunningSince(int gameId) => null;
        public Task<bool> LaunchAsync(int gameId) => throw new NotSupportedException();
        public bool IsRunning(int gameId) => false;
        public Task<int> ReconcileOpenSessionsAsync() => throw new NotSupportedException();
    }

    private sealed class FakeAchievements : IAchievementService
    {
        public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
        public event EventHandler<int>? AchievementsChanged;

        public void RaiseUnlocked(int gameId, string gameName, string achievementName, string? iconPath = null) =>
            AchievementUnlocked?.Invoke(this, new AchievementUnlockedEventArgs
            {
                GameId = gameId,
                GameName = gameName,
                AchievementName = achievementName,
                IconPath = iconPath,
            });

        public bool IsAutoResolutionAvailable => false;
        public Task<IReadOnlyList<Achievement>> GetAchievementsAsync(int gameId) => throw new NotSupportedException();
        public Task<(int Unlocked, int Total)> GetProgressAsync(int gameId) => throw new NotSupportedException();
        public Task<IReadOnlyList<SteamApp>> SuggestAppsAsync(int gameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LinkAppIdAsync(int gameId, int appId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetUnlinkedAsync(int gameId) => throw new NotSupportedException();
        public Task SetSourceAsync(int gameId, bool enabled, AchievementSource source) => throw new NotSupportedException();
        public Task RefreshAsync(int gameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ScanResult> ScanUnlocksAsync(int gameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetUnlockedAsync(int gameId, int achievementId, bool unlocked) => throw new NotSupportedException();
        public Task<Achievement> AddManualAchievementAsync(int gameId, string displayName, string? description = null) => throw new NotSupportedException();

        // Keeps the secondary interface event "used" (invoked in source) to avoid CS0067; the
        // overlay service does not consume it, so no test needs to call this.
        private void RaiseAchievementsChanged(int gameId) => AchievementsChanged?.Invoke(this, gameId);
    }
}
