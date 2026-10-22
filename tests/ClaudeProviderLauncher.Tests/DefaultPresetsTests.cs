using System.IO;
using System.Reflection;
using System.Text.Json;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class DefaultPresetsTests
{
    private readonly PresetStore _store;

    public DefaultPresetsTests()
    {
        // Use the default constructor which loads embedded defaults
        _store = new PresetStore(Path.Combine(Path.GetTempPath(), "DefaultPresetsTests", Guid.NewGuid().ToString("N"), "presets.json"), loadDefaults: true);
    }

    [Fact]
    public void DefaultPresets_AreLoadedFromEmbeddedResource()
    {
        var defaults = _store.LoadDefaults(ToolKind.ClaudeCode);

        Assert.NotEmpty(defaults);
        Assert.Contains(defaults, p => p.Name == "AnyModel" && p.Tool == ToolKind.ClaudeCode && p.IsDefault);
        Assert.Contains(defaults, p => p.Name == "OpenRouter" && p.Tool == ToolKind.ClaudeCode && p.IsDefault);
    }

    [Fact]
    public void DefaultPresets_AreMarkedAsDefaults()
    {
        var all = _store.Load();

        var anyModel = all.FirstOrDefault(p => p.Name == "AnyModel");
        Assert.NotNull(anyModel);
        Assert.True(anyModel!.IsDefault);
        Assert.Equal(ToolKind.ClaudeCode, anyModel.Tool);
    }

    [Fact]
    public void DefaultPresets_HaveCorrectValues()
    {
        var anyModel = _store.LoadDefaults(ToolKind.ClaudeCode).First(p => p.Name == "AnyModel");

        Assert.Equal("https://anymodel.org", anyModel.BaseUrl);
        Assert.Equal("claude-sonnet-4-5", anyModel.Model);
        Assert.Equal("", anyModel.ApiKey); // No API key in defaults
    }

    [Fact]
    public void DefaultPresets_CannotBeOverwrittenViaUpsert()
    {
        var defaultPreset = _store.LoadDefaults(ToolKind.ClaudeCode).First(p => p.Name == "AnyModel");

        // Try to upsert with the same name - should fail
        var result = _store.Upsert(new ProviderPreset
        {
            Tool = ToolKind.ClaudeCode,
            Name = "AnyModel",
            BaseUrl = "https://evil.example",
            ApiKey = "stolen-key",
            Model = "hacked-model"
        });

        Assert.False(result); // Upsert should fail for default names

        // Original default should be unchanged
        var reloaded = _store.LoadDefaults(ToolKind.ClaudeCode).First(p => p.Name == "AnyModel");
        Assert.Equal("https://anymodel.org", reloaded.BaseUrl);
        Assert.Equal("claude-sonnet-4-5", reloaded.Model);
        Assert.Equal("", reloaded.ApiKey);
    }

    [Fact]
    public void DefaultPresets_CannotBeDeleted()
    {
        var result = _store.Delete(ToolKind.ClaudeCode, "AnyModel");
        Assert.False(result); // Delete should fail for default presets

        // Default should still exist
        var reloaded = _store.LoadDefaults(ToolKind.ClaudeCode);
        Assert.Contains(reloaded, p => p.Name == "AnyModel");
    }

    [Fact]
    public void IsNameReservedByDefault_ReturnsTrueForDefaultNames()
    {
        Assert.True(_store.IsNameReservedByDefault(ToolKind.ClaudeCode, "AnyModel"));
        Assert.True(_store.IsNameReservedByDefault(ToolKind.ClaudeCode, "OpenRouter"));
        Assert.False(_store.IsNameReservedByDefault(ToolKind.ClaudeCode, "MyCustomPreset"));
    }

    [Fact]
    public void UserPresets_CanBeCreatedAlongsideDefaults()
    {
        // Add a user preset
        var userPreset = new ProviderPreset
        {
            Tool = ToolKind.ClaudeCode,
            Name = "MyProvider",
            BaseUrl = "https://my.example",
            ApiKey = "my-secret",
            Model = "my-model"
        };

        Assert.True(_store.Upsert(userPreset));

        // Load should contain both defaults and user preset
        var all = _store.Load(ToolKind.ClaudeCode);
        Assert.Equal(3, all.Count); // 2 defaults + 1 user

        // User preset should be loadable
        var loaded = _store.Find(ToolKind.ClaudeCode, "MyProvider");
        Assert.NotNull(loaded);
        Assert.False(loaded!.IsDefault);
        Assert.Equal("my-secret", loaded.ApiKey);
    }

    [Fact]
    public void UserPresets_AreSeparateFromDefaults()
    {
        var defaults = _store.LoadDefaults(ToolKind.ClaudeCode);
        var user = _store.LoadUser(ToolKind.ClaudeCode);

        Assert.Equal(2, defaults.Count);
        Assert.Empty(user);

        _store.Upsert(new ProviderPreset { Tool = ToolKind.ClaudeCode, Name = "User1", BaseUrl = "https://u1", ApiKey = "k1", Model = "m1" });

        defaults = _store.LoadDefaults(ToolKind.ClaudeCode);
        user = _store.LoadUser(ToolKind.ClaudeCode);

        Assert.Equal(2, defaults.Count); // Defaults unchanged
        Assert.Single(user);
    }

    [Fact]
    public void Load_CombinesDefaultsAndUserPresetsInCorrectOrder()
    {
        _store.Upsert(new ProviderPreset { Tool = ToolKind.ClaudeCode, Name = "UserPreset", BaseUrl = "https://user", ApiKey = "key", Model = "model" });

        var all = _store.Load(ToolKind.ClaudeCode);

        // Defaults first, then user presets
        Assert.Equal("AnyModel", all[0].Name);
        Assert.True(all[0].IsDefault);
        Assert.Equal("OpenRouter", all[1].Name);
        Assert.True(all[1].IsDefault);
        Assert.Equal("UserPreset", all[2].Name);
        Assert.False(all[2].IsDefault);
    }

    [Fact]
    public void EmbeddedResource_IsValidJson()
    {
        // The resource is in the main assembly (ClaudeProviderLauncher), not the test assembly
        var assembly = typeof(PresetStore).Assembly;
        const string resourceName = "ClaudeProviderLauncher.DefaultPresets.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("Presets", out var presets));
        Assert.Equal(JsonValueKind.Array, presets.ValueKind);
        Assert.True(presets.GetArrayLength() >= 2);
    }
}