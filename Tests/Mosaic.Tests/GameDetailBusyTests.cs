using Mosaic.Models;
using Mosaic.Services;
using Mosaic.ViewModels;

namespace Mosaic.Tests;

/// <summary>
/// While one achievement action runs, every achievement command must be disabled so a second
/// can't overlap an in-flight schema fetch / unlock scan.
/// </summary>
public class GameDetailBusyTests
{
    [Fact]
    public async Task RunningOneAchievementAction_DisablesAllAchievementCommands_UntilItCompletes()
    {
        var achievements = new GatedAchievementService();
        var vm = new GameDetailViewModel(
            new FakeGameLibrary(), new FakePlayTracker(), new NoOpArtworkService(),
            achievements, new NoOpDialogService(), new AchievementSettingsStub("key"));
        await vm.InitializeAsync(1);

        // Idle: every achievement command is runnable.
        Assert.True(vm.ScanUnlocksCommand.CanExecute(null));
        Assert.True(vm.RefreshAchievementsCommand.CanExecute(null));
        Assert.True(vm.FindAppIdCommand.CanExecute(null));
        Assert.True(vm.AddManualAchievementCommand.CanExecute(null));

        // Start a scan that blocks until we release it; the command sets IsAchievementBusy and awaits.
        var running = vm.ScanUnlocksCommand.ExecuteAsync(null);

        Assert.True(vm.IsAchievementBusy);
        Assert.False(vm.ScanUnlocksCommand.CanExecute(null));
        Assert.False(vm.RefreshAchievementsCommand.CanExecute(null));
        Assert.False(vm.FindAppIdCommand.CanExecute(null));
        Assert.False(vm.ApplyAppIdCommand.CanExecute(null));
        Assert.False(vm.UnlinkCommand.CanExecute(null));
        Assert.False(vm.AddManualAchievementCommand.CanExecute(null));
        Assert.False(vm.ToggleAchievementCommand.CanExecute(null));
        Assert.False(vm.GenerateSchemaCommand.CanExecute(null));

        // Let the scan finish; everything is runnable again.
        achievements.Release();
        await running;

        Assert.False(vm.IsAchievementBusy);
        Assert.True(vm.ScanUnlocksCommand.CanExecute(null));
        Assert.True(vm.RefreshAchievementsCommand.CanExecute(null));
        Assert.True(vm.FindAppIdCommand.CanExecute(null));
        Assert.True(vm.AddManualAchievementCommand.CanExecute(null));
    }

    /// <summary>Achievement service whose <see cref="ScanUnlocksAsync"/> blocks until released.</summary>
    private sealed class GatedAchievementService : IAchievementService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _gate.TrySetResult();

        public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked { add { } remove { } }
        public event EventHandler<int>? AchievementsChanged { add { } remove { } }
        public bool IsAutoResolutionAvailable => true;

        public async Task<ScanResult> ScanUnlocksAsync(int gameId, CancellationToken ct = default)
        {
            await _gate.Task;
            return new ScanResult(Array.Empty<Achievement>(), ScanDiagnostic.Empty);
        }

        public Task<IReadOnlyList<Achievement>> GetAchievementsAsync(int gameId) =>
            Task.FromResult<IReadOnlyList<Achievement>>(Array.Empty<Achievement>());
        public Task<(int Unlocked, int Total)> GetProgressAsync(int gameId) => Task.FromResult((0, 0));
        public Task<IReadOnlyList<SteamApp>> SuggestAppsAsync(int gameId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SteamApp>>(Array.Empty<SteamApp>());
        public Task LinkAppIdAsync(int gameId, int appId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetUnlinkedAsync(int gameId) => Task.CompletedTask;
        public Task SetSourceAsync(int gameId, bool enabled, AchievementSource source) => Task.CompletedTask;
        public Task RefreshAsync(int gameId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SchemaWriteResult> GenerateEmulatorSchemaAsync(int gameId, bool overwrite = false, CancellationToken ct = default) =>
            Task.FromResult(new SchemaWriteResult());
        public Task SetUnlockedAsync(int gameId, int achievementId, bool unlocked) => Task.CompletedTask;
        public Task<Achievement> AddManualAchievementAsync(int gameId, string displayName, string? description = null) =>
            Task.FromResult(new Achievement { GameId = gameId, ApiName = "m", DisplayName = displayName });
    }

    private sealed class FakeGameLibrary : IGameLibrary
    {
        public Task<Game?> GetGameAsync(int id) => Task.FromResult<Game?>(new Game { Id = id, Name = "Test" });
        public Task<GameStats> GetStatsAsync(int gameId) => Task.FromResult(GameStats.Empty);
        public Task<IReadOnlyList<GameListItem>> GetLibraryAsync() => throw new NotImplementedException();
        public Task<IReadOnlyList<GameListItem>> GetRecentlyPlayedAsync() => throw new NotImplementedException();
        public Task<Game> AddGameAsync(AddGameRequest request) => throw new NotImplementedException();
        public Task UpdateGameAsync(Game game) => throw new NotImplementedException();
        public Task RemoveGameAsync(int gameId) => throw new NotImplementedException();
        public Task<IReadOnlyList<ScanCandidate>> ScanFoldersAsync(IEnumerable<string> folders) => throw new NotImplementedException();
        public Task<IReadOnlyList<Game>> AddScannedGamesAsync(IEnumerable<ScanCandidate> confirmed) => throw new NotImplementedException();
    }

    private sealed class NoOpDialogService : IDialogService
    {
        public string? PickExecutable() => null;
        public string? PickFolder() => null;
        public string? PickImage() => null;
        public AddGameRequest? ShowAddGame() => null;
        public IReadOnlyList<ScanCandidate>? ShowScanResults(IReadOnlyList<ScanCandidate> candidates) => null;
        public IReadOnlyList<MediaScanCandidate>? ShowMediaScanResults(IReadOnlyList<MediaScanCandidate> candidates) => null;
        public void ShowGameDetail(int gameId) { }
        public void ShowMediaDetail(int mediaItemId) { }
        public bool Confirm(string message, string title) => false;
        public void ShowMessage(string message, string title) { }
    }
}
