namespace Community.PowerToys.Run.Plugin.Launchy.Models;

public sealed class IndexedEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required bool IsDirectory { get; init; }

    public required DateTimeOffset IndexedAt { get; init; }
}

