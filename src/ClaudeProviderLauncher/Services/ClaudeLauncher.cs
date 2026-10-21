using System.Diagnostics;
using System.IO;

namespace ClaudeProviderLauncher.Services;

/// <summary>Starts Claude Code with the globally active provider configuration.</summary>
public static class ClaudeLauncher
{
    public static Process Launch(string claudeExecutable, string workingDirectory, string baseUrl, string authToken, string? model)
    {
        ClaudeConfigPreparer.Prepare(workingDirectory);
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        // Global ~/.claude/settings.json drives ordinary CLI and the extension. These process
        // variables only mirror the current provider for this invocation and are not persisted.
        startInfo.Environment["ANTHROPIC_BASE_URL"] = NormalizeBaseUrl(baseUrl);
        startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] = authToken;
        if (!string.IsNullOrWhiteSpace(model)) startInfo.Environment["ANTHROPIC_MODEL"] = model;

        var extension = Path.GetExtension(claudeExecutable).ToLowerInvariant();
        if (extension is ".cmd" or ".bat")
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(claudeExecutable);
        }
        else startInfo.FileName = claudeExecutable;

        if (!string.IsNullOrWhiteSpace(model))
        {
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(model);
        }
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Claude Code process was not created.");
    }

    public static string NormalizeBaseUrlForSettings(string baseUrl) => NormalizeBaseUrl(baseUrl);

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^3].TrimEnd('/');
        return trimmed.Length == 0 ? baseUrl.Trim() : trimmed;
    }
}
