using System.IO;
using System.Text.Json.Nodes;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class ProviderConfigurationManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClaudeProviderLauncherTests", Guid.NewGuid().ToString("N"));
    private string Settings => Path.Combine(_directory, ".claude", "settings.json");
    private string Backup => Path.Combine(_directory, ".claude", "provider-manager", "default-settings.json");

    public ProviderConfigurationManagerTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        File.WriteAllText(Settings, "{\"env\":{\"ANTHROPIC_BASE_URL\":\"https://paid.example\",\"ANTHROPIC_AUTH_TOKEN\":\"original-token\",\"ANTHROPIC_API_KEY\":\"old-key\",\"KEEP\":\"value\"},\"model\":\"paid-model\",\"theme\":\"dark\",\"mcpServers\":{\"fetch\":{\"command\":\"fetch\"}}}");
    }

    [Fact]
    public void ApplyWritesProviderValuesAndPreservesAllOtherSettings()
    {
        Manager().Apply(new ProviderPreset { BaseUrl = "http://127.0.0.1:31415", ApiKey = "test-token", Model = "Free-claude-code" });

        var root = JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();
        Assert.Equal("http://127.0.0.1:31415", root["env"]!["ANTHROPIC_BASE_URL"]!.GetValue<string>());
        Assert.Equal("test-token", root["env"]!["ANTHROPIC_AUTH_TOKEN"]!.GetValue<string>());
        Assert.Null(root["env"]!["ANTHROPIC_API_KEY"]);
        Assert.Null(root["env"]!["ANTHROPIC_MODEL"]);
        Assert.Equal("Free-claude-code", root["model"]!.GetValue<string>());
        Assert.Equal("value", root["env"]!["KEEP"]!.GetValue<string>());
        Assert.Equal("dark", root["theme"]!.GetValue<string>());
        Assert.Equal("fetch", root["mcpServers"]!["fetch"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void FirstApplySavesOriginalSettingsToSinglePermanentBackup()
    {
        var original = File.ReadAllText(Settings);

        Manager().Apply(Profile());

        Assert.True(File.Exists(Backup));
        Assert.Equal(original, File.ReadAllText(Backup));
    }

    [Fact]
    public void SecondApplyReplacesPreviousValuesWithoutNewBackups()
    {
        var manager = Manager();
        manager.Apply(new ProviderPreset { BaseUrl = "https://one.example", ApiKey = "token-one", Model = "model-one" });
        var backupAfterFirstApply = File.ReadAllBytes(Backup);

        manager.Apply(new ProviderPreset { BaseUrl = "https://two.example", ApiKey = "token-two", Model = "model-two" });

        var root = JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();
        Assert.Equal("https://two.example", root["env"]!["ANTHROPIC_BASE_URL"]!.GetValue<string>());
        Assert.Equal("token-two", root["env"]!["ANTHROPIC_AUTH_TOKEN"]!.GetValue<string>());
        Assert.Equal("model-two", root["model"]!.GetValue<string>());
        Assert.DoesNotContain("token-one", File.ReadAllText(Settings));
        Assert.DoesNotContain("model-one", File.ReadAllText(Settings));
        Assert.Equal(backupAfterFirstApply, File.ReadAllBytes(Backup));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Backup)!));
    }

    [Fact]
    public void ExistingBackupIsNeverOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Backup)!);
        const string existingBackup = "{\"env\":{\"ANTHROPIC_BASE_URL\":\"https://kept.example\"}}";
        File.WriteAllText(Backup, existingBackup);

        Manager().Apply(Profile());

        Assert.Equal(existingBackup, File.ReadAllText(Backup));
    }

    [Fact]
    public void RestoreCopiesOriginalSettingsBackAndKeepsBackup()
    {
        var original = File.ReadAllText(Settings);
        var manager = Manager();
        manager.Apply(Profile());

        manager.Restore();

        Assert.Equal(original, File.ReadAllText(Settings));
        Assert.True(File.Exists(Backup));
        Assert.Equal(original, File.ReadAllText(Backup));
    }

    [Fact]
    public void TryGetOriginalProfileReturnsOriginalValuesNotTheTemporaryOnes()
    {
        var manager = Manager();
        manager.Apply(Profile(model: "temporary-model"));
        var backupBeforeRead = File.ReadAllBytes(Backup);

        Assert.True(manager.TryGetOriginalProfile(out var original, out _));

        Assert.Equal("https://paid.example", original.BaseUrl);
        Assert.Equal("original-token", original.ApiKey);
        Assert.Equal("paid-model", original.Model);
        Assert.DoesNotContain("temporary-model", original.Model);
        Assert.Equal(backupBeforeRead, File.ReadAllBytes(Backup));
    }

    [Fact]
    public void TryGetOriginalProfileFailsWhenBackupIsMissing()
    {
        Assert.False(Manager().TryGetOriginalProfile(out _, out _));
    }

    [Fact]
    public void TryGetOriginalProfileFailsWhenOriginalProfileIsIncomplete()
    {
        File.WriteAllText(Settings, "{\"env\":{\"ANTHROPIC_BASE_URL\":\"https://paid.example\",\"ANTHROPIC_AUTH_TOKEN\":\"original-token\"}}");
        var manager = Manager();
        manager.Apply(Profile());

        Assert.False(manager.TryGetOriginalProfile(out _, out _));
    }

    [Fact]
    public void ResolveStartProfilePrefersExplicitlySelectedValidPreset()
    {
        var preset = new ProviderPreset { Name = "Free LLM API", BaseUrl = "https://free.example", ApiKey = "free-token", Model = "Free-claude-code" };

        var resolved = ProviderConfigurationManager.ResolveStartProfile(preset, "https://paid.example", "paid-token", "paid-model");

        Assert.Same(preset, resolved);
    }

    [Fact]
    public void ResolveStartProfileUsesFieldValuesWhenNoPresetIsSelected()
    {
        var resolved = ProviderConfigurationManager.ResolveStartProfile(null, "https://paid.example", "paid-token", "paid-model");

        Assert.NotNull(resolved);
        Assert.Equal("https://paid.example", resolved.BaseUrl);
        Assert.Equal("paid-token", resolved.ApiKey);
        Assert.Equal("paid-model", resolved.Model);
    }

    [Fact]
    public void ResolveStartProfileFallsBackToFieldsWhenSelectedPresetIsIncomplete()
    {
        var incomplete = new ProviderPreset { Name = "broken", BaseUrl = "https://free.example", ApiKey = "", Model = "m" };

        var resolved = ProviderConfigurationManager.ResolveStartProfile(incomplete, "https://paid.example", "paid-token", "paid-model");

        Assert.NotNull(resolved);
        Assert.Equal("paid-token", resolved.ApiKey);
        Assert.Null(ProviderConfigurationManager.ResolveStartProfile(null, "https://paid.example", "", "paid-model"));
    }

    [Fact]
    public void RestoreThenResolveUsesOriginalProfileNotThePreviouslyAppliedOne()
    {
        // Regression for the stale-preset bug: after Apply(temporary) + Restore the UI
        // clears the preset selection and syncs the fields from the backup, so Start
        // resolves the original profile - not the temporary one.
        var manager = Manager();
        manager.Apply(Profile(model: "temporary-model"));
        manager.Restore();
        Assert.True(manager.TryGetOriginalProfile(out var original, out _));

        var resolved = ProviderConfigurationManager.ResolveStartProfile(selectedPreset: null, original.BaseUrl, original.ApiKey, original.Model);

        Assert.NotNull(resolved);
        Assert.Equal("https://paid.example", resolved.BaseUrl);
        Assert.Equal("original-token", resolved.ApiKey);
        Assert.Equal("paid-model", resolved.Model);
        Assert.DoesNotContain("temporary", resolved.Model, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreWithoutBackupFails()
    {
        Assert.ThrowsAny<Exception>(() => Manager().Restore());
    }

    [Fact]
    public void MissingOrInvalidProfileIsNeverAccepted()
    {
        Assert.False(ProviderConfigurationManager.IsValidProfile("", "token", "model"));
        Assert.False(ProviderConfigurationManager.IsValidProfile("ftp://host", "token", "model"));
        Assert.False(ProviderConfigurationManager.IsValidProfile("https://host", "", "model"));
        Assert.False(ProviderConfigurationManager.IsValidProfile("https://host", "token", " "));
    }

    private ProviderConfigurationManager Manager() => new(Settings, Backup);

    private static ProviderPreset Profile(string model = "Free-claude-code", string url = "https://provider.example") =>
        new() { BaseUrl = url, ApiKey = "test-token", Model = model };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
