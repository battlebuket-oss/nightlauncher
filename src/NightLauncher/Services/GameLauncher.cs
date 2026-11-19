using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using NightLauncher.Models;

namespace NightLauncher.Services;

public sealed record LaunchResult(bool Succeeded, string Message);

public sealed class GameLauncher
{
    public LaunchResult Launch(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (string.IsNullOrWhiteSpace(game.ExecutablePath) ||
            !Path.IsPathFullyQualified(game.ExecutablePath) ||
            !string.Equals(Path.GetExtension(game.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchResult(false, "Choose a valid Windows executable in the game settings.");
        }

        if (!File.Exists(game.ExecutablePath))
        {
            return new LaunchResult(false, "The selected executable could not be found. Edit the game entry and choose its current location.");
        }

        var workingDirectory = string.IsNullOrWhiteSpace(game.WorkingDirectory)
            ? Path.GetDirectoryName(game.ExecutablePath)!
            : game.WorkingDirectory;

        if (!Directory.Exists(workingDirectory))
        {
            return new LaunchResult(false, "The selected working directory does not exist. Edit the game entry to update it.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = game.ExecutablePath,
                WorkingDirectory = workingDirectory,
                Arguments = game.LaunchArguments ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            using var process = Process.Start(startInfo);
            return process is null
                ? new LaunchResult(false, "Windows did not return a process handle.")
                : new LaunchResult(true, $"Started {game.Name}.");
        }
        catch (Win32Exception exception)
        {
            return new LaunchResult(false, $"Windows could not start the game: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return new LaunchResult(false, $"The game could not be started: {exception.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            return new LaunchResult(false, "Windows denied permission to start this executable.");
        }
    }
}
