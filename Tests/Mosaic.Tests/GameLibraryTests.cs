using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

internal sealed class NoOpArtworkService : IArtworkService
{
    public event EventHandler<int>? ArtworkUpdated { add { } remove { } }
    public Task FetchArtworkAsync(int gameId, bool refetch = false, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public Task<string> SetManualOverrideAsync(int gameId, ArtworkKind kind, string sourceImagePath) =>
        Task.FromResult(sourceImagePath);
    public Task FetchMissingForAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class GameLibraryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mosaic_lib_{Guid.NewGuid():N}.db");
    private readonly string _tempExe = Path.Combine(Path.GetTempPath(), $"mosaic_game_{Guid.NewGuid():N}.exe");
    private readonly DbContextOptions<MosaicDbContext> _options;
    private readonly AppPaths _paths = new();

    public GameLibraryTests()
    {
        _options = new DbContextOptionsBuilder<MosaicDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var db = new MosaicDbContext(_options);
        db.Database.EnsureCreated();
        File.WriteAllText(_tempExe, "stub");
    }

    private GameLibrary NewLibrary() =>
        new(new TestDbContextFactory(_options), _paths, new NoOpArtworkService());

    [Fact]
    public async Task AddedGame_PersistsAcrossNewFactory()
    {
        var added = await NewLibrary().AddGameAsync(new AddGameRequest("My Game", _tempExe));

        // Simulate an app restart: a brand-new library/factory over the same file.
        var library = NewLibrary();
        var all = await library.GetLibraryAsync();

        Assert.Single(all);
        Assert.Equal("My Game", all[0].Game.Name);
        Assert.Equal(added.Id, all[0].Game.Id);
    }

    [Fact]
    public async Task AddGame_RejectsDuplicateExecutable()
    {
        var library = NewLibrary();
        await library.AddGameAsync(new AddGameRequest("First", _tempExe));
        await Assert.ThrowsAsync<DuplicateExecutableException>(() =>
            library.AddGameAsync(new AddGameRequest("Second", _tempExe)));
    }

    [Fact]
    public async Task RemoveGame_LeavesExecutableOnDisk()
    {
        var library = NewLibrary();
        var game = await library.AddGameAsync(new AddGameRequest("Temp", _tempExe));
        await library.RemoveGameAsync(game.Id);

        Assert.Empty(await library.GetLibraryAsync());
        Assert.True(File.Exists(_tempExe), "Removing a game must not delete its executable.");
    }

    [Fact]
    public async Task Scan_GroupsByGameFolder_AndPicksTheRealExecutable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mosaic_scan_{Guid.NewGuid():N}");
        try
        {
            // A games library with 3 game folders, each cluttered with junk executables.
            Game("DOOM The Dark Ages", "DOOMTheDarkAges.exe", 50_000);
            Junk("DOOM The Dark Ages", "BsSndRpt64.exe");
            Junk("DOOM The Dark Ages", "idTechLauncher.exe");

            Game("God of War Ragnarok", "GoWR.exe", 40_000);
            Junk("God of War Ragnarok", "crs-handler.exe");
            Junk("God of War Ragnarok", "crs-uploader.exe");

            // Nested UE game: real binary is the larger -Shipping under Binaries\Win64.
            Write(@"Dispatch\Dispatch.exe", 2_000);
            Write(@"Dispatch\Dispatch\Binaries\Win64\Dispatch-Win64-Shipping.exe", 60_000);
            Write(@"Dispatch\_CommonRedist\oalinst.exe", 1_000);

            var candidates = await NewLibrary().ScanFoldersAsync(new[] { root });

            Assert.Equal(3, candidates.Count); // one entry per game folder, not per exe

            var doom = candidates.Single(c => c.SuggestedName == "DOOM The Dark Ages");
            Assert.EndsWith("DOOMTheDarkAges.exe", doom.ExecutablePath);

            var gow = candidates.Single(c => c.SuggestedName == "God of War Ragnarok");
            Assert.EndsWith("GoWR.exe", gow.ExecutablePath);

            var dispatch = candidates.Single(c => c.SuggestedName == "Dispatch");
            Assert.EndsWith("Dispatch-Win64-Shipping.exe", dispatch.ExecutablePath);

            // No junk leaked through as a chosen executable.
            Assert.DoesNotContain(candidates, c =>
                c.ExecutablePath.Contains("crs-", StringComparison.OrdinalIgnoreCase) ||
                c.ExecutablePath.Contains("SndRpt", StringComparison.OrdinalIgnoreCase) ||
                c.ExecutablePath.Contains("oalinst", StringComparison.OrdinalIgnoreCase) ||
                c.ExecutablePath.Contains("idTechLauncher", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        void Game(string folder, string exe, int size) => Write(Path.Combine(folder, exe), size);
        void Junk(string folder, string exe) => Write(Path.Combine(folder, exe), 500);
        void Write(string relative, int size)
        {
            var full = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[size]);
        }
    }

    [Fact]
    public async Task Scan_SkipsGameFoldersAlreadyInLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mosaic_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Celeste"));
        var exe = Path.Combine(root, "Celeste", "Celeste.exe");
        File.WriteAllBytes(exe, new byte[10_000]);
        try
        {
            var library = NewLibrary();
            await library.AddGameAsync(new AddGameRequest("Celeste", exe));

            var candidates = await library.ScanFoldersAsync(new[] { root });
            Assert.Empty(candidates); // already added -> its folder is skipped
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_tempExe); } catch { }
    }
}
