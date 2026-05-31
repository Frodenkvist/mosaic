using System.IO;
using System.Text.Json;

namespace Mosaic.Services;

public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
        _settings = Load(paths.SettingsPath);
    }

    public AppSettings Current => _settings;

    public event EventHandler? Changed;

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        await File.WriteAllTextAsync(_paths.SettingsPath, json);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt settings file: fall back to defaults rather than crashing on startup.
        }
        return new AppSettings();
    }
}
