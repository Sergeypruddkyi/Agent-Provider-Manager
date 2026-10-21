namespace ClaudeProviderLauncher.Services;

/// <summary>
/// One entry in the profile picker. System profiles are identified by a stable
/// identifier, never by their display name, so a user preset with a similar name
/// can never be mistaken for (or overwrite) a system profile.
/// </summary>
public sealed class ProfileChoice
{
    public const string SystemIdPrefix = "system:";
    public const string UserIdPrefix = "user:";

    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public ProviderPreset? Preset { get; init; }

    public bool IsSystem => Id.StartsWith(SystemIdPrefix, StringComparison.Ordinal);

    public static ProfileChoice FromSystem(SystemProfile profile) => new()
    {
        Id = SystemProfileIds.For(profile.Tool),
        DisplayName = profile.DisplayName,
        Preset = profile.Preset,
    };

    public static ProfileChoice FromPreset(ProviderPreset preset) => new()
    {
        Id = UserIdPrefix + preset.Name,
        DisplayName = preset.Name,
        Preset = preset,
    };

    public override string ToString() => DisplayName;
}

public static class SystemProfileIds
{
    public const string ClaudeDefault = "system:claude-code:default";
    public const string CodexDefault = "system:codex:default";

    public static string For(ToolKind tool) => tool switch
    {
        ToolKind.ClaudeCode => ClaudeDefault,
        ToolKind.Codex => CodexDefault,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
    };
}
