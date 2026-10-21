using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Prepares this app's private Claude Code config directory.
/// Only the launcher's own config directory is ever touched - never the user's ~/.claude.
/// </summary>
public static class ClaudeConfigPreparer
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Marks onboarding as completed and pre-accepts the folder-trust prompt for
    /// <paramref name="workingDirectory"/> so a launch goes straight to the prompt.
    /// Existing values are preserved. Only this app's own config directory is written.
    /// </summary>
    public static void Prepare(string workingDirectory)
    {
        Directory.CreateDirectory(AppPaths.ClaudeConfigDirectory);

        var configFile = Path.Combine(AppPaths.ClaudeConfigDirectory, ".claude.json");

        JsonObject root;
        try
        {
            root = File.Exists(configFile)
                ? JsonNode.Parse(File.ReadAllText(configFile)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch
        {
            root = new JsonObject();
        }

        root["hasCompletedOnboarding"] = true;

        var projectKey = NormalizePath(workingDirectory);

        if (root["projects"] is not JsonObject projects)
        {
            projects = new JsonObject();
            root["projects"] = projects;
        }

        if (projects[projectKey] is not JsonObject projectEntry)
        {
            projectEntry = new JsonObject();
            projects[projectKey] = projectEntry;
        }

        projectEntry["hasTrustDialogAccepted"] = true;

        try
        {
            File.WriteAllText(configFile, root.ToJsonString(SerializerOptions));
        }
        catch
        {
            // If this fails the worst case is the one-time onboarding / trust prompt; not fatal.
        }
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        return normalized.Length == 0 ? path : normalized;
    }
}
