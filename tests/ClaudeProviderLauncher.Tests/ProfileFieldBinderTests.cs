using System.IO;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

/// <summary>
/// Records the values <see cref="ProfileFieldBinder"/> delivers, so tests can
/// verify not only that a config/profile is read correctly, but also that its
/// values actually reach the profile-field filling step of the UI.
/// </summary>
public sealed class RecordingFieldTarget : IProfileFieldTarget
{
    public string BaseUrl { get; private set; } = "unassigned";
    public string ApiKey { get; private set; } = "unassigned";
    public string Model { get; private set; } = "unassigned";

    public void SetFields(string baseUrl, string apiKey, string model)
    {
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        Model = model;
    }
}

/// <summary>
/// Covers the last step of the loading path: the selected profile choice must
/// deliver its values into the UI fields, and a missing value must clear the
/// corresponding field instead of leaving the previous profile's value behind.
/// </summary>
public sealed class ProfileFieldBinderTests : IDisposable
{
    private readonly string _directory;

    public ProfileFieldBinderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AgentProviderManager.FieldBinder", Guid.NewGuid().ToString("N"));
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

    private static ProfileChoice SystemChoiceFromClaudeConfig(string settingsPath)
    {
        return ProfileChoice.FromSystem(SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: settingsPath));
    }

    /// <summary>
    /// The scenario from the requirements: the system configuration contains
    /// system-model and https://system.example, the user preset contains
    /// user-model and https://user.example.
    /// </summary>
    [Fact]
    public void SystemChoice_DeliversSystemValues_ExactlyAsInConfig()
    {
        var settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://system.example",
                    "ANTHROPIC_AUTH_TOKEN": "token-system"
                },
                "model": "system-model"
            }
            """);

        var target = new RecordingFieldTarget();
        ProfileFieldBinder.Apply(SystemChoiceFromClaudeConfig(settingsPath), target);

        Assert.Equal("https://system.example", target.BaseUrl);
        Assert.Equal("token-system", target.ApiKey);
        Assert.Equal("system-model", target.Model);
    }

    [Fact]
    public void UserPresetChoice_DeliversPresetValues_ExactlyAsStored()
    {
        var target = new RecordingFieldTarget();
        ProfileFieldBinder.Apply(UserChoice("https://user.example", "token-user", "user-model"), target);

        Assert.Equal("https://user.example", target.BaseUrl);
        Assert.Equal("token-user", target.ApiKey);
        Assert.Equal("user-model", target.Model);
    }

    /// <summary>
    /// Selecting user preset → system → user preset changes the delivered values
    /// every time; a system selection must never deliver values of the previously
    /// selected user preset (and vice versa).
    /// </summary>
    [Fact]
    public void SwitchBetweenProfiles_DeliversValuesOfSelectedProfile_EachTime()
    {
        var settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://system.example",
                    "ANTHROPIC_AUTH_TOKEN": "token-system"
                },
                "model": "system-model"
            }
            """);

        var systemChoice = SystemChoiceFromClaudeConfig(settingsPath);
        var userChoice = UserChoice("https://user.example", "token-user", "user-model");

        var target = new RecordingFieldTarget();

        ProfileFieldBinder.Apply(systemChoice, target);
        Assert.Equal("system-model", target.Model);
        Assert.Equal("https://system.example", target.BaseUrl);
        Assert.Equal("token-system", target.ApiKey);

        ProfileFieldBinder.Apply(userChoice, target);
        Assert.Equal("user-model", target.Model);
        Assert.Equal("https://user.example", target.BaseUrl);
        Assert.Equal("token-user", target.ApiKey);

        // Back to the system profile: is the user preset's values once more.
        ProfileFieldBinder.Apply(systemChoice, target);
        Assert.Equal("system-model", target.Model);
        Assert.Equal("https://system.example", target.BaseUrl);
        Assert.Equal("token-system", target.ApiKey);
    }

    /// <summary>
    /// The system profile does contain SystemProfiles values, and the binder's job
    /// is to transfer them into the UI fields even after a user preset was showing
    /// its values. The preset must not leak into the system fields.
    /// </summary>
    [Fact]
    public void BackToSystemProfile_UserValuesAreReplaced_NotMerged()
    {
        var settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://system.example"
                },
                "model": "system-model"
            }
            """);

        var systemChoice = SystemChoiceFromClaudeConfig(settingsPath);
        var target = new RecordingFieldTarget();

        // The user preset had a complete profile; switching back to the system
        // profile (without a token in settings.json) must deliver an empty key.
        ProfileFieldBinder.Apply(UserChoice("https://user.example", "token-user", "user-model"), target);
        ProfileFieldBinder.Apply(systemChoice, target);

        Assert.Equal("https://system.example", target.BaseUrl);
        Assert.Equal(string.Empty, target.ApiKey);
        Assert.Equal("system-model", target.Model);
    }

    /// <summary>
    /// A selected profile without a specific value must deliver an empty string for
    /// it so the corresponding field is cleared, not silently kept filled with the
    /// previous profile's value.
    /// </summary>
    [Fact]
    public void MissingPresetValue_DeliversEmptyString_FieldIsCleared()
    {
        var target = new RecordingFieldTarget();

        // First show a complete profile ...
        ProfileFieldBinder.Apply(UserChoice("https://previous.example", "token-previous", "previous-model"), target);
        Assert.Equal("previous-model", target.Model);

        // ... then a preset that only stores a base URL: the rest must clear.
        ProfileFieldBinder.Apply(UserChoice("https://user.example", string.Empty, string.Empty), target);

        Assert.Equal("https://user.example", target.BaseUrl);
        Assert.Equal(string.Empty, target.ApiKey);
        Assert.Equal(string.Empty, target.Model);
    }

    [Fact]
    public void NullChoice_ClearsAllFields()
    {
        var target = new RecordingFieldTarget();
        ProfileFieldBinder.Apply(UserChoice("https://user.example", "token-user", "user-model"), target);

        ProfileFieldBinder.Apply(null, target);

        Assert.Equal(string.Empty, target.BaseUrl);
        Assert.Equal(string.Empty, target.ApiKey);
        Assert.Equal(string.Empty, target.Model);
    }

    [Fact]
    public void CodexSystemChoice_DeliversModelFromConfigToml_AndEmptyWhenAbsent()
    {
        var configPath = Path.Combine(_directory, "config.toml");
        File.WriteAllText(configPath, """
            model = "codex-model"
            model_provider = "openai"

            [model_providers.openai]
            base_url = "https://openai.example/v1"
            env_key = "OPENAI_API_KEY"
            """);

        var target = new RecordingFieldTarget();
        ProfileFieldBinder.Apply(
            ProfileChoice.FromSystem(SystemProfiles.Get(ToolKind.Codex, codexConfigPath: configPath)),
            target);

        Assert.Equal("https://openai.example/v1", target.BaseUrl);
        Assert.Equal("OPENAI_API_KEY", target.ApiKey);
        Assert.Equal("codex-model", target.Model);

        // A config without provider values leaves the corresponding fields empty.
        var emptyConfigPath = Path.Combine(_directory, "empty", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(emptyConfigPath)!);
        File.WriteAllText(emptyConfigPath, "[tui]\n");

        var secondTarget = new RecordingFieldTarget();
        ProfileFieldBinder.Apply(
            ProfileChoice.FromSystem(SystemProfiles.Get(ToolKind.Codex, codexConfigPath: emptyConfigPath)),
            secondTarget);

        Assert.Equal(string.Empty, secondTarget.BaseUrl);
        Assert.Equal(string.Empty, secondTarget.ApiKey);
        Assert.Equal(string.Empty, secondTarget.Model);
    }

    private static ProfileChoice UserChoice(string baseUrl, string apiKey, string model)
    {
        return ProfileChoice.FromPreset(new ProviderPreset
        {
            Name = "TestUserPreset",
            Tool = ToolKind.ClaudeCode,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Model = model,
        });
    }
}
