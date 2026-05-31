using Mosaic.Models;

namespace Mosaic.Services;

/// <summary>Aggregate play statistics derived from a game's sessions.</summary>
public record GameStats(TimeSpan TotalPlayTime, DateTimeOffset? LastPlayed, int SessionCount)
{
    public bool HasBeenPlayed => SessionCount > 0 && LastPlayed is not null;
    public static GameStats Empty { get; } = new(TimeSpan.Zero, null, 0);
}

/// <summary>A game plus its derived stats and cover-art path, for list/grid display.</summary>
public record GameListItem(Game Game, GameStats Stats, string? CoverPath);

/// <summary>Request to add a game manually.</summary>
public record AddGameRequest(
    string Name,
    string ExecutablePath,
    string? LaunchArguments = null,
    string? WorkingDirectory = null,
    string? RealExecutableName = null);

/// <summary>A candidate executable found during a folder scan, awaiting user confirmation.</summary>
public record ScanCandidate(string SuggestedName, string ExecutablePath);
