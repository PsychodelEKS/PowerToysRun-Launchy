using System.IO;
using System.Text.Json;
using Community.PowerToys.Run.Plugin.Launchy.Models;

namespace Community.PowerToys.Run.Plugin.Launchy.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string SettingsDirectory { get; }

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public string IndexPath => Path.Combine(SettingsDirectory, "index.json");

    public SettingsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsDirectory = Path.Combine(
            localAppData,
            "Microsoft",
            "PowerToys",
            "PowerToys Run",
            "Settings",
            "Plugins",
            "Community.PowerToys.Run.Plugin.Launchy");
    }

    public LaunchySettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return LaunchySettings.CreateDefault();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<LaunchySettings>(json, JsonOptions) ?? LaunchySettings.CreateDefault();
        }
        catch
        {
            return LaunchySettings.CreateDefault();
        }
    }

    public void SaveSettings(LaunchySettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public IReadOnlyList<IndexedEntry> LoadIndex()
    {
        try
        {
            if (!File.Exists(IndexPath))
            {
                return [];
            }

            var json = File.ReadAllText(IndexPath);
            return JsonSerializer.Deserialize<List<IndexedEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveIndex(IReadOnlyList<IndexedEntry> entries)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
