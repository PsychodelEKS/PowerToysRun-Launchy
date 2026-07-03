using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Community.PowerToys.Run.Plugin.Launchy.Models;
using Community.PowerToys.Run.Plugin.Launchy.Services;
using Community.PowerToys.Run.Plugin.Launchy.UI;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.Launchy;

public sealed class Main : IPlugin, IPluginI18n, IContextMenu, ISettingProvider, IReloadable, IDisposable, IDelayedExecutionPlugin
{
    private const string DefaultActionKeyword = "ln";
    private const string RescanCommand = "rescan";
    private const string SettingsCommand = "settings";
    private const int ExactCommandScore = 1100;
    private const int SuggestedCommandScore = 100;
    private const string FolderRulesOptionKey = "folderRules";
    private const string DefaultExtensions = ".exe;.lnk";
    private const int DefaultMaxDepth = 10;

    private PluginInitContext? _context;
    private SettingsService? _settingsService;
    private IndexService? _indexService;
    private LaunchySettings _settings = LaunchySettings.CreateDefault();
    private string _iconPath = "Images\\Launchy.light.png";
    private Window? _settingsWindow;
    private bool _disposed;

    public string Name => "Launchy";

    public string Description => "Index and launch files from selected folders.";

    public static string PluginID => "f5a247f6c82a4b63a33ef0b88adff02a";

    public IEnumerable<PluginAdditionalOption> AdditionalOptions
    {
        get
        {
            var serializedFolderRules = SerializeFolderRules(_settings.FolderRules);
            return
            [
                new()
                {
                    PluginOptionType = PluginAdditionalOption.AdditionalOptionType.MultilineTextbox,
                    Key = FolderRulesOptionKey,
                    DisplayLabel = "Folder rules",
                    DisplayDescription = "Same list used by ln settings. One rule per line: path | extensions | maxDepth | includeDirectories | enabled | matchDirectoryNames",
                    TextValue = string.Join(Environment.NewLine, serializedFolderRules),
                    TextValueAsMultilineList = serializedFolderRules,
                    PlaceholderText = $@"C:\Tools | {DefaultExtensions} | {DefaultMaxDepth} | false | true | false",
                },
            ];
        }
    }

    public void Init(PluginInitContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _context.API.ThemeChanged += OnThemeChanged;
        UpdateIconPath(_context.API.GetCurrentTheme());

        _settingsService = new SettingsService();
        _settings = _settingsService.LoadSettings();
        _indexService = new IndexService(_settingsService);
        _ = RescanInBackgroundAsync(showNotification: false);
    }

    public List<Result> Query(Query query)
    {
        return QueryInternal(query);
    }

    public List<Result> Query(Query query, bool delayedExecution)
    {
        return QueryInternal(query);
    }

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not IndexedEntry entry)
        {
            return [];
        }

        return
        [
            new ContextMenuResult
            {
                Title = "Open",
                Glyph = "\xE8A7",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                AcceleratorKey = Key.Enter,
                Action = _ => OpenEntry(entry),
            },
            new ContextMenuResult
            {
                Title = "Open containing folder",
                Glyph = "\xE838",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                AcceleratorKey = Key.E,
                AcceleratorModifiers = ModifierKeys.Control,
                Action = _ => OpenContainingFolder(entry),
            },
            new ContextMenuResult
            {
                Title = "Copy path",
                Glyph = "\xE8C8",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                AcceleratorKey = Key.C,
                AcceleratorModifiers = ModifierKeys.Control,
                Action = _ =>
                {
                    System.Windows.Clipboard.SetText(entry.FullPath);
                    return true;
                },
            },
            new ContextMenuResult
            {
                Title = "Rescan index",
                Glyph = "\xE72C",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                AcceleratorKey = Key.R,
                AcceleratorModifiers = ModifierKeys.Control,
                Action = context =>
                {
                    _ = RescanInBackgroundAsync(showNotification: true);
                    return true;
                },
            },
        ];
    }

    public System.Windows.Controls.Control CreateSettingPanel()
    {
        return new SettingsPanel(
            _settings.Clone(),
            SaveSettingsFromGui,
            () => RescanInBackgroundAsync(showNotification: true));
    }

    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        if (_settingsService is null)
        {
            return;
        }

        var updatedSettings = _settings.Clone();
        updatedSettings.EnableGlobalResults = settings.IsGlobal;

        var folderRulesOption = settings.AdditionalOptions?
            .FirstOrDefault(option => string.Equals(option.Key, FolderRulesOptionKey, StringComparison.OrdinalIgnoreCase));
        if (folderRulesOption is not null)
        {
            updatedSettings.FolderRules = ParseFolderRules(folderRulesOption).ToList();
        }

        SaveSettings(updatedSettings);
        _ = RescanInBackgroundAsync(showNotification: false);
    }

    public void ReloadData()
    {
        if (_settingsService is null)
        {
            return;
        }

        _settings = _settingsService.LoadSettings();
        _ = RescanInBackgroundAsync(showNotification: false);
    }

    public string GetTranslatedPluginTitle()
    {
        return Name;
    }

    public string GetTranslatedPluginDescription()
    {
        return Description;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private List<Result> QueryInternal(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_indexService is null)
        {
            return [];
        }

        var isKeywordQuery = IsKeywordQuery(query);
        var search = query.Search?.Trim() ?? string.Empty;

        if (!isKeywordQuery && !_settings.EnableGlobalResults)
        {
            return [];
        }

        var results = new List<Result>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            results.AddRange(_indexService.Search(
                    search,
                    includeBuiltInProgramDuplicates: isKeywordQuery)
                .Select(match => CreateEntryResult(match, search)));
        }

        if (isKeywordQuery)
        {
            results.AddRange(CreateCommandResults(search));
        }

        return results
            .OrderByDescending(result => result.Score)
            .ToList();
    }

    private Result CreateEntryResult(SearchMatch match, string query)
    {
        var entry = match.Entry;
        return new Result
        {
            Title = entry.Name,
            SubTitle = entry.FullPath,
            QueryTextDisplay = query,
            IcoPath = GetEntryIconPath(entry),
            Score = match.Score,
            TitleHighlightData = match.TitleHighlightData,
            ContextData = entry,
            Action = _ => OpenEntry(entry),
        };
    }

    private IEnumerable<Result> CreateCommandResults(string query)
    {
        if (ShouldShowCommand(query, RescanCommand))
        {
            yield return CreateRescanResult(query, GetCommandScore(query, RescanCommand));
        }

        if (ShouldShowCommand(query, SettingsCommand))
        {
            yield return CreateSettingsResult(query, GetCommandScore(query, SettingsCommand));
        }
    }

    private Result CreateRescanResult(string query, int score)
    {
        var subtitle = _indexService?.IsRescanRunning == true
            ? "Index rebuild is already running."
            : "Rebuild the Launchy index in the background.";

        return new Result
        {
            Title = "Rescan Launchy index",
            SubTitle = subtitle,
            QueryTextDisplay = query,
            IcoPath = _iconPath,
            Score = score,
            Action = context =>
            {
                _ = RescanInBackgroundAsync(showNotification: true);
                return true;
            },
        };
    }

    private Result CreateSettingsResult(string query, int score)
    {
        return new Result
        {
            Title = "Open Launchy settings",
            SubTitle = "Edit folder rules with a table and folder picker.",
            QueryTextDisplay = query,
            IcoPath = _iconPath,
            Score = score,
            Action = _ =>
            {
                OpenSettingsWindow();
                return true;
            },
        };
    }

    private string GetEntryIconPath(IndexedEntry entry)
    {
        return EntryExists(entry) ? entry.FullPath : _iconPath;
    }

    private static bool EntryExists(IndexedEntry entry)
    {
        return entry.IsDirectory ? Directory.Exists(entry.FullPath) : File.Exists(entry.FullPath);
    }

    private async Task<int?> RescanInBackgroundAsync(bool showNotification)
    {
        if (_indexService is null)
        {
            return null;
        }

        try
        {
            var count = await _indexService.TryRescanAsync(_settings).ConfigureAwait(false);
            if (showNotification)
            {
                if (count is null)
                {
                    ShowNotification("Launchy index", "Index rebuild is already running.");
                }
                else
                {
                    ShowNotification("Launchy index", $"Indexed {count.Value} item(s).");
                }
            }

            return count;
        }
        catch (Exception ex)
        {
            if (showNotification)
            {
                ShowNotification("Launchy index", $"Rescan failed: {ex.Message}");
            }

            return null;
        }
    }

    private void SaveSettings(LaunchySettings settings)
    {
        _settings = settings.Clone();
        _settingsService?.SaveSettings(_settings);
    }

    private void SaveSettingsFromGui(LaunchySettings settings)
    {
        SaveSettings(settings);
        _settingsService?.SavePowerToysPluginOptions(
            PluginID,
            FolderRulesOptionKey,
            GetFolderRulesText(_settings.FolderRules),
            _settings.EnableGlobalResults);
    }

    private void OpenSettingsWindow()
    {
        if (_context is null)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new Window
            {
                Title = "Launchy settings",
                Width = 900,
                Height = 440,
                MinWidth = 720,
                MinHeight = 320,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new SettingsPanel(
                    _settings.Clone(),
                    SaveSettingsFromGui,
                    () => RescanInBackgroundAsync(showNotification: true)),
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private static List<string> SerializeFolderRules(IEnumerable<LaunchyFolderRule> rules)
    {
        return rules
            .Select(rule => string.Join(
                " | ",
                rule.Path,
                string.IsNullOrWhiteSpace(rule.Extensions) ? DefaultExtensions : rule.Extensions,
                Math.Max(0, rule.MaxDepth).ToString(),
                rule.IncludeDirectories.ToString().ToLowerInvariant(),
                rule.Enabled.ToString().ToLowerInvariant(),
                rule.MatchDirectoryNames.ToString().ToLowerInvariant()))
            .ToList();
    }

    private static string GetFolderRulesText(IEnumerable<LaunchyFolderRule> rules)
    {
        return string.Join(Environment.NewLine, SerializeFolderRules(rules));
    }

    private static IEnumerable<LaunchyFolderRule> ParseFolderRules(PluginAdditionalOption option)
    {
        var lines = option.TextValueAsMultilineList;
        if ((lines is null || lines.Count == 0) && !string.IsNullOrWhiteSpace(option.TextValue))
        {
            lines = option.TextValue
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .ToList();
        }

        if (lines is null)
        {
            yield break;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('|').Select(part => TrimQuotes(part.Trim())).ToArray();
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            yield return new LaunchyFolderRule
            {
                Path = parts[0],
                Extensions = GetPart(parts, 1, DefaultExtensions),
                MaxDepth = Math.Max(0, ParseInt(GetPart(parts, 2, DefaultMaxDepth.ToString()), DefaultMaxDepth)),
                IncludeDirectories = ParseBool(GetPart(parts, 3, bool.FalseString), defaultValue: false),
                Enabled = ParseBool(GetPart(parts, 4, bool.TrueString), defaultValue: true),
                MatchDirectoryNames = ParseBool(GetPart(parts, 5, bool.FalseString), defaultValue: false),
            };
        }
    }

    private static string GetPart(IReadOnlyList<string> parts, int index, string defaultValue)
    {
        return index < parts.Count && !string.IsNullOrWhiteSpace(parts[index])
            ? parts[index]
            : defaultValue;
    }

    private static int ParseInt(string value, int defaultValue)
    {
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => defaultValue,
        };
    }

    private static string TrimQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private void ShowNotification(string title, string message)
    {
        try
        {
            _context?.API.ShowNotification(title, message);
        }
        catch
        {
        }
    }

    private static bool OpenEntry(IndexedEntry entry)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.FullPath,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool OpenContainingFolder(IndexedEntry entry)
    {
        try
        {
            var folder = entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = entry.IsDirectory ? $"\"{folder}\"" : $"/select,\"{entry.FullPath}\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKeywordQuery(Query query)
    {
        if (!string.IsNullOrWhiteSpace(query.ActionKeyword) &&
            !string.Equals(query.ActionKeyword, "*", StringComparison.Ordinal))
        {
            return true;
        }

        var rawQuery = query.RawQuery ?? string.Empty;
        return rawQuery.Equals(DefaultActionKeyword, StringComparison.OrdinalIgnoreCase)
            || rawQuery.StartsWith($"{DefaultActionKeyword} ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldShowCommand(string search, string command)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return command.StartsWith(search, StringComparison.OrdinalIgnoreCase)
            || search.StartsWith(command, StringComparison.OrdinalIgnoreCase)
            || !search.Contains(' ', StringComparison.Ordinal);
    }

    private static int GetCommandScore(string search, string command)
    {
        return search.Equals(command, StringComparison.OrdinalIgnoreCase)
            ? ExactCommandScore
            : SuggestedCommandScore;
    }

    private void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        UpdateIconPath(newTheme);
    }

    private void UpdateIconPath(Theme theme)
    {
        _iconPath = theme is Theme.Light or Theme.HighContrastWhite
            ? "Images\\Launchy.light.png"
            : "Images\\Launchy.dark.png";
    }

    private void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        if (_context is not null)
        {
            _context.API.ThemeChanged -= OnThemeChanged;
        }

        _settingsWindow?.Close();
        _settingsWindow = null;

        _disposed = true;
    }
}
