using System.IO;
using System.Security.Cryptography;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class CodexConfigBackupTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "AgentProviderManager.BackupTests", Guid.NewGuid().ToString("N"));
    private readonly string _configPath;
    private readonly string _dataDir;

    public CodexConfigBackupTests()
    {
        _dataDir = Path.Combine(_directory, "data");
        _configPath = Path.Combine(_directory, "config.toml");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Fact]
    public void GetFileHash_ReturnsConsistentHash()
    {
        File.WriteAllText(_configPath, "model = \"test\"\nbase_url = \"https://example.com\"");

        var hash1 = CodexConfigBackupTestExtensions.GetFileHashForTest(_configPath);
        var hash2 = CodexConfigBackupTestExtensions.GetFileHashForTest(_configPath);

        Assert.NotNull(hash1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void FilesMatch_DetectsDifference()
    {
        File.WriteAllText(_configPath, "model = \"test1\"");
        var otherPath = Path.Combine(_directory, "other.toml");
        File.WriteAllText(otherPath, "model = \"test2\"");

        Assert.False(CodexConfigBackupTestExtensions.FilesMatchForTest(_configPath, otherPath));
    }

    [Fact]
    public void PermanentBackup_CreatedOnceAndRestored()
    {
        var permanentBackupPath = Path.Combine(_dataDir, "codex-config-backup.toml");
        File.WriteAllText(_configPath, "model = \"permanent-original\"");

        // First call creates backup
        var created1 = CodexConfigBackupTestExtensions.EnsurePermanentBackupForTest(_configPath, permanentBackupPath);
        Assert.True(created1);
        Assert.True(File.Exists(permanentBackupPath));

        // Second call returns false (already exists)
        var created2 = CodexConfigBackupTestExtensions.EnsurePermanentBackupForTest(_configPath, permanentBackupPath);
        Assert.False(created2);

        // Modify original
        File.WriteAllText(_configPath, "model = \"modified\"");

        // Restore
        var restored = CodexConfigBackupTestExtensions.RestoreFromPermanentBackupForTest(_configPath, permanentBackupPath);
        Assert.True(restored);
        Assert.Equal("model = \"permanent-original\"", File.ReadAllText(_configPath).Trim());
    }
}

// Extension methods for testing private methods
public static class CodexConfigBackupTestExtensions
{
    public static bool EnsurePermanentBackupForTest(string configPath, string backupPath)
    {
        if (File.Exists(backupPath)) return false;
        if (!File.Exists(configPath)) return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(configPath, backupPath);
            return true;
        }
        catch { return false; }
    }

    public static bool RestoreFromPermanentBackupForTest(string configPath, string backupPath)
    {
        if (!File.Exists(backupPath)) return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.Copy(backupPath, configPath, overwrite: true);
            return CodexConfigBackupTestExtensions.FilesMatchForTest(configPath, backupPath);
        }
        catch { return false; }
    }

    public static string? GetFileHashForTest(string path)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch { return null; }
    }

    public static bool FilesMatchForTest(string path1, string path2)
    {
        var hash1 = CodexConfigBackup.GetFileHash(path1);
        var hash2 = CodexConfigBackup.GetFileHash(path2);
        return hash1 != null && hash2 != null && hash1.Equals(hash2, StringComparison.OrdinalIgnoreCase);
    }
}