namespace Mosaic.Services;

/// <summary>Serializable user settings persisted to settings.json.</summary>
public class AppSettings
{
    /// <summary>Folders the user wants Mosaic to scan for games.</summary>
    public List<string> ScanFolders { get; set; } = new();

    /// <summary>SteamGridDB API key; null/empty disables artwork auto-fetch.</summary>
    public string? SteamGridDbApiKey { get; set; }

    /// <summary>Steam Web API key; null/empty disables achievement schema auto-resolution.</summary>
    public string? SteamWebApiKey { get; set; }

    /// <summary>
    /// Whether Mosaic checks for new versions in the background at startup. Defaults to enabled;
    /// an older settings.json that predates this field deserializes to this default (true).
    /// </summary>
    public bool AutomaticUpdatesEnabled { get; set; } = true;

    /// <summary>UTC time of the last completed update check, used to throttle automatic checks.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }
}
