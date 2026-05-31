namespace Mosaic.Services;

public interface ISettingsService
{
    /// <summary>Current in-memory settings (loaded on first access).</summary>
    AppSettings Current { get; }

    /// <summary>Raised after settings are saved.</summary>
    event EventHandler? Changed;

    /// <summary>Persists the current settings to disk.</summary>
    Task SaveAsync();
}
