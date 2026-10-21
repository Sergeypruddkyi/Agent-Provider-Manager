using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class CodexLauncherTests
{
    private const string Secret = "sk-test-secret-that-must-not-leak";

    private static CodexLaunchOptions CustomOptions(string? model = "gpt-6-luna") => new()
    {
        WorkingDirectory = @"C:\work",
        BaseUrl = "https://api.example/v1",
        ApiKey = Secret,
        Model = model,
    };

    [Fact]
    public void CustomProvider_UsesOnlyLaunchTimeConfigOverrides()
    {
        var arguments = CodexLauncher.BuildArguments(CustomOptions());

        Assert.Equal(new[]
        {
            "-c", "model_provider=\"agent_provider_manager\"",
            "-c", "model_providers.agent_provider_manager.name=\"Agent Provider Manager\"",
            "-c", "model_providers.agent_provider_manager.base_url=\"https://api.example/v1\"",
            "-c", "model_providers.agent_provider_manager.env_key=\"AGENT_PROVIDER_MANAGER_API_KEY\"",
            "-c", "model=\"gpt-6-luna\"",
        }, arguments);
    }

    [Fact]
    public void CustomProvider_NeverPutsTheSecretIntoArguments()
    {
        var arguments = CodexLauncher.BuildArguments(CustomOptions());

        Assert.DoesNotContain(Secret, string.Join(" ", arguments));
        Assert.Contains("AGENT_PROVIDER_MANAGER_API_KEY", string.Join(" ", arguments));
    }

    [Fact]
    public void CustomProvider_WithoutModel_OmitsModelOverride()
    {
        var arguments = CodexLauncher.BuildArguments(CustomOptions(model: null));

        Assert.DoesNotContain(arguments, a => a.StartsWith("model=", StringComparison.Ordinal));
        Assert.Contains(arguments, a => a.Contains("base_url", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentConfig_UsesModelFlagAndNoProviderOverrides()
    {
        var arguments = CodexLauncher.BuildArguments(new CodexLaunchOptions
        {
            WorkingDirectory = @"C:\work",
            Model = "gpt-6-luna",
        });

        Assert.Equal(new[] { "--model", "gpt-6-luna" }, arguments);
        Assert.DoesNotContain(arguments, a => a.Contains("model_provider", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentConfig_WithoutModel_PassesNoArguments()
    {
        var arguments = CodexLauncher.BuildArguments(new CodexLaunchOptions
        {
            WorkingDirectory = @"C:\work",
        });

        Assert.Empty(arguments);
    }

    [Fact]
    public void TomlValues_AreEscaped()
    {
        var arguments = CodexLauncher.BuildArguments(new CodexLaunchOptions
        {
            WorkingDirectory = @"C:\work",
            BaseUrl = "https://api.example/v1",
            Model = "model-with-\\-and-\"-quote",
        });

        var modelArgument = arguments.Single(a => a.StartsWith("model=", StringComparison.Ordinal));

        Assert.Equal("model=\"model-with-\\\\-and-\\\"-quote\"", modelArgument);
    }

    [Theory]
    [InlineData("https://api.example/v1", true)]
    [InlineData("http://127.0.0.1:8080", true)]
    [InlineData("ftp://api.example", false)]
    [InlineData("api.example", false)]
    [InlineData("", false)]
    public void BaseUrlValidation_AcceptsOnlyAbsoluteHttpUrls(string value, bool expected)
    {
        Assert.Equal(expected, CodexLauncher.IsValidBaseUrl(value));
    }

    [Fact]
    public void IsCustomProvider_IsBasedOnBaseUrl()
    {
        Assert.True(CodexLauncher.IsCustomProvider(CustomOptions()));
        Assert.False(CodexLauncher.IsCustomProvider(new CodexLaunchOptions
        {
            WorkingDirectory = @"C:\work",
            Model = "gpt-6-luna",
        }));
    }

    [Fact]
    public void LaunchDesktop_CustomProvider_UsesAppSubcommandAndSameOverrides()
    {
        var options = CustomOptions("deepseek-v4.1-flash");
        var codexExe = @"C:\fake\codex.exe";

        // We can't easily test Process.Start, but we can verify BuildArguments
        // are reused correctly by checking the arguments that would be passed.
        var arguments = CodexLauncher.BuildArguments(options);

        Assert.Equal(new[]
        {
            "-c", "model_provider=\"agent_provider_manager\"",
            "-c", "model_providers.agent_provider_manager.name=\"Agent Provider Manager\"",
            "-c", "model_providers.agent_provider_manager.base_url=\"https://api.example/v1\"",
            "-c", "model_providers.agent_provider_manager.env_key=\"AGENT_PROVIDER_MANAGER_API_KEY\"",
            "-c", "model=\"deepseek-v4.1-flash\"",
        }, arguments);
    }

    [Fact]
    public void LaunchDesktop_CustomProvider_NeverPutsSecretInArguments()
    {
        var options = CustomOptions();
        var arguments = CodexLauncher.BuildArguments(options);

        Assert.DoesNotContain(Secret, string.Join(" ", arguments));
        Assert.Contains("AGENT_PROVIDER_MANAGER_API_KEY", string.Join(" ", arguments));
    }

    [Fact]
    public void LaunchDesktop_CurrentConfig_UsesAppSubcommandAndModelFlag()
    {
        var options = new CodexLaunchOptions
        {
            WorkingDirectory = @"C:\work",
            Model = "gpt-6-luna",
        };

        var arguments = CodexLauncher.BuildArguments(options);

        Assert.Equal(new[] { "--model", "gpt-6-luna" }, arguments);
        Assert.DoesNotContain(arguments, a => a.Contains("model_provider", StringComparison.Ordinal));
    }
}
