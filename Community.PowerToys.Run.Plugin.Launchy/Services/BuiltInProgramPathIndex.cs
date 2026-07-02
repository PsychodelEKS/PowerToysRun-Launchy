using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Wox.Infrastructure;

namespace Community.PowerToys.Run.Plugin.Launchy.Services;

public sealed class BuiltInProgramPathIndex
{
    private const string AppPathsRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private readonly HashSet<string> _paths;

    private BuiltInProgramPathIndex(HashSet<string> paths)
    {
        _paths = paths;
    }

    public static BuiltInProgramPathIndex CreateDefault()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddRegistryAppPaths(paths);
        AddShortcutSource(paths, Environment.SpecialFolder.StartMenu);
        AddShortcutSource(paths, Environment.SpecialFolder.CommonStartMenu);
        AddShortcutSource(paths, Environment.SpecialFolder.Desktop);
        AddShortcutSource(paths, Environment.SpecialFolder.CommonDesktopDirectory);
        AddPathEnvironmentPrograms(paths);

        return new BuiltInProgramPathIndex(paths);
    }

    public bool Contains(string path)
    {
        var normalizedPath = NormalizePath(path);
        return !string.IsNullOrWhiteSpace(normalizedPath) && _paths.Contains(normalizedPath);
    }

    private static void AddRegistryAppPaths(HashSet<string> paths)
    {
        AddRegistryAppPaths(paths, RegistryHive.LocalMachine, RegistryView.Registry64);
        AddRegistryAppPaths(paths, RegistryHive.LocalMachine, RegistryView.Registry32);
        AddRegistryAppPaths(paths, RegistryHive.CurrentUser, RegistryView.Registry64);
        AddRegistryAppPaths(paths, RegistryHive.CurrentUser, RegistryView.Registry32);
    }

    private static void AddRegistryAppPaths(HashSet<string> paths, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appPathsKey = baseKey.OpenSubKey(AppPathsRegistryKey);
            if (appPathsKey is null)
            {
                return;
            }

            foreach (var subKeyName in appPathsKey.GetSubKeyNames())
            {
                using var subKey = appPathsKey.OpenSubKey(subKeyName);
                AddPath(paths, subKey?.GetValue(string.Empty)?.ToString());
            }
        }
        catch
        {
        }
    }

    private static void AddShortcutSource(HashSet<string> paths, Environment.SpecialFolder specialFolder)
    {
        var directory = Environment.GetFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        IEnumerable<string> shortcutPaths;
        try
        {
            shortcutPaths = Directory.EnumerateFiles(
                directory,
                "*.lnk",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                }).ToList();
        }
        catch
        {
            return;
        }

        foreach (var shortcutPath in shortcutPaths)
        {
            AddPath(paths, shortcutPath);
            AddPath(paths, TryResolveShortcutTarget(shortcutPath));
        }
    }

    private static void AddPathEnvironmentPrograms(HashSet<string> paths)
    {
        var pathEnvironment = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var expandedDirectory = Environment.ExpandEnvironmentVariables(directory.Trim());
            if (!Directory.Exists(expandedDirectory))
            {
                continue;
            }

            try
            {
                foreach (var executablePath in Directory.EnumerateFiles(expandedDirectory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    AddPath(paths, executablePath);
                }
            }
            catch
            {
            }
        }
    }

    private static string TryResolveShortcutTarget(string shortcutPath)
    {
        try
        {
            return new ShellLinkHelper().RetrieveTargetPath(shortcutPath);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void AddPath(HashSet<string> paths, string? path)
    {
        var normalizedPath = NormalizePath(path);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            paths.Add(normalizedPath);
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return GetLongPath(Path.GetFullPath(path));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetLongPath(string path)
    {
        var buffer = new StringBuilder(path.Length + 1);
        var length = GetLongPathName(path, buffer, buffer.Capacity);
        if (length > 0 && length < buffer.Capacity)
        {
            return buffer.ToString();
        }

        if (length > buffer.Capacity)
        {
            buffer.EnsureCapacity(length);
            length = GetLongPathName(path, buffer, buffer.Capacity);
            if (length > 0 && length < buffer.Capacity)
            {
                return buffer.ToString();
            }
        }

        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int bufferLength);
}
