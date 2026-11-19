using System.IO;
using System.Text.Json;
using NightLauncher.Models;

namespace NightLauncher.Services;

public sealed class GameLibraryService
{
    private const int MaximumGames = 1000;
    private const long MaximumJsonBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public GameLibraryService(string? storageDirectory = null)
    {
        var directory = storageDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NightLauncher");

        LibraryPath = Path.Combine(directory, "library.json");
    }

    public string LibraryPath { get; }

    public IReadOnlyList<GameEntry> Load()
    {
        if (!File.Exists(LibraryPath))
        {
            return Array.Empty<GameEntry>();
        }

        try
        {
            var games = ReadGames(LibraryPath);
            ValidateCollection(games);
            return games;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            var quarantinePath = Path.Combine(
                Path.GetDirectoryName(LibraryPath)!,
                $"library.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");

            try
            {
                File.Move(LibraryPath, quarantinePath, overwrite: false);
            }
            catch
            {
                // Keep the original file untouched if it cannot be quarantined.
            }

            throw new InvalidDataException(
                $"The library file could not be read safely. A copy was preserved at: {quarantinePath}",
                exception);
        }
    }

    public void Save(IEnumerable<GameEntry> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        var list = games.Select(game => game.Copy()).ToList();
        ValidateCollection(list);
        WriteGames(LibraryPath, list);
    }

    public IReadOnlyList<GameEntry> Import(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var imported = ReadGames(sourcePath);
        ValidateCollection(imported, assignMissingIds: true);
        // Imported libraries are untrusted input. Require a one-time path review before launching each imported entry.
        foreach (var game in imported)
        {
            game.NeedsLaunchConfirmation = true;
        }

        return imported;
    }

    public void Export(string destinationPath, IEnumerable<GameEntry> games)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(games);
        var list = games.Select(game => game.Copy()).ToList();
        ValidateCollection(list);
        WriteGames(destinationPath, list);
    }

    private static List<GameEntry> ReadGames(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The selected library file does not exist.", path);
        }

        if (info.Length > MaximumJsonBytes)
        {
            throw new InvalidDataException("The selected library file is larger than the 10 MB safety limit.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<List<GameEntry>>(stream, SerializerOptions) ?? [];
    }

    private static void WriteGames(string destinationPath, IReadOnlyCollection<GameEntry> games)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The library destination has no parent directory.");

        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + ".tmp";

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, games, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // A stale temp file is safer than masking the original save error.
                }
            }
        }
    }

    private static void ValidateCollection(List<GameEntry> games, bool assignMissingIds = false)
    {
        if (games.Count > MaximumGames)
        {
            throw new InvalidDataException($"A library can contain at most {MaximumGames} games.");
        }

        var ids = new HashSet<Guid>();
        foreach (var game in games)
        {
            if (game is null)
            {
                throw new InvalidDataException("The library contains an empty game entry.");
            }

            if (assignMissingIds)
            {
                if (game.Id == Guid.Empty || !ids.Add(game.Id))
                {
                    do
                    {
                        game.Id = Guid.NewGuid();
                    }
                    while (!ids.Add(game.Id));
                }
            }
            else if (game.Id == Guid.Empty || !ids.Add(game.Id))
            {
                throw new InvalidDataException("The library contains a missing or duplicate game identifier.");
            }

            game.LaunchMode = string.IsNullOrWhiteSpace(game.LaunchMode) ? "Executable" : game.LaunchMode.Trim();
            game.SteamAppId ??= string.Empty;
            game.SteamInstallDirectory ??= string.Empty;
            game.LaunchArguments ??= string.Empty;
            game.WorkingDirectory ??= string.Empty;
            game.Category ??= "PC Game";

            if (string.IsNullOrWhiteSpace(game.Name) || game.Name.Length > 120)
            {
                throw new InvalidDataException("Every game must have a name between 1 and 120 characters.");
            }

            if (string.Equals(game.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                game.LaunchMode = "Steam";
                game.SteamAppId = game.SteamAppId.Trim();
                if (!uint.TryParse(game.SteamAppId, out var appId) || appId == 0)
                {
                    throw new InvalidDataException($"'{game.Name}' must have a valid numeric Steam App ID.");
                }

                if (!string.IsNullOrWhiteSpace(game.SteamInstallDirectory) &&
                    !Path.IsPathFullyQualified(game.SteamInstallDirectory))
                {
                    throw new InvalidDataException($"'{game.Name}' has a Steam install directory that is not an absolute path.");
                }

                // Steam handles its own launch arguments and entitlement checks.
                game.ExecutablePath = string.Empty;
                game.WorkingDirectory = string.Empty;
                game.LaunchArguments = string.Empty;
            }
            else if (string.Equals(game.LaunchMode, "Executable", StringComparison.OrdinalIgnoreCase))
            {
                game.LaunchMode = "Executable";
                game.SteamInstallDirectory = string.Empty;
                if (string.IsNullOrWhiteSpace(game.ExecutablePath) ||
                    !Path.IsPathFullyQualified(game.ExecutablePath) ||
                    !string.Equals(Path.GetExtension(game.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"'{game.Name}' must have an absolute path to a Windows .exe file.");
                }
            }
            else
            {
                throw new InvalidDataException($"'{game.Name}' uses an unsupported launch mode.");
            }

            if (game.LaunchArguments.Length > 4096 || game.Category.Length > 80 ||
                game.WorkingDirectory.Length > 32768 || game.SteamInstallDirectory.Length > 32768)
            {
                throw new InvalidDataException($"'{game.Name}' contains a field that exceeds the allowed length.");
            }

            if (!string.IsNullOrWhiteSpace(game.WorkingDirectory) && !Path.IsPathFullyQualified(game.WorkingDirectory))
            {
                throw new InvalidDataException($"'{game.Name}' has a working directory that is not an absolute path.");
            }

            game.Category = string.IsNullOrWhiteSpace(game.Category) ? "PC Game" : game.Category.Trim();
            game.Name = game.Name.Trim();
        }
    }
}
