using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// A read-only view of the active Claude Code user configuration.
/// The manager reads the live settings.json to show current values in the UI.
/// </summary>
public sealed class ClaudeConfigSnapshot
{
    public string? BaseUrl { get; init; }
    public string? AuthToken { get; init; }
    public string? ApiKey { get; init; }
    public string? Model { get; init; }
    public string? Error { get; init; }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);
}

/// <summary>
/// Reads the active provider configuration from Claude Code's settings.json.
/// This is read-only - the file is never modified by this application.
/// </summary>
public static class ClaudeConfigReader
{
    private const string BaseUrlKey = "ANTHROPIC_BASE_URL";
    private const string AuthTokenKey = "ANTHROPIC_AUTH_TOKEN";
    private const string ApiKeyKey = "ANTHROPIC_API_KEY";
    private const string ModelKey = "ANTHROPIC_MODEL";

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "settings.json");

    /// <summary>
    /// Reads the active provider profile from settings.json. Returns empty snapshot if file doesn't exist.
    /// </summary>
    public static ClaudeConfigSnapshot Read(string? path = null)
    {
        var settingsPath = path ?? DefaultPath;

        try
        {
            if (!File.Exists(settingsPath))
            {
                return new ClaudeConfigSnapshot();
            }

            var json = File.ReadAllText(settingsPath);
            var root = JsonNode.Parse(json) as JsonObject;

            if (root is null)
            {
                return new ClaudeConfigSnapshot
                {
                    Error = "settings.json is not a valid JSON object."
                };
            }

            var env = root["env"] as JsonObject;
            var baseUrl = env?[BaseUrlKey]?.GetValue<string>();
            var authToken = env?[AuthTokenKey]?.GetValue<string>();
            var apiKey = env?[ApiKeyKey]?.GetValue<string>();
            // model is at root level, not in env
            var model = root["model"]?.GetValue<string>();

            return new ClaudeConfigSnapshot
            {
                BaseUrl = baseUrl?.Trim(),
                AuthToken = authToken?.Trim(),
                ApiKey = apiKey?.Trim(),
                Model = model?.Trim(),
            };
        }
        catch (Exception ex)
        {
            return new ClaudeConfigSnapshot
            {
                Error = "Could not read Claude Code settings.json: " + ex.Message,
            };
        }
    }
}