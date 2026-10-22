using System.IO;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class CodexConfigReaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "AgentProviderManager.CodexConfig", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, "config.toml");

    public CodexConfigReaderTests()
    {
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

    private void WriteConfig(string contents) => File.WriteAllText(ConfigPath, contents);

    [Fact]
    public void MissingFile_IsValidAndEmpty()
    {
        var snapshot = CodexConfigReader.Read(ConfigPath);

        Assert.False(snapshot.HasError);
        Assert.Null(snapshot.Model);
        Assert.Null(snapshot.ModelProvider);
    }

    [Fact]
    public void ReadsTopLevelModelAndProvider_AndIgnoresTables()
    {
        WriteConfig("""
            # current Codex configuration
            model = "gpt-6-luna"
            model_provider = "openai"

            [tui]
            screen_reader_detection_done = true

            [model_providers.custom]
            model = "nested-model"
            base_url = "https://custom.example/v1"
            """);

        var snapshot = CodexConfigReader.Read(ConfigPath);

        Assert.False(snapshot.HasError);
        Assert.Equal("gpt-6-luna", snapshot.Model);
        Assert.Equal("openai", snapshot.ModelProvider);
    }

    [Fact]
    public void SupportsSingleQuotesAndTrailingComments()
    {
        WriteConfig("""
            model = 'literal-model' # comment
            model_provider = "example" # another comment
            """);

        var snapshot = CodexConfigReader.Read(ConfigPath);

        Assert.Equal("literal-model", snapshot.Model);
        Assert.Equal("example", snapshot.ModelProvider);
    }

    [Fact]
    public void EscapedQuotesInModel_AreDecoded()
    {
        WriteConfig("model = \"model-\\\"quoted\\\"\"");

        var snapshot = CodexConfigReader.Read(ConfigPath);

        Assert.False(snapshot.HasError);
        Assert.Equal("model-\"quoted\"", snapshot.Model);
    }

    [Fact]
    public void UnterminatedString_IsReportedAsError()
    {
        WriteConfig("model = \"unterminated");

        var snapshot = CodexConfigReader.Read(ConfigPath);

        Assert.True(snapshot.HasError);
        Assert.Contains("line 1", snapshot.Error);
    }

    [Fact]
    public void UnquotedModelValue_IsReportedAsError()
    {
        WriteConfig("model = gpt-6-luna");

        var snapshot = CodexConfigReader.Read(ConfigPath);

        Assert.True(snapshot.HasError);
        Assert.Null(snapshot.Model);
    }

    [Fact]
    public void MissingConfig_DoesNotPreventCodexDefaultProfile()
    {
        var profile = SystemProfiles.Get(ToolKind.Codex, ConfigPath);

        Assert.False(profile.Preset.BaseUrl.Length > 0);
        Assert.False(profile.Preset.ApiKey.Length > 0);
        Assert.False(string.IsNullOrEmpty(profile.DisplayName));
        Assert.True(profile.UsesCurrentCodexConfig);
        Assert.Null(profile.Error);
    }

    [Fact]
    public void ExistingConfig_IsUsedForCodexDefaultProfile()
    {
        WriteConfig("""
            model = "gpt-6-luna"
            model_provider = "openai"
            [windows]
            sandbox = "elevated"
            """);

        var profile = SystemProfiles.Get(ToolKind.Codex, ConfigPath);

        Assert.Equal("gpt-6-luna", profile.Preset.Model);
        Assert.Equal(string.Empty, profile.Preset.BaseUrl);
        Assert.Equal(string.Empty, profile.Preset.ApiKey);
        Assert.Null(profile.Error);
    }

    [Fact]
    public void ClaudeDefaultProfile_ReadsFromSettingsJson_WhenAvailable()
    {
        // Test the new ClaudeConfigReader directly
        var tempFile = Path.Combine(_directory, "settings.json");
        File.WriteAllText(tempFile, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://claude.example",
                    "ANTHROPIC_AUTH_TOKEN": "sk-auth-token",
                    "ANTHROPIC_API_KEY": "sk-api-key"
                },
                "model": "claude-model"
            }
            """);

        var snapshot = ClaudeConfigReader.Read(tempFile);

        Assert.False(snapshot.HasError);
        Assert.Equal("https://claude.example", snapshot.BaseUrl);
        Assert.Equal("sk-auth-token", snapshot.AuthToken);
        Assert.Equal("sk-api-key", snapshot.ApiKey);
        Assert.Equal("claude-model", snapshot.Model);
    }

    [Fact]
    public void ClaudeDefaultProfile_PrefersAuthTokenOverApiKey()
    {
        var tempFile = Path.Combine(_directory, "settings.json");
        File.WriteAllText(tempFile, """
            {
                "env": {
                    "ANTHROPIC_AUTH_TOKEN": "sk-auth-token",
                    "ANTHROPIC_API_KEY": "sk-api-key"
                },
                "model": "claude-model"
            }
            """);

        var snapshot = ClaudeConfigReader.Read(tempFile);

        Assert.Equal("sk-auth-token", snapshot.AuthToken);
        Assert.Equal("sk-api-key", snapshot.ApiKey);
    }

    [Fact]
    public void MissingClaudeConfig_IsValidAndEmpty()
    {
        var snapshot = ClaudeConfigReader.Read(Path.Combine(_directory, "nonexistent.json"));

        Assert.False(snapshot.HasError);
        Assert.Null(snapshot.BaseUrl);
        Assert.Null(snapshot.AuthToken);
        Assert.Null(snapshot.ApiKey);
        Assert.Null(snapshot.Model);
    }

    [Fact]
    public void CorruptedClaudeConfig_IsReportedAsError()
    {
        var tempFile = Path.Combine(_directory, "settings.json");
        File.WriteAllText(tempFile, "{ not valid json }");

        var snapshot = ClaudeConfigReader.Read(tempFile);

        Assert.True(snapshot.HasError);
        Assert.Contains("settings.json", snapshot.Error);
    }

    [Fact]
    public void SystemProfile_ClaudeCode_UsesClaudeConfigReader()
    {
        var tempFile = Path.Combine(_directory, "settings.json");
        File.WriteAllText(tempFile, """
            {
                "env": {
                    "ANTHROPIC_BASE_URL": "https://claude.example",
                    "ANTHROPIC_AUTH_TOKEN": "sk-auth-token"
                },
                "model": "claude-model"
            }
            """);

        var profile = SystemProfiles.Get(ToolKind.ClaudeCode, claudeConfigPath: tempFile);

        Assert.Equal("https://claude.example", profile.Preset.BaseUrl);
        Assert.Equal("sk-auth-token", profile.Preset.ApiKey); // AuthToken preferred
        Assert.Equal("claude-model", profile.Preset.Model);
        Assert.Null(profile.Error);
    }
}
