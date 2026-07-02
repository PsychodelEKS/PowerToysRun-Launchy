namespace Community.PowerToys.Run.Plugin.Launchy.Models;

public sealed class SearchMatch
{
    public required IndexedEntry Entry { get; init; }

    public required int Score { get; init; }
}

