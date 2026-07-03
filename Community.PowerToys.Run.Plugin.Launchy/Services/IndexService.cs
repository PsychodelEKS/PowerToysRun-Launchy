using System.IO;
using System.Text;
using Community.PowerToys.Run.Plugin.Launchy.Models;
using Wox.Infrastructure;

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
        bool includeBuiltInProgramDuplicates = false)
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

            var match = Score(entry, query);
            if (match.Score <= 0)
            {
                continue;
            }

            matches.Add(new SearchMatch { Entry = entry, Score = match.Score, TitleHighlightData = match.TitleHighlightData });
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
                    AddEntry(directoryPath, isDirectory: true, rule.MatchDirectoryNames);
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

                        AddEntry(filePath, isDirectory: false, rule.MatchDirectoryNames);
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

        void AddEntry(string path, bool isDirectory, bool matchDirectoryNames)
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
                MatchDirectoryNames = matchDirectoryNames,
                IndexedAt = now,
            });
        }
    }

    private static SearchScore Score(IndexedEntry entry, string query)
    {
        var nameMatch = FuzzySearch(query, entry.Name);
        var bestScore = new SearchScore(nameMatch.Score, nameMatch.MatchData);

        if (entry.MatchDirectoryNames && DirectoryNameMatches(entry.FullPath, query))
        {
            var directoryScore = ScoreDirectoryNameMatch(entry.FullPath, query);
            if (directoryScore > bestScore.Score)
            {
                bestScore = new SearchScore(directoryScore, TitleHighlightData: null);
            }
        }

        return bestScore;
    }

    private static bool DirectoryNameMatches(string fullPath, string query)
    {
        return ScoreDirectoryNameMatch(fullPath, query) > 0;
    }

    private static int ScoreDirectoryNameMatch(string fullPath, string query)
    {
        var directoryPath = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return 0;
        }

        var bestScore = 0;
        foreach (var directoryName in directoryPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            bestScore = Math.Max(bestScore, FuzzySearch(query, directoryName).Score / 2);
        }

        return bestScore;
    }

    private static MatchResult FuzzySearch(string query, string text)
    {
        var match = FuzzyMatch(query, text);
        if (match.Score > 0 || string.IsNullOrWhiteSpace(text))
        {
            return match;
        }

        var compactText = CompactSearchText(text);
        if (compactText.Text.Length == text.Length)
        {
            return match;
        }

        var compactMatch = FuzzyMatch(query, compactText.Text);
        if (compactMatch.Score <= 0 || compactMatch.MatchData.Count == 0)
        {
            return match;
        }

        return new MatchResult(
            compactMatch.Success,
            compactMatch.SearchPrecision,
            compactMatch.MatchData.Select(index => compactText.IndexMap[index]).ToList(),
            compactMatch.RawScore);
    }

    private static MatchResult FuzzyMatch(string query, string text)
    {
        if (StringMatcher.Instance is not null)
        {
            return StringMatcher.Instance.FuzzyMatch(query, text);
        }

        return new StringMatcher { UserSettingSearchPrecision = StringMatcher.SearchPrecisionScore.Regular }
            .FuzzyMatch(query, text);
    }

    private static CompactSearchTextResult CompactSearchText(string text)
    {
        var builder = new StringBuilder(text.Length);
        var indexMap = new List<int>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLetterOrDigit(text[i]))
            {
                continue;
            }

            builder.Append(text[i]);
            indexMap.Add(i);
        }

        return new CompactSearchTextResult(builder.ToString(), indexMap);
    }

    private sealed record SearchScore(int Score, IList<int>? TitleHighlightData);

    private sealed record CompactSearchTextResult(string Text, List<int> IndexMap);

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
