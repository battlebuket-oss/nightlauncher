using System.IO;
using NightLauncher.Models;

namespace NightLauncher.Services;

public sealed class GameDiscoveryService
{
    private const int MaximumDirectoriesToVisit = 10_000;

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "redist",
        "redistributable",
        "redistributables",
        "_commonredist",
        "commonredist",
        "directx",
        "easyanticheat",
        "battleye",
        "crashreport",
        "crashreports",
        "support",
        "installers",
        "uninstall"
    };

    private static readonly HashSet<string> IgnoredExecutableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup.exe",
        "uninstall.exe",
        "unins000.exe",
        "unitycrashhandler32.exe",
        "unitycrashhandler64.exe",
        "crashreportclient.exe",
        "easyanticheat_eos_setup.exe",
        "beservice.exe"
    };

    public IReadOnlyList<DiscoveredGameExecutable> DiscoverExecutables(
        string selectedRoot,
        int maximumDepth = 4,
        int maximumResults = 500,
        int maximumDirectoriesToVisit = MaximumDirectoriesToVisit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedRoot);
        if (maximumDepth < 0 || maximumDepth > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth), "Scan depth must be between 0 and 8.");
        }

        if (maximumResults < 1 || maximumResults > 2_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults), "Result limit must be between 1 and 2000.");
        }

        if (maximumDirectoriesToVisit < 1 || maximumDirectoriesToVisit > MaximumDirectoriesToVisit)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDirectoriesToVisit),
                $"Directory visit limit must be between 1 and {MaximumDirectoriesToVisit}.");
        }

        var root = Path.GetFullPath(selectedRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The selected folder does not exist: {root}");
        }

        var candidates = new List<DiscoveredGameExecutable>();
        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string DirectoryPath, int Depth)>();
        pending.Push((root, 0));

        var visitedDirectories = 0;
        while (pending.Count > 0 &&
               visitedDirectories < maximumDirectoriesToVisit &&
               candidates.Count < maximumResults)
        {
            var (currentDirectory, depth) = pending.Pop();
            visitedDirectories++;

            try
            {
                foreach (var executablePath in Directory.EnumerateFiles(
                             currentDirectory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    if (candidates.Count >= maximumResults)
                    {
                        break;
                    }

                    var fullExecutablePath = Path.GetFullPath(executablePath);
                    var fileName = Path.GetFileName(fullExecutablePath);
                    if (IgnoredExecutableNames.Contains(fileName) || !knownPaths.Add(fullExecutablePath))
                    {
                        continue;
                    }

                    candidates.Add(new DiscoveredGameExecutable(
                        Path.GetFileNameWithoutExtension(fullExecutablePath),
                        fullExecutablePath,
                        Path.GetDirectoryName(fullExecutablePath)!));
                }
            }
            catch (IOException)
            {
                // Continue scanning other folders when a directory is temporarily unavailable.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue scanning folders the current user can read.
            }

            if (depth >= maximumDepth || candidates.Count >= maximumResults)
            {
                continue;
            }

            try
            {
                foreach (var childDirectory in Directory.EnumerateDirectories(
                             currentDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    // Bound pending work as well as completed visits. Without this check one
                    // folder containing hundreds of thousands of child directories could grow
                    // the stack far beyond the global scan budget before the next loop iteration.
                    if (visitedDirectories + pending.Count >= maximumDirectoriesToVisit)
                    {
                        break;
                    }

                    if (IgnoredDirectoryNames.Contains(Path.GetFileName(childDirectory)))
                    {
                        continue;
                    }

                    try
                    {
                        var attributes = File.GetAttributes(childDirectory);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    pending.Push((childDirectory, depth + 1));
                }
            }
            catch (IOException)
            {
                // The selected directory may contain inaccessible or removable subfolders.
            }
            catch (UnauthorizedAccessException)
            {
                // Do not elevate privileges to inspect protected directories.
            }
        }

        return candidates
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
