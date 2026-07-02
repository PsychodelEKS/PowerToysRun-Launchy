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
    private const string ActionKeyword = "ln";
    private const string RescanCommand = "rescan";

    private PluginInitContext? _context;
    private SettingsService? _settingsService;
    private IndexService? _indexService;
    private LaunchySettings _settings = LaunchySettings.CreateDefault();
    private string _iconPath = "Images\\Launchy.light.png";
    private bool _disposed;

    public string Name => "Launchy";

    public string Description => "Index and launch files from selected folders.";

    public static string PluginID => "f5a247f6c82a4b63a33ef0b88adff02a";

    public IEnumerable<PluginAdditionalOption> AdditionalOptions => [];

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
            SaveSettings,
            () => RescanInBackgroundAsync(showNotification: true));
    }

    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
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

        if (isKeywordQuery && IsRescanQuery(search))
        {
            return [CreateRescanResult(search)];
        }

        if (!isKeywordQuery && !_settings.EnableGlobalResults)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }

        return _indexService.Search(search)
            .Select(match => CreateEntryResult(match.Entry, match.Score, search))
            .ToList();
    }

    private Result CreateEntryResult(IndexedEntry entry, int score, string query)
    {
        return new Result
        {
            Title = entry.Name,
            SubTitle = entry.FullPath,
            QueryTextDisplay = query,
            IcoPath = _iconPath,
            Score = score,
            ContextData = entry,
            Action = _ => OpenEntry(entry),
        };
    }

    private Result CreateRescanResult(string query)
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
            Score = 1000,
            Action = context =>
            {
                _ = RescanInBackgroundAsync(showNotification: true);
                return true;
            },
        };
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
        if (string.Equals(query.ActionKeyword, ActionKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rawQuery = query.RawQuery ?? string.Empty;
        return rawQuery.Equals(ActionKeyword, StringComparison.OrdinalIgnoreCase)
            || rawQuery.StartsWith($"{ActionKeyword} ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRescanQuery(string search)
    {
        return search.Equals(RescanCommand, StringComparison.OrdinalIgnoreCase);
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

        _disposed = true;
    }
}
