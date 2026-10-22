using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Well-known locations used by the launcher. All application-owned data is stored
/// inside the application's own directory so the app is fully portable (e.g. on a USB stick).
/// </summary>
public static class AppPaths
{
    private static string? _dataDirectory;
    private static readonly object _lock = new();

    /// <summary>
    /// Directory that holds this launcher's own small state (presets, last values, backups, claude-home).
    /// Resolves to the directory containing the running executable (self-contained single-file friendly).
    /// </summary>
    public static string DataDirectory
    {
        get
        {
            if (_dataDirectory is not null)
            {
                return _dataDirectory;
            }

            lock (_lock)
            {
                if (_dataDirectory is not null)
                {
                    return _dataDirectory;
                }

                // For self-contained single-file apps, Assembly.Location returns the temp extraction path.
                // Use the entry assembly's location as the primary anchor, then fall back to the current process.
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var location = asm.Location;

                if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
                {
                    // Fallback: current process main module
                    try
                    {
                        using var proc = System.Diagnostics.Process.GetCurrentProcess();
                        location = proc.MainModule?.FileName ?? string.Empty;
                    }
                    catch
                    {
                        // Ignore, will use fallback below
                    }
                }

                if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
                {
                    _dataDirectory = Path.GetDirectoryName(location)!;
                }
                else
                {
                    // Last resort: current directory (only when running from source/debug without publish)
                    _dataDirectory = AppContext.BaseDirectory;
                }

                return _dataDirectory;
            }
        }
    }

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
