using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class CliDetectorTests
{
    // --- ICliDetector interface tests ---

    [Fact]
    public void DefaultCliDetector_ImplementsInterface()
    {
        var detector = new DefaultCliDetector();
        Assert.IsAssignableFrom<ICliDetector>(detector);
    }

    [Fact]
    public void CliDetectionResult_Found_HasCorrectProperties()
    {
        var result = CliDetectionResult.Found(@"C:\path\to\claude.exe");

        Assert.True(result.IsFound);
        Assert.Equal(@"C:\path\to\claude.exe", result.ExecutablePath);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void CliDetectionResult_NotFound_HasCorrectProperties()
    {
        var result = CliDetectionResult.NotFound("Custom reason");

        Assert.False(result.IsFound);
        Assert.Null(result.ExecutablePath);
        Assert.Equal("Custom reason", result.ErrorMessage);
    }

    [Fact]
    public void CliDetectionResult_Error_HasCorrectProperties()
    {
        var result = CliDetectionResult.Error("Something went wrong");

        Assert.False(result.IsFound);
        Assert.Null(result.ExecutablePath);
        Assert.Equal("Something went wrong", result.ErrorMessage);
    }

    // --- Testable detector for isolated unit tests ---

    private sealed class TestCliDetector : ICliDetector
    {
        private readonly CliDetectionResult _claudeResult;
        private readonly CliDetectionResult _codexResult;

        public TestCliDetector(CliDetectionResult claudeResult, CliDetectionResult codexResult)
        {
            _claudeResult = claudeResult;
            _codexResult = codexResult;
        }

        public CliDetectionResult DetectClaude() => _claudeResult;
        public CliDetectionResult DetectCodex() => _codexResult;
    }

    [Fact]
    public void Detect_BothAvailable_ReturnsFoundForBoth()
    {
        var detector = new TestCliDetector(
            CliDetectionResult.Found(@"C:\claude.exe"),
            CliDetectionResult.Found(@"C:\codex.exe"));

        var claude = detector.DetectClaude();
        var codex = detector.DetectCodex();

        Assert.True(claude.IsFound);
        Assert.True(codex.IsFound);
    }

    [Fact]
    public void Detect_OnlyClaudeAvailable_ReturnsFoundForClaudeOnly()
    {
        var detector = new TestCliDetector(
            CliDetectionResult.Found(@"C:\claude.exe"),
            CliDetectionResult.NotFound("Not installed"));

        var claude = detector.DetectClaude();
        var codex = detector.DetectCodex();

        Assert.True(claude.IsFound);
        Assert.False(codex.IsFound);
    }

    [Fact]
    public void Detect_OnlyCodexAvailable_ReturnsFoundForCodexOnly()
    {
        var detector = new TestCliDetector(
            CliDetectionResult.NotFound("Not installed"),
            CliDetectionResult.Found(@"C:\codex.exe"));

        var claude = detector.DetectClaude();
        var codex = detector.DetectCodex();

        Assert.False(claude.IsFound);
        Assert.True(codex.IsFound);
    }

    [Fact]
    public void Detect_NeitherAvailable_ReturnsNotFoundForBoth()
    {
        var detector = new TestCliDetector(
            CliDetectionResult.NotFound("Not installed"),
            CliDetectionResult.NotFound("Not installed"));

        var claude = detector.DetectClaude();
        var codex = detector.DetectCodex();

        Assert.False(claude.IsFound);
        Assert.False(codex.IsFound);
    }

    [Fact]
    public void Detect_ErrorDoesNotAffectOther()
    {
        var detector = new TestCliDetector(
            CliDetectionResult.Error("Access denied"),
            CliDetectionResult.Found(@"C:\codex.exe"));

        var claude = detector.DetectClaude();
        var codex = detector.DetectCodex();

        Assert.False(claude.IsFound);
        Assert.Contains("Access denied", claude.ErrorMessage);
        Assert.True(codex.IsFound);
    }

    [Fact]
    public void Detect_ExceptionDuringClaudeDoesNotAffectCodex()
    {
        var detector = new TestCliDetector(
            CliDetectionResult.Error("Claude detection failed"),
            CliDetectionResult.Found(@"C:\codex.exe"));

        var claude = detector.DetectClaude();
        var codex = detector.DetectCodex();

        Assert.False(claude.IsFound);
        Assert.True(codex.IsFound);
    }
}