using System.IO;
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
}

/// <summary>
/// Best-effort persistence of named provider presets in a small JSON file next to the
/// other per-user launcher state (<c>%LOCALAPPDATA%\ClaudeProviderLauncher\presets.json</c>).
/// The API key is encrypted at rest with Windows DPAPI (CurrentUser scope), so it is only
/// readable by the same Windows user on this machine and never stored in plain text.
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

    private readonly string _filePath;

    public PresetStore()
        : this(Path.Combine(AppPaths.DataDirectory, "presets.json"))
    {
    }

    /// <summary>Path-injectable constructor, primarily for tests.</summary>
    public PresetStore(string filePath)
    {
        _filePath = filePath;
    }

    public string FilePath => _filePath;

    /// <summary>Loads all presets from every tool in stored order. Never throws.</summary>
    public IReadOnlyList<ProviderPreset> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(_filePath), SerializerOptions);
            if (file?.Presets is null)
            {
                return [];
            }

            var result = new List<ProviderPreset>();
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
                });
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Loads only the presets belonging to <paramref name="tool"/>.</summary>
    public IReadOnlyList<ProviderPreset> Load(ToolKind tool) =>
        Load().Where(p => p.Tool == tool).ToList();

    /// <summary>Persists the full preset list. Returns false when the file could not be written.</summary>
    public bool Save(IReadOnlyList<ProviderPreset> presets)
    {
        try
        {
            var file = new StoredFile
            {
                Presets = presets
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

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(file, SerializerOptions));
            return true;
        }
        catch
        {
            // Disk/DPAPI failures must not crash the app; the caller decides how to report.
            return false;
        }
    }

    /// <summary>Returns the Claude Code preset with the given name (case-insensitive), or null.</summary>
    public ProviderPreset? Find(string name) => Find(ToolKind.ClaudeCode, name);

    /// <summary>Returns the preset with the given name inside one tool, or null.</summary>
    public ProviderPreset? Find(ToolKind tool, string name)
    {
        return Load(tool).FirstOrDefault(p => NameEquals(p.Name, name));
    }

    /// <summary>
    /// Inserts a new preset or overwrites the existing preset with the same name
    /// inside the same tool (case-insensitive). A preset in the other tool with
    /// the same name is never touched. Returns false when the file could not be
    /// written.
    /// </summary>
    public bool Upsert(ProviderPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            return false;
        }

        preset.Name = preset.Name.Trim();

        var list = Load().ToList();
        var index = list.FindIndex(p => p.Tool == preset.Tool && NameEquals(p.Name, preset.Name));

        if (index >= 0)
        {
            list[index] = preset;
        }
        else
        {
            list.Add(preset);
        }

        return Save(list);
    }

    /// <summary>Deletes the Claude Code preset with the given name.</summary>
    public bool Delete(string name) => Delete(ToolKind.ClaudeCode, name);

    /// <summary>
    /// Deletes the preset with the given name inside one tool. Presets belonging to
    /// the other tool are preserved. Returns false when it did not exist.
    /// </summary>
    public bool Delete(ToolKind tool, string name)
    {
        var list = Load().ToList();
        var removed = list.RemoveAll(p => p.Tool == tool && NameEquals(p.Name, name));

        if (removed == 0)
        {
            return false;
        }

        return Save(list);
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
        public ToolKind Tool { get; set; } = ToolKind.ClaudeCode;
    }
}
