namespace Mosaic.Services;

/// <summary>Serializable user settings persisted to settings.json.</summary>
public class AppSettings
{
    /// <summary>Folders the user wants Mosaic to scan for games.</summary>
    public List<string> ScanFolders { get; set; } = new();

    /// <summary>Folders the user wants Mosaic to scan (recursively) for movies and TV.</summary>
    public List<string> MediaFolders { get; set; } = new();

    /// <summary>SteamGridDB API key; null/empty disables artwork auto-fetch.</summary>
    public string? SteamGridDbApiKey { get; set; }

    /// <summary>TMDB API key; null/empty disables media poster/metadata auto-fetch.</summary>
    public string? TmdbApiKey { get; set; }

    /// <summary>
    /// Path to a preferred media player executable; null/empty (or a missing file) means Mosaic
    /// opens media with the operating system's default file association.
    /// </summary>
    public string? PreferredMediaPlayerPath { get; set; }

    /// <summary>Steam Web API key; null/empty disables achievement schema auto-resolution.</summary>
    public string? SteamWebApiKey { get; set; }

    /// <summary>
    /// Whether Mosaic checks for new versions in the background at startup. Defaults to enabled;
    /// an older settings.json that predates this field deserializes to this default (true).
    /// </summary>
    public bool AutomaticUpdatesEnabled { get; set; } = true;

    /// <summary>UTC time of the last completed update check, used to throttle automatic checks.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// Whether the transparent in-game overlay is shown over games launched through Mosaic (so
    /// achievement toasts appear on top of the running game). Defaults to enabled; an older
    /// settings.json that predates this field deserializes to this default (true).
    /// </summary>
    public bool GameOverlayEnabled { get; set; } = true;

    /// <summary>
    /// Whether a short sound plays when an achievement unlocks during play. Defaults to enabled;
    /// an older settings.json that predates this field deserializes to this default (true).
    /// </summary>
    public bool AchievementSoundEnabled { get; set; } = true;
}
