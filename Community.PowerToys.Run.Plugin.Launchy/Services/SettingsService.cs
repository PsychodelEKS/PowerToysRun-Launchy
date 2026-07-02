using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public string PowerToysRunSettingsPath { get; }

    public SettingsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var powerToysRunDirectory = Path.Combine(localAppData, "Microsoft", "PowerToys", "PowerToys Run");
        SettingsDirectory = Path.Combine(
            powerToysRunDirectory,
            "Settings",
            "Plugins",
            "Community.PowerToys.Run.Plugin.Launchy");
        PowerToysRunSettingsPath = Path.Combine(powerToysRunDirectory, "settings.json");
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

    public void SavePowerToysPluginOptions(
        string pluginId,
        string folderRulesOptionKey,
        string folderRulesText,
        bool enableGlobalResults)
    {
        if (!File.Exists(PowerToysRunSettingsPath))
        {
            return;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(PowerToysRunSettingsPath)) as JsonObject;
            var plugins = root?["plugins"] as JsonArray;
            if (root is null || plugins is null)
            {
                return;
            }

            var launchyPlugin = plugins
                .OfType<JsonObject>()
                .FirstOrDefault(plugin =>
                    string.Equals(plugin["Id"]?.GetValue<string>(), pluginId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(plugin["Name"]?.GetValue<string>(), "Launchy", StringComparison.OrdinalIgnoreCase));

            if (launchyPlugin is null)
            {
                return;
            }

            launchyPlugin["IsGlobal"] = enableGlobalResults;

            if (launchyPlugin["AdditionalOptions"] is not JsonArray additionalOptions)
            {
                additionalOptions = [];
                launchyPlugin["AdditionalOptions"] = additionalOptions;
            }

            var folderRulesOption = additionalOptions
                .OfType<JsonObject>()
                .FirstOrDefault(option =>
                    string.Equals(option["Key"]?.GetValue<string>(), folderRulesOptionKey, StringComparison.OrdinalIgnoreCase));

            if (folderRulesOption is null)
            {
                folderRulesOption = new JsonObject
                {
                    ["PluginOptionType"] = 4,
                    ["Key"] = folderRulesOptionKey,
                    ["DisplayLabel"] = "Folder rules",
                    ["DisplayDescription"] = "Same list used by ln settings. One rule per line: path | extensions | maxDepth | includeDirectories | enabled",
                    ["Value"] = false,
                    ["ComboBoxValue"] = 0,
                    ["NumberValue"] = 0,
                    ["PlaceholderText"] = @"C:\Tools | .exe;.lnk | 10 | false | true",
                };
                additionalOptions.Add(folderRulesOption);
            }

            folderRulesOption["TextValue"] = folderRulesText.Replace("\r\n", "\r", StringComparison.Ordinal).Replace("\n", "\r", StringComparison.Ordinal);

            File.WriteAllText(PowerToysRunSettingsPath, root.ToJsonString(JsonOptions));
        }
        catch
        {
        }
    }
}
