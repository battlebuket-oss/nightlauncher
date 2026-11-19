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

        if (string.Equals(game.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchThroughSteam(game);
        }

        if (!string.Equals(game.LaunchMode, "Executable", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchResult(false, "This game uses an unsupported launch mode. Edit the game entry and choose a supported launch target.");
        }

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

    public static Uri CreateSteamLaunchUri(string steamAppId)
    {
        if (!uint.TryParse(steamAppId, out var appId) || appId == 0)
        {
            throw new ArgumentException("A valid numeric Steam App ID is required.", nameof(steamAppId));
        }

        return new Uri($"steam://rungameid/{appId}", UriKind.Absolute);
    }

    private static LaunchResult LaunchThroughSteam(GameEntry game)
    {
        try
        {
            var launchUri = CreateSteamLaunchUri(game.SteamAppId);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = launchUri.AbsoluteUri,
                UseShellExecute = true
            });

            return process is null
                ? new LaunchResult(false, "Windows could not hand the request to the official Steam client. Check that Steam is installed and its protocol is registered.")
                : new LaunchResult(true, $"Sent {game.Name} to the official Steam client. Steam will perform its normal sign-in and ownership checks.");
        }
        catch (ArgumentException)
        {
            return new LaunchResult(false, "This game has an invalid Steam App ID. Edit the game entry and enter a numeric App ID.");
        }
        catch (Win32Exception exception)
        {
            return new LaunchResult(false, $"Windows could not open the Steam client: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return new LaunchResult(false, $"The Steam launch request could not be opened: {exception.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            return new LaunchResult(false, "Windows denied permission to open the Steam launch request.");
        }
    }
}
