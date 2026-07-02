namespace Community.PowerToys.Run.Plugin.Launchy.Models;

public sealed class LaunchyFolderRule
{
    public bool Enabled { get; set; } = true;

    public string Path { get; set; } = string.Empty;

    public string Extensions { get; set; } = ".exe;.lnk";

    public int MaxDepth { get; set; } = 10;

    public bool IncludeDirectories { get; set; }

    public bool MatchDirectoryNames { get; set; }

    public LaunchyFolderRule Clone()
    {
        return new LaunchyFolderRule
        {
            Enabled = Enabled,
            Path = Path,
            Extensions = Extensions,
            MaxDepth = MaxDepth,
            IncludeDirectories = IncludeDirectories,
            MatchDirectoryNames = MatchDirectoryNames,
        };
    }
}
