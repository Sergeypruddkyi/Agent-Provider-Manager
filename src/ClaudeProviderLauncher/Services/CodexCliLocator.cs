using System.IO;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Locates the installed Codex CLI executable.
/// </summary>
public static class CodexCliLocator
{
    private static readonly string[] ExecutableNames = { "codex.exe", "codex.cmd", "codex.bat" };

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
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidates = new[]
        {
            Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe"),
            Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "codex.exe"),
            Path.Combine(localAppData, "Programs", "codex", "codex.exe"),
            Path.Combine(roamingAppData, "npm", "codex.cmd"),
            Path.Combine(home, ".local", "bin", "codex.exe"),
            Path.Combine(home, ".local", "bin", "codex.cmd"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
