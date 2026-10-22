namespace ClaudeProviderLauncher.Services;

/// <summary>
/// The built-in, protected profile of one tool. System profiles are not stored in
/// presets.json and are never offered for deletion or overwrite.
/// </summary>
public sealed class SystemProfile
{
    public required ToolKind Tool { get; init; }

    public required string DisplayName { get; init; }

    public required ProviderPreset Preset { get; init; }

    public string? Error { get; init; }

    public bool UsesCurrentCodexConfig { get; init; }
}

public static class SystemProfiles
{
    public const string ClaudeDisplayName = "Default: Claude Code";
    public const string CodexDisplayName = "Default: Codex (config.toml)";

    /// <summary>
    /// Reads the live configuration from each tool's native config file:
    /// - Claude Code: ~/.claude/settings.json (env: ANTHROPIC_BASE_URL, ANTHROPIC_AUTH_TOKEN/ANTHROPIC_API_KEY, model)
    /// - Codex: ~/.codex/config.toml (model, model_provider, base_url, env_key)
    /// If a config is missing or unreadable, returns an empty snapshot with error info.
    /// </summary>
    public static SystemProfile Get(
        ToolKind tool,
        string? codexConfigPath = null,
        string? claudeConfigPath = null)
    {
        return tool switch
        {
            ToolKind.ClaudeCode => CreateClaude(claudeConfigPath ?? ClaudeConfigReader.DefaultPath),
            ToolKind.Codex => CreateCodex(codexConfigPath ?? CodexConfigReader.DefaultPath),
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
        };
    }

    public static string DisplayName(ToolKind tool) => tool switch
    {
        ToolKind.ClaudeCode => ClaudeDisplayName,
        ToolKind.Codex => CodexDisplayName,
        _ => tool.ToString(),
    };

    /// <summary>
    /// A user preset may not use a system profile's display name or the reserved
    /// system identifier prefix. The actual protection is the identifier, this
    /// check only prevents confusing duplicate labels.
    /// </summary>
    public static bool IsReservedName(ToolKind tool, string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith(ProfileChoice.SystemIdPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, DisplayName(tool), StringComparison.OrdinalIgnoreCase);
    }

    private static SystemProfile CreateClaude(string configPath)
    {
        var snapshot = ClaudeConfigReader.Read(configPath);

        // Prefer AUTH_TOKEN over API_KEY if both present; neither is written back to disk by this app.
        var apiKey = !string.IsNullOrWhiteSpace(snapshot.AuthToken)
            ? snapshot.AuthToken
            : snapshot.ApiKey;

        return new SystemProfile
        {
            Tool = ToolKind.ClaudeCode,
            DisplayName = ClaudeDisplayName,
            Error = snapshot.Error,
            Preset = new ProviderPreset
            {
                Name = ClaudeDisplayName,
                Tool = ToolKind.ClaudeCode,
                BaseUrl = snapshot.BaseUrl ?? string.Empty,
                Model = snapshot.Model ?? string.Empty,
                ApiKey = apiKey ?? string.Empty,
            },
        };
    }

    private static SystemProfile CreateCodex(string configPath)
    {
        // The original config is backed up once, before any launch. Restore will
        // copy it back verbatim without parsing individual parameters.
        CodexConfigBackup.EnsurePermanentBackup();

        var snapshot = CodexConfigReader.Read(configPath);

        return new SystemProfile
        {
            Tool = ToolKind.Codex,
            DisplayName = CodexDisplayName,
            UsesCurrentCodexConfig = true,
            Error = snapshot.Error,
            Preset = new ProviderPreset
            {
                Name = CodexDisplayName,
                Tool = ToolKind.Codex,
                BaseUrl = snapshot.BaseUrl ?? string.Empty,
                Model = snapshot.Model ?? string.Empty,
                ApiKey = snapshot.ApiKeyEnv ?? string.Empty,
            },
        };
    }
}
