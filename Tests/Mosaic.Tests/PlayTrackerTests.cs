using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

/// <summary>Minimal in-test factory backed by a fixed set of options.</summary>
internal sealed class TestDbContextFactory : IDbContextFactory<MosaicDbContext>
{
    private readonly DbContextOptions<MosaicDbContext> _options;
    public TestDbContextFactory(DbContextOptions<MosaicDbContext> options) => _options = options;
    public MosaicDbContext CreateDbContext() => new(_options);
}

public class PlayTrackerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mosaic_test_{Guid.NewGuid():N}.db");
    private readonly TestDbContextFactory _factory;

    public PlayTrackerTests()
    {
        var options = new DbContextOptionsBuilder<MosaicDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _factory = new TestDbContextFactory(options);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task LaunchAsync_ReturnsFalse_AndRecordsNothing_WhenExecutableMissing()
    {
        int gameId;
        await using (var db = _factory.CreateDbContext())
        {
            var game = new Game
            {
                Name = "Ghost",
                ExecutablePath = Path.Combine(Path.GetTempPath(), "does-not-exist-12345.exe"),
                DateAdded = DateTimeOffset.UtcNow,
            };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            gameId = game.Id;
        }

        var tracker = new PlayTracker(_factory);
        var launched = await tracker.LaunchAsync(gameId);

        Assert.False(launched);
        await using var verify = _factory.CreateDbContext();
        Assert.Empty(verify.PlaySessions);
    }

    [Fact]
    public async Task ReconcileOpenSessions_DiscardsSessionsLeftOpen()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var game = new Game { Name = "G", ExecutablePath = @"C:\g.exe", DateAdded = DateTimeOffset.UtcNow };
            db.Games.Add(game);
            await db.SaveChangesAsync();

            db.PlaySessions.Add(new PlaySession { GameId = game.Id, StartedAt = DateTimeOffset.UtcNow, EndedAt = null });
            db.PlaySessions.Add(new PlaySession
            {
                GameId = game.Id,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                EndedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                DurationSeconds = 1800,
            });
            await db.SaveChangesAsync();
        }

        var tracker = new PlayTracker(_factory);
        var discarded = await tracker.ReconcileOpenSessionsAsync();

        Assert.Equal(1, discarded);
        await using var verify = _factory.CreateDbContext();
        Assert.Single(verify.PlaySessions);                       // the completed one remains
        Assert.All(verify.PlaySessions, s => Assert.NotNull(s.EndedAt));
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
