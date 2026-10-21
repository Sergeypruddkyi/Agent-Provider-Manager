using System.Diagnostics;
using System.IO;

namespace ClaudeProviderLauncher.Services;

public sealed class CodexLaunchOptions
{
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>Null/empty means "use the current Codex config.toml and its auth".</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Only used for a custom provider. It is passed via the process environment.</summary>
    public string? ApiKey { get; init; }

    public string? Model { get; init; }
}

/// <summary>
/// Starts Codex with temporary, per-process provider overrides. The user's
/// <c>~/.codex/config.toml</c> is read by Codex itself but is never modified by
/// this application, so sandbox, Windows, trust and every other setting survive.
/// </summary>
public static class CodexLauncher
{
    public const string ProviderId = "agent_provider_manager";

    /// <summary>
    /// Name of the environment variable referenced by <c>env_key</c>. The real key
    /// is only placed in this process's environment, never in config.toml or argv.
    /// </summary>
    public const string ApiKeyEnvironmentVariable = "AGENT_PROVIDER_MANAGER_API_KEY";

    public static bool IsCustomProvider(CodexLaunchOptions options) =>
        !string.IsNullOrWhiteSpace(options.BaseUrl);

    public static bool IsValidBaseUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static IReadOnlyList<string> BuildArguments(CodexLaunchOptions options)
    {
        var arguments = new List<string>();

        if (IsCustomProvider(options))
        {
            // Codex supports -c key=value overrides that exist only for this run.
            // env_key is the variable NAME; the value is supplied by Launch below.
            arguments.Add("-c");
            arguments.Add($"model_provider={TomlString(ProviderId)}");
            arguments.Add("-c");
            arguments.Add($"model_providers.{ProviderId}.name={TomlString("Agent Provider Manager")}");
            arguments.Add("-c");
            arguments.Add($"model_providers.{ProviderId}.base_url={TomlString(options.BaseUrl!.Trim())}");
            arguments.Add("-c");
            arguments.Add($"model_providers.{ProviderId}.env_key={TomlString(ApiKeyEnvironmentVariable)}");

            if (!string.IsNullOrWhiteSpace(options.Model))
            {
                arguments.Add("-c");
                arguments.Add($"model={TomlString(options.Model.Trim())}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.Model))
        {
            // The current config.toml (including its auth and provider) stays active.
            arguments.Add("--model");
            arguments.Add(options.Model.Trim());
        }

        return arguments;
    }

    public static Process Launch(string codexExecutable, CodexLaunchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.WorkingDirectory))
        {
            throw new ArgumentException("Working directory is required.", nameof(options));
        }

        if (!Directory.Exists(options.WorkingDirectory))
        {
            throw new DirectoryNotFoundException("Working directory not found: " + options.WorkingDirectory);
        }

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        foreach (var argument in BuildArguments(options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (IsCustomProvider(options))
        {
            startInfo.Environment[ApiKeyEnvironmentVariable] = options.ApiKey ?? string.Empty;
        }

        var extension = Path.GetExtension(codexExecutable).ToLowerInvariant();
        if (extension is ".cmd" or ".bat")
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.ArgumentList.Insert(0, codexExecutable);
            startInfo.ArgumentList.Insert(0, "/c");
        }
        else
        {
            startInfo.FileName = codexExecutable;
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Codex process was not created.");
    }

    private static string TomlString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
