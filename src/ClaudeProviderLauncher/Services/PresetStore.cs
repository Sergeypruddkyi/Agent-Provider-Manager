using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// A named provider preset: the three values needed to talk to a provider
/// (Base URL, API key / auth token, model). The working directory is
/// deliberately NOT part of a preset - it is chosen per launch.
/// </summary>
public sealed class ProviderPreset
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    /// <summary>Plaintext only in memory; encrypted with DPAPI before it is written to disk.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Tool this preset belongs to. Missing values in older files deserialize to
    /// <see cref="ToolKind.ClaudeCode"/>, which keeps existing presets working.
    /// </summary>
    public ToolKind Tool { get; set; } = ToolKind.ClaudeCode;

    /// <summary>Whether this preset is a built-in default supplied with the application.</summary>
    public bool IsDefault { get; set; } = false;
}

/// <summary>
/// Best-effort persistence of named provider presets.
/// Built-in defaults are embedded as a resource and loaded read-only.
/// User presets are stored in <c>%LOCALAPPDATA%\ClaudeProviderLauncher\presets.json</c>
/// with API keys encrypted via DPAPI (CurrentUser scope).
/// </summary>
public sealed class PresetStore
{
    // Optional entropy makes the file slightly less generic; the real protection is DPAPI.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClaudeProviderLauncher.Presets.v1");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _userFilePath;
    private readonly bool _loadDefaults;
    private IReadOnlyList<ProviderPreset>? _cachedDefaults;

    public PresetStore()
        : this(Path.Combine(AppPaths.DataDirectory, "presets.json"), loadDefaults: true)
    {
    }

    /// <summary>Path-injectable constructor, primarily for tests.</summary>
    /// <param name="userFilePath">Path to the user presets file.</param>
    /// <param name="loadDefaults">Whether to load built-in defaults (false for tests using custom paths).</param>
    public PresetStore(string userFilePath, bool loadDefaults = false)
    {
        _userFilePath = userFilePath;
        _loadDefaults = loadDefaults;
    }

    public string UserFilePath => _userFilePath;

    /// <summary>Loads all presets: built-in defaults (read-only) followed by user presets. Never throws.</summary>
    public IReadOnlyList<ProviderPreset> Load()
    {
        var result = new List<ProviderPreset>();

        // Built-in defaults first (only when enabled)
        if (_loadDefaults)
        {
            result.AddRange(LoadDefaults());
        }

        // Then user presets
        try
        {
            if (File.Exists(_userFilePath))
            {
                var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(_userFilePath), SerializerOptions);
                if (file?.Presets is not null)
                {
                    foreach (var stored in file.Presets)
                    {
                        if (string.IsNullOrWhiteSpace(stored.Name))
                        {
                            continue;
                        }

                        result.Add(new ProviderPreset
                        {
                            Name = stored.Name.Trim(),
                            BaseUrl = stored.BaseUrl ?? string.Empty,
                            Model = stored.Model ?? string.Empty,
                            ApiKey = TryUnprotect(stored.ApiKeyProtected),
                            Tool = stored.Tool,
                            IsDefault = false,
                        });
                    }
                }
            }
        }
        catch
        {
            // Ignore corrupt user file; defaults remain available.
        }

        return result;
    }

    /// <summary>Loads only the presets belonging to <paramref name="tool"/> (defaults + user).</summary>
    public IReadOnlyList<ProviderPreset> Load(ToolKind tool) =>
        Load().Where(p => p.Tool == tool).ToList();

    /// <summary>Loads only the built-in default presets for a tool.</summary>
    public IReadOnlyList<ProviderPreset> LoadDefaults(ToolKind tool) =>
        LoadDefaults().Where(p => p.Tool == tool).ToList();

    /// <summary>Loads only the user presets for a tool.</summary>
    public IReadOnlyList<ProviderPreset> LoadUser(ToolKind tool) =>
        Load().Where(p => p.Tool == tool && !p.IsDefault).ToList();

    /// <summary>Loads built-in default presets from the embedded resource.</summary>
    private IReadOnlyList<ProviderPreset> LoadDefaults()
    {
        if (_cachedDefaults is not null)
        {
            return _cachedDefaults;
        }

        var defaults = new List<ProviderPreset>();

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "ClaudeProviderLauncher.DefaultPresets.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var file = JsonSerializer.Deserialize<StoredFile>(json, SerializerOptions);
                if (file?.Presets is not null)
                {
                    foreach (var stored in file.Presets)
                    {
                        if (string.IsNullOrWhiteSpace(stored.Name))
                        {
                            continue;
                        }

                        defaults.Add(new ProviderPreset
                        {
                            Name = stored.Name.Trim(),
                            BaseUrl = stored.BaseUrl ?? string.Empty,
                            Model = stored.Model ?? string.Empty,
                            ApiKey = stored.ApiKey ?? string.Empty, // Defaults have no encryption
                            Tool = stored.Tool,
                            IsDefault = true,
                        });
                    }
                }
            }
        }
        catch
        {
            // Resource missing or invalid — continue with empty defaults.
        }

        _cachedDefaults = defaults;
        return _cachedDefaults;
    }

    /// <summary>Persists the full user preset list. Returns false when the file could not be written.</summary>
    public bool Save(IReadOnlyList<ProviderPreset> presets)
    {
        try
        {
            // Only save user presets (non-defaults)
            var userPresets = presets.Where(p => !p.IsDefault).ToList();

            var file = new StoredFile
            {
                Presets = userPresets
                    .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new StoredPreset
                    {
                        Name = p.Name.Trim(),
                        BaseUrl = p.BaseUrl,
                        Model = p.Model,
                        ApiKeyProtected = TryProtect(p.ApiKey),
                        Tool = p.Tool,
                    })
                    .ToList(),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_userFilePath)!);
            File.WriteAllText(_userFilePath, JsonSerializer.Serialize(file, SerializerOptions));
            return true;
        }
        catch
        {
            // Disk/DPAPI failures must not crash the app; the caller decides how to report.
            return false;
        }
    }

    /// <summary>Returns the preset with the given name inside one tool (defaults + user), or null.</summary>
    public ProviderPreset? Find(ToolKind tool, string name)
    {
        return Load(tool).FirstOrDefault(p => NameEquals(p.Name, name));
    }

    /// <summary>Returns the user preset with the given name inside one tool, or null.</summary>
    public ProviderPreset? FindUser(ToolKind tool, string name)
    {
        return LoadUser(tool).FirstOrDefault(p => NameEquals(p.Name, name));
    }

    /// <summary>
    /// Inserts a new user preset or overwrites the existing user preset with the same name
    /// inside the same tool (case-insensitive). A preset in the other tool with
    /// the same name is never touched. Default presets cannot be modified.
    /// Returns false when the file could not be written or the name belongs to a default.
    /// </summary>
    public bool Upsert(ProviderPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            return false;
        }

        // Cannot overwrite a default preset (only when defaults are loaded)
        if (_loadDefaults)
        {
            var existingDefault = LoadDefaults(preset.Tool).FirstOrDefault(p => NameEquals(p.Name, preset.Name));
            if (existingDefault is not null)
            {
                return false;
            }
        }

        preset.Name = preset.Name.Trim();
        preset.IsDefault = false;

        var list = Load().ToList();
        var index = list.FindIndex(p => p.Tool == preset.Tool && NameEquals(p.Name, preset.Name));

        if (index >= 0)
        {
            // Only replace if the existing one is not a default
            if (list[index].IsDefault)
            {
                return false;
            }
            list[index] = preset;
        }
        else
        {
            list.Add(preset);
        }

        return Save(list);
    }

    /// <summary>
    /// Deletes the user preset with the given name inside one tool.
    /// Default presets cannot be deleted. Presets belonging to the other tool are preserved.
    /// Returns false when it did not exist or was a default.
    /// </summary>
    public bool Delete(ToolKind tool, string name)
    {
        // Cannot delete a default preset (only when defaults are loaded)
        if (_loadDefaults)
        {
            var existingDefault = LoadDefaults(tool).FirstOrDefault(p => NameEquals(p.Name, name));
            if (existingDefault is not null)
            {
                return false;
            }
        }

        var list = Load().ToList();
        var removed = list.RemoveAll(p => p.Tool == tool && NameEquals(p.Name, name) && !p.IsDefault);

        if (removed == 0)
        {
            return false;
        }

        return Save(list);
    }

    /// <summary>Checks if a name is reserved by a default preset for the given tool.</summary>
    public bool IsNameReservedByDefault(ToolKind tool, string name)
    {
        if (!_loadDefaults)
        {
            return false;
        }
        return LoadDefaults(tool).Any(p => NameEquals(p.Name, name));
    }

    private static bool NameEquals(string left, string right)
    {
        return string.Equals(left.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string TryProtect(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            // Never fall back to writing the secret in plain text.
            return string.Empty;
        }
    }

    private static string TryUnprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            return string.Empty;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Encrypted by another user/machine or hand-edited: the key must be re-entered.
            return string.Empty;
        }
    }

    private sealed class StoredFile
    {
        public List<StoredPreset>? Presets { get; set; }
    }

    private sealed class StoredPreset
    {
        public string? Name { get; set; }
        public string? BaseUrl { get; set; }
        public string? Model { get; set; }
        public string? ApiKeyProtected { get; set; }
        public string? ApiKey { get; set; } // For defaults (unencrypted)
        public ToolKind Tool { get; set; } = ToolKind.ClaudeCode;
    }
}