using System.IO;
using System.Text.Json;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class ToolPresetIsolationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "AgentProviderManager.Tests", Guid.NewGuid().ToString("N"));

    private readonly string _filePath;

    public ToolPresetIsolationTests()
    {
        _filePath = Path.Combine(_directory, "presets.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }

    private PresetStore CreateStore() => new(_filePath);

    private static ProviderPreset Preset(
        ToolKind tool,
        string name = "Provider",
        string baseUrl = "https://provider.example",
        string apiKey = "secret-key",
        string model = "model")
        => new() { Tool = tool, Name = name, BaseUrl = baseUrl, ApiKey = apiKey, Model = model };

    [Fact]
    public void Load_WithToolFilter_ReturnsOnlyThatToolsPresets()
    {
        var store = CreateStore();
        store.Upsert(Preset(ToolKind.ClaudeCode, name: "Claude preset"));
        store.Upsert(Preset(ToolKind.Codex, name: "Codex preset"));

        var claude = store.Load(ToolKind.ClaudeCode);
        var codex = store.Load(ToolKind.Codex);

        Assert.Single(claude);
        Assert.Single(codex);
        Assert.Equal("Claude preset", claude[0].Name);
        Assert.Equal("Codex preset", codex[0].Name);
    }

    [Fact]
    public void SameNameInBothTools_IsStoredAndSelectedIndependently()
    {
        var store = CreateStore();
        store.Upsert(Preset(ToolKind.ClaudeCode, name: "Shared", baseUrl: "https://claude.example", model: "claude-model"));
        store.Upsert(Preset(ToolKind.Codex, name: "Shared", baseUrl: "https://codex.example", model: "codex-model"));

        Assert.Equal(2, store.Load().Count);
        Assert.Equal("https://claude.example", store.Find(ToolKind.ClaudeCode, "Shared")!.BaseUrl);
        Assert.Equal("https://codex.example", store.Find(ToolKind.Codex, "Shared")!.BaseUrl);
    }

    [Fact]
    public void Delete_IsScopedToOneTool()
    {
        var store = CreateStore();
        store.Upsert(Preset(ToolKind.ClaudeCode, name: "Shared"));
        store.Upsert(Preset(ToolKind.Codex, name: "Shared"));

        Assert.True(store.Delete(ToolKind.Codex, "Shared"));

        Assert.NotNull(store.Find(ToolKind.ClaudeCode, "Shared"));
        Assert.Null(store.Find(ToolKind.Codex, "Shared"));
    }

    [Fact]
    public void LegacyPresetWithoutTool_LoadsAsClaudeCode()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, """
            {
              "Presets": [
                {
                  "Name": "Legacy",
                  "BaseUrl": "https://legacy.example",
                  "Model": "legacy-model",
                  "ApiKeyProtected": ""
                }
              ]
            }
            """);

        var loaded = Assert.Single(CreateStore().Load());

        Assert.Equal(ToolKind.ClaudeCode, loaded.Tool);
        Assert.Equal("Legacy", loaded.Name);
    }

    [Fact]
    public void SavedFile_StoresToolByName()
    {
        CreateStore().Upsert(Preset(ToolKind.Codex, name: "Codex preset"));

        using var document = JsonDocument.Parse(File.ReadAllText(_filePath));
        var preset = document.RootElement.GetProperty("Presets")[0];

        Assert.Equal("Codex", preset.GetProperty("Tool").GetString());
    }

    [Fact]
    public void SystemProfileIds_AreStableAndDistinct()
    {
        Assert.NotEqual(SystemProfileIds.ClaudeDefault, SystemProfileIds.CodexDefault);
        Assert.Equal(SystemProfileIds.ClaudeDefault, SystemProfileIds.For(ToolKind.ClaudeCode));
        Assert.Equal(SystemProfileIds.CodexDefault, SystemProfileIds.For(ToolKind.Codex));
    }

    [Fact]
    public void ProfileChoice_RecognizesSystemByIdNotByDisplayName()
    {
        var claudeSystem = ProfileChoice.FromSystem(new SystemProfile
        {
            Tool = ToolKind.ClaudeCode,
            DisplayName = SystemProfiles.ClaudeDisplayName,
            Preset = new ProviderPreset { Tool = ToolKind.ClaudeCode },
        });

        var maliciousUserPreset = ProfileChoice.FromPreset(new ProviderPreset
        {
            Tool = ToolKind.ClaudeCode,
            Name = SystemProfiles.ClaudeDisplayName,
        });

        Assert.True(claudeSystem.IsSystem);
        Assert.False(maliciousUserPreset.IsSystem);
    }

    [Fact]
    public void ReservedSystemNames_AreRejectedForUserPresets()
    {
        Assert.True(SystemProfiles.IsReservedName(ToolKind.ClaudeCode, SystemProfiles.ClaudeDisplayName));
        Assert.True(SystemProfiles.IsReservedName(ToolKind.Codex, " system:codex:default "));
        Assert.False(SystemProfiles.IsReservedName(ToolKind.Codex, "My provider"));
    }
}
