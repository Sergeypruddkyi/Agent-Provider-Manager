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
    public void ClaudeDefaultProfile_KeepsExistingLastValuesAndNeverRestoresApiKey()
    {
        var last = new LastValues
        {
            BaseUrl = "https://claude.example",
            Model = "claude-model",
            WorkingDirectory = @"C:\work",
        };

        var profile = SystemProfiles.Get(ToolKind.ClaudeCode, claudeLastValues: last);

        Assert.Equal("https://claude.example", profile.Preset.BaseUrl);
        Assert.Equal("claude-model", profile.Preset.Model);
        Assert.Equal(string.Empty, profile.Preset.ApiKey);
        Assert.Equal(ToolKind.ClaudeCode, profile.Preset.Tool);
    }
}
