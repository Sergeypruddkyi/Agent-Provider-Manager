using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Switches the provider in Claude Code's active user settings file
/// (<c>%USERPROFILE%\.claude\settings.json</c>). The first switch saves the original
/// file once to <c>default-settings.json</c>; that backup is permanent and is never
/// overwritten. Only the provider values are changed - every other setting (MCP
/// servers, theme, permissions, ...) is preserved. No API requests are made and
/// model identifiers are never validated.
/// </summary>
public sealed class ProviderConfigurationManager
{
    private const string BaseUrl = "ANTHROPIC_BASE_URL";
    private const string AuthToken = "ANTHROPIC_AUTH_TOKEN";
    private const string ApiKey = "ANTHROPIC_API_KEY";
    private const string Model = "ANTHROPIC_MODEL";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private readonly string _backupPath;

    public ProviderConfigurationManager()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "provider-manager", "default-settings.json"))
    {
    }

    public ProviderConfigurationManager(string settingsPath, string backupPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
        _backupPath = Path.GetFullPath(backupPath);
    }

    public string SettingsPath => _settingsPath;

    public string BackupPath => _backupPath;

    public static bool IsValidProfile(string? baseUrl, string? token, string? model) =>
        Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(model);

    /// <summary>
    /// The profile a Start uses: an explicitly selected, complete preset wins; otherwise
    /// the values currently shown in the fields; otherwise null (Start must not run).
    /// This is why a restore clears the preset selection: the fields then hold the
    /// restored original profile, and a stale temporary preset cannot silently
    /// override it.
    /// </summary>
    public static ProviderPreset? ResolveStartProfile(ProviderPreset? selectedPreset, string? baseUrl, string? token, string? model) =>
        selectedPreset is not null && IsValidProfile(selectedPreset.BaseUrl, selectedPreset.ApiKey, selectedPreset.Model)
            ? selectedPreset
            : IsValidProfile(baseUrl, token, model)
                ? new ProviderPreset { BaseUrl = baseUrl!.Trim(), ApiKey = token!, Model = model!.Trim() }
                : null;

    /// <summary>
    /// Writes the profile's Base URL, token and model into the active settings file,
    /// replacing any previously applied provider values. All other settings are kept.
    /// </summary>
    public void Apply(ProviderPreset profile)
    {
        if (!IsValidProfile(profile.BaseUrl, profile.ApiKey, profile.Model))
            throw new InvalidOperationException("The provider profile is incomplete or invalid.");

        SaveOriginalOnce();

        var settings = ReadObject(_settingsPath);
        var env = EnsureObject(settings, "env");
        env.Remove(ApiKey);
        env.Remove(Model);
        env[BaseUrl] = ClaudeLauncher.NormalizeBaseUrlForSettings(profile.BaseUrl);
        env[AuthToken] = profile.ApiKey;
        settings["model"] = profile.Model.Trim();
        WriteTextAtomically(_settingsPath, settings.ToJsonString(JsonOptions));
    }

    /// <summary>
    /// Copies the original settings saved in <c>default-settings.json</c> back over the
    /// active settings file. The backup file itself is kept for later restores.
    /// </summary>
    public void Restore()
    {
        if (!File.Exists(_backupPath))
            throw new FileNotFoundException("The original settings backup was not found.", _backupPath);

        WriteTextAtomically(_settingsPath, File.ReadAllText(_backupPath));
    }

    /// <summary>
    /// Reads the provider profile (Base URL, token, model) from the permanent backup,
    /// so the UI can be synchronized with the values a restore brought back. Read-only:
    /// the backup is never modified. Returns false when it is missing or does not hold
    /// a complete profile; never throws.
    /// </summary>
    public bool TryGetOriginalProfile(out ProviderPreset profile, out string error)
    {
        try
        {
            profile = new ProviderPreset();
            if (!File.Exists(_backupPath))
            {
                error = "The original settings backup was not found.";
                return false;
            }

            var root = JsonNode.Parse(File.ReadAllText(_backupPath)) as JsonObject;
            var env = root?["env"] as JsonObject;
            var baseUrl = env?[BaseUrl]?.GetValue<string>();
            var token = env?[AuthToken]?.GetValue<string>() ?? env?[ApiKey]?.GetValue<string>();
            var model = root?["model"]?.GetValue<string>();

            if (!IsValidProfile(baseUrl, token, model))
            {
                error = "The original settings do not contain a complete provider profile.";
                return false;
            }

            profile = new ProviderPreset { BaseUrl = baseUrl!.Trim(), ApiKey = token!, Model = model!.Trim() };
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            profile = new ProviderPreset();
            error = "Unable to read the original settings: " + ex.Message;
            return false;
        }
    }

    /// <summary>Saves the current active settings verbatim, once. An existing backup is never touched.</summary>
    private void SaveOriginalOnce()
    {
        if (File.Exists(_backupPath))
        {
            return;
        }

        var original = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : "{}";
        if (JsonNode.Parse(original) is not JsonObject)
            throw new InvalidDataException("The active settings file is not a valid JSON object: " + _settingsPath);

        WriteTextAtomically(_backupPath, original);
    }

    private static JsonObject ReadObject(string path) =>
        !File.Exists(path)
            ? new JsonObject()
            : JsonNode.Parse(File.ReadAllText(path)) as JsonObject
              ?? throw new InvalidDataException("Configuration must be a JSON object: " + path);

    private static JsonObject EnsureObject(JsonObject root, string name)
    {
        if (root[name] is JsonObject obj)
        {
            return obj;
        }

        obj = new JsonObject();
        root[name] = obj;
        return obj;
    }

    private static void WriteTextAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, content);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
