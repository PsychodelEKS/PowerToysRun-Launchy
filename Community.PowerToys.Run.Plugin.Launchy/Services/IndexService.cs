using System.IO;
using Community.PowerToys.Run.Plugin.Launchy.Models;

namespace Community.PowerToys.Run.Plugin.Launchy.Services;

public sealed class IndexService
{
    private readonly SettingsService _settingsService;
    private readonly SemaphoreSlim _rescanLock = new(1, 1);
    private IReadOnlyList<IndexedEntry> _entries;

    public IndexService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _entries = settingsService.LoadIndex();
    }

    public bool IsRescanRunning => _rescanLock.CurrentCount == 0;

    public IReadOnlyList<IndexedEntry> Entries => _entries;

    public async Task<int?> TryRescanAsync(LaunchySettings settings, CancellationToken cancellationToken = default)
    {
        if (!await _rescanLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            var entries = await Task.Run(() => BuildIndex(settings, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            _entries = entries;
            _settingsService.SaveIndex(entries);
            return entries.Count;
        }
        finally
        {
            _rescanLock.Release();
        }
    }

    public IReadOnlyList<SearchMatch> Search(
        string query,
        int limit = 30,
        bool includeBuiltInProgramDuplicates = false,
        bool includePathMatches = true)
    {
        query = query.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return [];
        }

        var matches = new List<SearchMatch>();
        foreach (var entry in _entries)
        {
            if (!includeBuiltInProgramDuplicates && entry.IsBuiltInProgramDuplicate)
            {
                continue;
            }

            var score = Score(entry, query, includePathMatches);
            if (score <= 0)
            {
                continue;
            }

            matches.Add(new SearchMatch { Entry = entry, Score = score });
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Entry.IsDirectory)
            .ThenBy(match => match.Entry.Name.Length)
            .ThenBy(match => match.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static List<IndexedEntry> BuildIndex(LaunchySettings settings, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new List<IndexedEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builtInProgramPaths = BuiltInProgramPathIndex.CreateDefault();

        foreach (var rule in settings.FolderRules.Where(rule => rule.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(rule.Path) || !Directory.Exists(rule.Path))
            {
                continue;
            }

            var extensions = ParseExtensions(rule.Extensions);
            var maxDepth = Math.Max(0, rule.MaxDepth);
            ScanDirectory(rule.Path, currentDepth: 0);

            void ScanDirectory(string directoryPath, int currentDepth)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ShouldSkip(directoryPath))
                {
                    return;
                }

                if (rule.IncludeDirectories && currentDepth > 0)
                {
                    AddEntry(directoryPath, isDirectory: true);
                }

                try
                {
                    foreach (var filePath in Directory.EnumerateFiles(directoryPath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (ShouldSkip(filePath) || !MatchesExtension(filePath, extensions))
                        {
                            continue;
                        }

                        AddEntry(filePath, isDirectory: false);
                    }

                    if (currentDepth >= maxDepth)
                    {
                        return;
                    }

                    foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath))
                    {
                        ScanDirectory(childDirectory, currentDepth + 1);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }
        }

        return entries;

        void AddEntry(string path, bool isDirectory)
        {
            var fullPath = Path.GetFullPath(path);
            if (!seen.Add(fullPath))
            {
                return;
            }

            entries.Add(new IndexedEntry
            {
                Name = isDirectory ? new DirectoryInfo(fullPath).Name : Path.GetFileName(fullPath),
                FullPath = fullPath,
                IsDirectory = isDirectory,
                IsBuiltInProgramDuplicate = !isDirectory && builtInProgramPaths.Contains(fullPath),
                IndexedAt = now,
            });
        }
    }

    private static int Score(IndexedEntry entry, string query, bool includePathMatches)
    {
        if (entry.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        if (entry.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 900;
        }

        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 700;
        }

        if (includePathMatches && entry.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 500;
        }

        return 0;
    }

    private static HashSet<string> ParseExtensions(string extensions)
    {
        return extensions
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.') ? extension : $".{extension}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesExtension(string filePath, HashSet<string> extensions)
    {
        return extensions.Count == 0 || extensions.Contains(Path.GetExtension(filePath));
    }

    private static bool ShouldSkip(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
        }
        catch
        {
            return true;
        }
    }
}
