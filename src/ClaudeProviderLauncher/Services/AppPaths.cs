using System.IO;
using System.Text.Json;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Well-known per-user locations used by the launcher.
/// </summary>
public static class AppPaths
{
    /// <summary>Directory that holds this launcher's own small state (e.g. last entered values).</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeProviderLauncher");

    /// <summary>
    /// A dedicated Claude Code config directory used only for launches started by this app.
    /// This keeps every launch isolated from the user's real <c>~/.claude</c> (whose
    /// <c>settings.json</c> may pin a different provider and would otherwise win over our values).
    /// </summary>
    public static string ClaudeConfigDirectory { get; } = Path.Combine(DataDirectory, "claude-home");
}

/// <summary>
/// The values that are remembered between runs. The API key / auth token is deliberately
/// NOT part of this type, so it can never be persisted to disk.
/// </summary>
public sealed class LastValues
{
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? WorkingDirectory { get; set; }
}

/// <summary>
/// Best-effort persistence of the last entered (non-secret) values.
/// </summary>
public static class LastValuesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "last-values.json");

    public static LastValues Load()
    {
        TryLoad(out var values, out _);
        return values;
    }

    /// <summary>
    /// Reads the remembered non-secret values. Returns false with a readable error
    /// when the file exists but cannot be parsed, so the UI can report a damaged
    /// default profile instead of silently presenting invented values.
    /// </summary>
    public static bool TryLoad(out LastValues values, out string? error)
    {
        values = new LastValues();
        error = null;

        try
        {
            if (!File.Exists(FilePath))
            {
                return true;
            }

            var json = File.ReadAllText(FilePath);
            var parsed = JsonSerializer.Deserialize<LastValues>(json);
            if (parsed is null)
            {
                error = "last-values.json does not contain a profile.";
                return false;
            }

            values = parsed;
            return true;
        }
        catch (Exception ex)
        {
            error = "Could not read last-values.json: " + ex.Message;
            return false;
        }
    }

    public static void Save(LastValues values)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(values, SerializerOptions));
        }
        catch
        {
            // Remembering the last values is a convenience only; ignore failures.
        }
    }

    /// <summary>
    /// Remembers only Claude Code's non-secret values. Codex launches must never
    /// write their provider values here, otherwise Claude's default profile would
    /// silently pick them up on the next start.
    /// </summary>
    public static void SaveClaudeValues(string baseUrl, string model, string workingDirectory)
    {
        var values = Load();
        values.BaseUrl = baseUrl;
        values.Model = model;
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            values.WorkingDirectory = workingDirectory;
        }

        Save(values);
    }

    /// <summary>
    /// The working directory is chosen per project and shared by both tools; it is
    /// independent of the selected provider and is never part of a preset.
    /// </summary>
    public static void SaveWorkingDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return;
        }

        var values = Load();
        values.WorkingDirectory = workingDirectory;
        Save(values);
    }
}
