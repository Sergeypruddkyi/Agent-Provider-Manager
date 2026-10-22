using System;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Result of CLI detection.
/// </summary>
public sealed class CliDetectionResult
{
    public bool IsFound { get; init; }
    public string? ExecutablePath { get; init; }
    public string? ErrorMessage { get; init; }

    public static CliDetectionResult Found(string executablePath) => new()
    {
        IsFound = true,
        ExecutablePath = executablePath,
    };

    public static CliDetectionResult NotFound(string? reason = null) => new()
    {
        IsFound = false,
        ErrorMessage = reason,
    };

    public static CliDetectionResult Error(string message) => new()
    {
        IsFound = false,
        ErrorMessage = message,
    };
}

/// <summary>
/// Interface for CLI detection to allow testing without real filesystem access.
/// </summary>
public interface ICliDetector
{
    CliDetectionResult DetectClaude();
    CliDetectionResult DetectCodex();
}

/// <summary>
/// Default implementation using the existing locator classes.
/// </summary>
public sealed class DefaultCliDetector : ICliDetector
{
    public CliDetectionResult DetectClaude()
    {
        try
        {
            var path = ClaudeCliLocator.Find();
            if (path is not null)
            {
                return CliDetectionResult.Found(path);
            }
            return CliDetectionResult.NotFound("Claude CLI executable not found in PATH or known locations.");
        }
        catch (Exception ex)
        {
            return CliDetectionResult.Error($"Error detecting Claude CLI: {ex.Message}");
        }
    }

    public CliDetectionResult DetectCodex()
    {
        try
        {
            var path = CodexCliLocator.Find();
            if (path is not null)
            {
                return CliDetectionResult.Found(path);
            }
            return CliDetectionResult.NotFound("Codex CLI executable not found in PATH or known locations.");
        }
        catch (Exception ex)
        {
            return CliDetectionResult.Error($"Error detecting Codex CLI: {ex.Message}");
        }
    }
}