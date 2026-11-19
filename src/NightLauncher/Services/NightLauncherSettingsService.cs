using System.IO;
using System.Text.Json;

namespace NightLauncher.Services;

public sealed class NightLauncherSettingsService
{
    private const long MaximumSettingsBytes = 64 * 1024;
    private static readonly string[] SupportedSortModes = ["Name", "RecentlyPlayed", "RecentlyAdded"];

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
        var settings = ReadSettingsDocument();
        if (string.IsNullOrWhiteSpace(settings?.BackupDirectory))
        {
            return null;
        }

        if (settings.BackupDirectory.Length > 32_000 || !Path.IsPathFullyQualified(settings.BackupDirectory))
        {
            throw new InvalidDataException("The configured backup directory must be an absolute path within the supported path length.");
        }

        try
        {
            return Path.GetFullPath(settings.BackupDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException("The configured backup directory path is invalid.", exception);
        }
    }

    public string LoadPreferredSortMode()
    {
        var configured = ReadSettingsDocument()?.PreferredSortMode;
        return SupportedSortModes.FirstOrDefault(mode =>
            string.Equals(mode, configured, StringComparison.OrdinalIgnoreCase)) ?? "Name";
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

        var preferredSortMode = LoadPreferredSortMode();
        WriteSettingsDocument(new SettingsDocument
        {
            BackupDirectory = normalized,
            PreferredSortMode = preferredSortMode
        });
    }

    public void SavePreferredSortMode(string? preferredSortMode)
    {
        var normalized = SupportedSortModes.FirstOrDefault(mode =>
            string.Equals(mode, preferredSortMode, StringComparison.OrdinalIgnoreCase));
        if (normalized is null)
        {
            throw new ArgumentException("Choose a supported library sort mode.", nameof(preferredSortMode));
        }

        var backupDirectory = LoadBackupDirectory();
        WriteSettingsDocument(new SettingsDocument
        {
            BackupDirectory = backupDirectory,
            PreferredSortMode = normalized
        });
    }

    private SettingsDocument? ReadSettingsDocument()
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
        return JsonSerializer.Deserialize<SettingsDocument>(stream, SerializerOptions);
    }

    private void WriteSettingsDocument(SettingsDocument settings)
    {
        var parent = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The settings file has no parent directory.");
        Directory.CreateDirectory(parent);

        var temporary = SettingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, SerializerOptions);
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

        public string? PreferredSortMode { get; set; } = "Name";
    }
}
