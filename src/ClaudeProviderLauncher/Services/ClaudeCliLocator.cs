using System.IO;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Locates the installed Claude Code CLI executable.
/// </summary>
public static class ClaudeCliLocator
{
    private static readonly string[] ExecutableNames = { "claude.exe", "claude.cmd", "claude.bat" };

    public static string? Find()
    {
        return FindOnPath() ?? FindInWellKnownLocations();
    }

    private static string? FindOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var rawDirectory in path.Split(Path.PathSeparator))
        {
            var directory = rawDirectory.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            foreach (var name in ExecutableNames)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? FindInWellKnownLocations()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidates = new[]
        {
            Path.Combine(home, ".local", "bin", "claude.exe"),
            Path.Combine(home, ".local", "bin", "claude.cmd"),
            Path.Combine(home, "AppData", "Roaming", "npm", "claude.cmd"),
            Path.Combine(home, "AppData", "Local", "Programs", "claude", "claude.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
