using System.IO;
using System.Text.Json;

namespace NightLauncher.Services;

public sealed class NightLauncherSettingsService
{
    private const long MaximumSettingsBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public NightLauncherSettingsService(string? settingsPath = null)
    {
        SettingsPath = Path.GetFullPath(settingsPath ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NightLauncher",
                "settings.json"));
    }

    public string SettingsPath { get; }

    public string? LoadBackupDirectory()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        var info = new FileInfo(SettingsPath);
        if (info.Length > MaximumSettingsBytes)
        {
            throw new InvalidDataException("NightLauncher settings exceed the 64 KB safety limit.");
        }

        using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var settings = JsonSerializer.Deserialize<SettingsDocument>(stream, SerializerOptions);
        if (string.IsNullOrWhiteSpace(settings?.BackupDirectory))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(settings.BackupDirectory))
        {
            throw new InvalidDataException("The configured backup directory must be an absolute path.");
        }

        return Path.GetFullPath(settings.BackupDirectory);
    }

    public void SaveBackupDirectory(string? backupDirectory)
    {
        string? normalized = null;
        if (!string.IsNullOrWhiteSpace(backupDirectory))
        {
            if (!Path.IsPathFullyQualified(backupDirectory))
            {
                throw new ArgumentException("The backup directory must be an absolute path.", nameof(backupDirectory));
            }

            normalized = Path.GetFullPath(backupDirectory);
            if (normalized.Length > 32_000)
            {
                throw new ArgumentException("The backup directory path is too long.", nameof(backupDirectory));
            }
        }

        var parent = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The settings file has no parent directory.");
        Directory.CreateDirectory(parent);

        var temporary = SettingsPath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new SettingsDocument(normalized), SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                    // A stale temp file is safe and can be removed manually.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the original settings-write error.
                }
            }
        }
    }

    public sealed class SettingsDocument
    {
        public SettingsDocument()
        {
        }

        public string? BackupDirectory { get; set; }
    }
}
