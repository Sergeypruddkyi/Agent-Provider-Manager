using System.IO;
using System.Text.Json;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class ProfileReloadTests : IDisposable
{
    private readonly string _directory;

    public ProfileReloadTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AgentProviderManager.ProfileReload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
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

    [Fact]
    public void SystemProfile_ClaudeCode_AlwaysReadsLiveConfig()
    {
        // Simulate the scenario: first read with some config
        var settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://first.example",
                    "ANTHROPIC_AUTH_TOKEN": "sk-first"
                },
                "model": "first-model"
            }
            """);

        var firstProfile = SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: settingsPath);
        Assert.Equal("https://first.example", firstProfile.Preset.BaseUrl);
        Assert.Equal("sk-first", firstProfile.Preset.ApiKey);
        Assert.Equal("first-model", firstProfile.Preset.Model);

        // Simulate config change (user edited settings.json externally)
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://second.example",
                    "ANTHROPIC_AUTH_TOKEN": "sk-second"
                },
                "model": "second-model"
            }
            """);

        // Getting the profile again should read the new values
        var secondProfile = SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: settingsPath);
        Assert.Equal("https://second.example", secondProfile.Preset.BaseUrl);
        Assert.Equal("sk-second", secondProfile.Preset.ApiKey);
        Assert.Equal("second-model", secondProfile.Preset.Model);
    }

    [Fact]
    public void SystemProfile_Codex_AlwaysReadsLiveConfig()
    {
        var configPath = Path.Combine(_directory, "config.toml");
        File.WriteAllText(configPath, """
            model = "first-model"
            model_provider = "openai"
            """);

        var firstProfile = SystemProfiles.Get(ToolKind.Codex, codexConfigPath: configPath);
        Assert.Equal("first-model", firstProfile.Preset.Model);

        // Config changes
        File.WriteAllText(configPath, """
            model = "second-model"
            model_provider = "openai"
            """);

        var secondProfile = SystemProfiles.Get(ToolKind.Codex, codexConfigPath: configPath);
        Assert.Equal("second-model", secondProfile.Preset.Model);
    }

    [Fact]
    public void ReloadProfiles_ReturnsFreshSystemProfileEachTime()
    {
        // This test simulates the ReloadProfiles behavior:
        // - Creates a fresh system profile on each call
        // - System profile reflects current config state

        var settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://initial.example",
                    "ANTHROPIC_AUTH_TOKEN": "sk-initial"
                },
                "model": "initial-model"
            }
            """);

        // First "reload"
        var profiles1 = new List<ProfileChoice>
        {
            ProfileChoice.FromSystem(SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: settingsPath)),
        };
        var systemChoice1 = profiles1.First(p => p.IsSystem);
        Assert.Equal("https://initial.example", systemChoice1.Preset?.BaseUrl);

        // Config changes externally
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://updated.example",
                    "ANTHROPIC_AUTH_TOKEN": "sk-updated"
                },
                "model": "updated-model"
            }
            """);

        // Second "reload" (simulates user switching back to system profile)
        var profiles2 = new List<ProfileChoice>
        {
            ProfileChoice.FromSystem(SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: settingsPath)),
        };
        var systemChoice2 = profiles2.First(p => p.IsSystem);
        Assert.Equal("https://updated.example", systemChoice2.Preset?.BaseUrl);
        Assert.Equal("sk-updated", systemChoice2.Preset?.ApiKey);
        Assert.Equal("updated-model", systemChoice2.Preset?.Model);
    }

    [Fact]
    public void UserPreset_RemainsUnchanged_WhenSystemConfigChanges()
    {
        // User presets should not be affected by external config changes
        var store = new PresetStore(Path.Combine(_directory, "presets.json"), loadDefaults: false);
        store.Upsert(new ProviderPreset
        {
            Tool = ToolKind.ClaudeCode,
            Name = "MyPreset",
            BaseUrl = "https://user.example",
            ApiKey = "sk-user",
            Model = "user-model"
        });

        var settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://system.example",
                    "ANTHROPIC_AUTH_TOKEN": "sk-system"
                },
                "model": "system-model"
            }
            """);

        // Reload profiles
        var profiles = new List<ProfileChoice>
        {
            ProfileChoice.FromSystem(SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: settingsPath)),
        };
        profiles.AddRange(store.LoadUser(ToolKind.ClaudeCode).Select(ProfileChoice.FromPreset));

        // System profile reflects current config
        var systemChoice = profiles.First(p => p.IsSystem);
        Assert.Equal("https://system.example", systemChoice.Preset?.BaseUrl);

        // User preset unchanged
        var userChoice = profiles.First(p => p.IsUserPreset);
        Assert.Equal("https://user.example", userChoice.Preset?.BaseUrl);
        Assert.Equal("sk-user", userChoice.Preset?.ApiKey);
        Assert.Equal("user-model", userChoice.Preset?.Model);
    }

    [Fact]
    public void MissingConfigField_ClearsField_OnSystemProfileReload()
    {
        var settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://example.com",
                    "ANTHROPIC_AUTH_TOKEN": "sk-token"
                },
                "model": "some-model"
            }
            """);

        var profile1 = SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: settingsPath);
        Assert.Equal("some-model", profile1.Preset.Model);

        // Config changes - model removed
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://example.com",
                    "ANTHROPIC_AUTH_TOKEN": "sk-token"
                }
            }
            """);

        var profile2 = SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: settingsPath);
        // When model is missing from config, it becomes empty string (not null)
        Assert.Equal(string.Empty, profile2.Preset.Model);
    }
}