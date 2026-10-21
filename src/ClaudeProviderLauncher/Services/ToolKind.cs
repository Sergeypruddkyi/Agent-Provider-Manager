namespace ClaudeProviderLauncher.Services;

/// <summary>
/// The external CLI tool a profile belongs to. Profiles, presets and launches are
/// always scoped to exactly one tool so Claude Code and Codex can never share values.
/// </summary>
public enum ToolKind
{
    ClaudeCode = 0,
    Codex = 1,
}

public static class ToolKindExtensions
{
    public static string DisplayName(this ToolKind tool) => tool switch
    {
        ToolKind.ClaudeCode => "Claude Code",
        ToolKind.Codex => "Codex",
        _ => tool.ToString(),
    };

    public static string StartButtonText(this ToolKind tool) => tool switch
    {
        ToolKind.ClaudeCode => "START CLAUDE CODE",
        ToolKind.Codex => "START CODEX",
        _ => "START",
    };
}
