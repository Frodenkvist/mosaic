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
    }
}
