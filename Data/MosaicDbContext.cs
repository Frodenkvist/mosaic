using Microsoft.EntityFrameworkCore;
using Mosaic.Models;

namespace Mosaic.Data;

public class MosaicDbContext : DbContext
{
    public MosaicDbContext(DbContextOptions<MosaicDbContext> options) : base(options)
    {
    }

    public DbSet<Game> Games => Set<Game>();
    public DbSet<PlaySession> PlaySessions => Set<PlaySession>();
    public DbSet<Artwork> Artwork => Set<Artwork>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<WatchSession> WatchSessions => Set<WatchSession>();
    public DbSet<MediaArtwork> MediaArtwork => Set<MediaArtwork>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var game = modelBuilder.Entity<Game>();
        game.HasKey(g => g.Id);
        game.Property(g => g.Name).IsRequired();
        game.Property(g => g.ExecutablePath).IsRequired();
        game.HasIndex(g => g.ExecutablePath).IsUnique();

        // Removing a game cascades to its sessions and artwork.
        game.HasMany(g => g.Sessions)
            .WithOne(s => s.Game!)
            .HasForeignKey(s => s.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        game.HasMany(g => g.Artwork)
            .WithOne(a => a.Game!)
            .HasForeignKey(a => a.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // Removing a game cascades to its achievements (definitions + unlock state).
        game.HasMany(g => g.Achievements)
            .WithOne(a => a.Game!)
            .HasForeignKey(a => a.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // New games track achievements by default; existing rows get this on migration.
        game.Property(g => g.AchievementTrackingEnabled).HasDefaultValue(true);

        modelBuilder.Entity<PlaySession>(s =>
        {
            s.HasKey(x => x.Id);
            s.HasIndex(x => x.GameId);
            s.HasIndex(x => x.EndedAt); // for open-session reconciliation & recently-played
        });

        modelBuilder.Entity<Artwork>(a =>
        {
            a.HasKey(x => x.Id);
            a.HasIndex(x => new { x.GameId, x.Kind });
        });

        modelBuilder.Entity<Achievement>(a =>
        {
            a.HasKey(x => x.Id);
            a.Property(x => x.ApiName).IsRequired();
            a.Property(x => x.DisplayName).IsRequired();
            // One row per achievement per game; the key matches across schema refreshes.
            a.HasIndex(x => new { x.GameId, x.ApiName }).IsUnique();
        });

        // Media domain (parallel to the game domain; entirely independent of it).
        var media = modelBuilder.Entity<MediaItem>();
        media.HasKey(m => m.Id);
        media.Property(m => m.Title).IsRequired();
        media.HasIndex(m => m.ParentId);
        // At most one media item per video file (SQLite treats NULLs as distinct, so the many
        // null FilePaths of Series rows are fine under this filtered unique index).
        media.HasIndex(m => m.FilePath).IsUnique().HasFilter("\"FilePath\" IS NOT NULL");

        // A series owns its episodes; removing it cascades to them (and, via the relationships
        // below, to each episode's watch sessions and artwork).
        media.HasMany(m => m.Episodes)
            .WithOne(m => m.Parent!)
            .HasForeignKey(m => m.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        media.HasMany(m => m.WatchSessions)
            .WithOne(w => w.MediaItem!)
            .HasForeignKey(w => w.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);

        media.HasMany(m => m.Artwork)
            .WithOne(a => a.MediaItem!)
            .HasForeignKey(a => a.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WatchSession>(w =>
        {
            w.HasKey(x => x.Id);
            w.HasIndex(x => x.MediaItemId);
            w.HasIndex(x => x.EndedAt); // open-session reconciliation & recently-watched
        });

        modelBuilder.Entity<MediaArtwork>(a =>
        {
            a.HasKey(x => x.Id);
            a.HasIndex(x => new { x.MediaItemId, x.Kind });
        });
    }
}
