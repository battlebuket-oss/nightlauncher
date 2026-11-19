using System.IO;
using NightLauncher.Models;

namespace NightLauncher.Services;

public sealed class GameLibraryBackupService
{
    private readonly GameLibraryService _library;
    private readonly int _maximumBackups;

    public GameLibraryBackupService(
        GameLibraryService library,
        string? backupDirectory = null,
        int maximumBackups = 20)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (maximumBackups < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBackups), "At least one backup must be retained.");
        }

        _library = library;
        BackupDirectory = Path.GetFullPath(backupDirectory ??
            Path.Combine(Path.GetDirectoryName(library.LibraryPath)!, "backups"));
        _maximumBackups = maximumBackups;
    }

    public string BackupDirectory { get; }

    public IReadOnlyList<string> GetBackups()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(BackupDirectory, "NightLauncher-library-*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string CreateBackup()
    {
        if (!File.Exists(_library.LibraryPath))
        {
            throw new FileNotFoundException("There is no NightLauncher library to back up.", _library.LibraryPath);
        }

        // Validate the current file before preserving it as a known-good backup.
        _ = _library.Load();
        Directory.CreateDirectory(BackupDirectory);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var destination = Path.Combine(BackupDirectory, $"NightLauncher-library-{timestamp}-{Guid.NewGuid():N}.json");
        var temporary = destination + ".tmp";
        var temporaryCreated = false;

        try
        {
            File.Copy(_library.LibraryPath, temporary, overwrite: false);
            temporaryCreated = true;
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (temporaryCreated && File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                    // A stale temporary file is harmless and can be removed manually.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the original backup error.
                }
            }
        }

        PruneOldBackups();
        return destination;
    }

    public string RestoreLatestBackup()
    {
        var latest = GetBackups().FirstOrDefault()
            ?? throw new FileNotFoundException("No NightLauncher backups were found.", BackupDirectory);

        RestoreBackup(latest);
        return latest;
    }

    public void RestoreBackup(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        var fullPath = Path.GetFullPath(backupPath);
        var backupRoot = BackupDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only backup files inside the NightLauncher backup folder can be restored.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The selected backup file could not be found.", fullPath);
        }

        // Preserve the current library before attempting restoration. Import validates the backup
        // and marks every restored launch target for a one-time trust confirmation.
        if (File.Exists(_library.LibraryPath))
        {
            _ = CreateBackup();
        }

        var restoredGames = _library.Import(fullPath);
        _library.Save(restoredGames);
    }

    private void PruneOldBackups()
    {
        var backups = GetBackups();
        foreach (var obsolete in backups.Skip(_maximumBackups))
        {
            try
            {
                File.Delete(obsolete);
            }
            catch (IOException)
            {
                // Retention is best-effort; creation of the new backup has already succeeded.
            }
            catch (UnauthorizedAccessException)
            {
                // Retention is best-effort; creation of the new backup has already succeeded.
            }
        }
    }
}
