namespace Community.PowerToys.Run.Plugin.Launchy.Models;

public sealed class LaunchySettings
{
    public bool EnableGlobalResults { get; set; }

    public List<LaunchyFolderRule> FolderRules { get; set; } = [];

    public static LaunchySettings CreateDefault()
    {
        return new LaunchySettings
        {
            EnableGlobalResults = false,
            FolderRules = [],
        };
    }

    public LaunchySettings Clone()
    {
        return new LaunchySettings
        {
            EnableGlobalResults = EnableGlobalResults,
            FolderRules = FolderRules.Select(rule => rule.Clone()).ToList(),
        };
    }
}

