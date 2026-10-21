using System.IO;
using System.Security.Cryptography;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Manages the permanent backup of the user's original <c>~/.codex/config.toml</c>.
/// The backup is created once on first use and never overwritten.
/// </summary>
public static class CodexConfigBackup
{
    public const string PermanentBackupFileName = "codex-config-backup.toml";

    private static string ConfigPath => CodexConfigReader.DefaultPath;

    private static string PermanentBackupPath => Path.Combine(AppPaths.DataDirectory, PermanentBackupFileName);

    /// <summary>True when the permanent backup of the original config already exists.</summary>
    public static bool PermanentBackupExists => File.Exists(PermanentBackupPath);

    /// <summary>
    /// Creates the permanent backup exactly once. Returns true when a new backup was written,
    /// false when a backup already existed or the source file was missing.
    /// The original config.toml is never modified by this call.
    /// </summary>
    public static bool EnsurePermanentBackup()
    {
        if (PermanentBackupExists)
        {
            return false;
        }

        if (!File.Exists(ConfigPath))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PermanentBackupPath)!);
            File.Copy(ConfigPath, PermanentBackupPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Restores the original config.toml from the permanent backup.
    /// Returns false when no backup exists.
    /// </summary>
    public static bool RestoreFromPermanentBackup()
    {
        if (!PermanentBackupExists)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.Copy(PermanentBackupPath, ConfigPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Computes SHA256 hash of a file.
    /// </summary>
    public static string? GetFileHash(string path)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Compares two files byte-for-byte using SHA256.
    /// </summary>
    public static bool FilesMatch(string path1, string path2)
    {
        var hash1 = GetFileHash(path1);
        var hash2 = GetFileHash(path2);
        return hash1 != null && hash2 != null && hash1.Equals(hash2, StringComparison.OrdinalIgnoreCase);
    }
}