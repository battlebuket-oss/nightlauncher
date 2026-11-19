using Microsoft.Win32;
using System.IO;
using System.Globalization;
using System.Security;
using System.Text;

namespace NightLauncher.Services;

public sealed record DiscoveredSteamGame(string AppId, string Name, string InstallDirectory);

/// <summary>
/// Reads Steam's local library metadata to identify installed games. It never reads account
/// credentials or interprets ownership/licence state; Steam remains responsible for launch checks.
/// </summary>
public sealed class SteamLibraryDiscoveryService
{
    private const int MaximumLibraries = 32;
    private const int MaximumManifestsPerLibrary = 512;
    private const int MaximumManifestCandidatesScanned = 2048;
    private const int MaximumVdfFilesRead = 2048;
    private const int MaximumDiscoveredGames = 1000;
    private const long MaximumVdfBytes = 1 * 1024 * 1024;
    private const long MaximumTotalVdfBytes = 32 * 1024 * 1024;

    public IReadOnlyList<DiscoveredSteamGame> DiscoverInstalledGames() =>
        DiscoverInstalledGames(GetSteamInstallRoots());

    public IReadOnlyList<DiscoveredSteamGame> DiscoverInstalledGames(IEnumerable<string> steamInstallRoots)
    {
        ArgumentNullException.ThrowIfNull(steamInstallRoots);

        var budget = new ScanBudget();
        var libraries = ResolveSteamLibraries(steamInstallRoots, budget);
        var discovered = new Dictionary<string, DiscoveredSteamGame>(StringComparer.Ordinal);

        foreach (var library in libraries)
        {
            var steamAppsDirectory = Path.Combine(library, "steamapps");
            IEnumerable<string> manifests;

            try
            {
                manifests = Directory.EnumerateFiles(steamAppsDirectory, "appmanifest_*.acf", SearchOption.TopDirectoryOnly)
                    .Take(MaximumManifestsPerLibrary)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            var commonDirectory = Path.GetFullPath(Path.Combine(steamAppsDirectory, "common"));
            var commonAttributes = TryGetAttributes(commonDirectory);
            if (commonAttributes is null ||
                (commonAttributes.Value & FileAttributes.Directory) == 0 ||
                (commonAttributes.Value & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (var manifestPath in manifests)
            {
                if (!budget.TryConsumeManifestCandidate())
                {
                    return discovered.Values
                        .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }

                if (discovered.Count >= MaximumDiscoveredGames)
                {
                    return discovered.Values
                        .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }

                var attributes = TryGetAttributes(manifestPath);
                if (attributes is null || (attributes.Value & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var manifest = TryReadVdf(manifestPath, budget);
                if (budget.IsExhausted)
                {
                    return discovered.Values
                        .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }

                if (manifest is null)
                {
                    continue;
                }

                var appState = GetObject(manifest, "AppState") ?? manifest;
                var appIdText = GetString(appState, "appid");
                var name = CleanDisplayName(GetString(appState, "name"));
                var installDirectoryName = GetString(appState, "installdir");

                if (!uint.TryParse(appIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) ||
                    appId == 0 ||
                    string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(installDirectoryName) ||
                    Path.IsPathFullyQualified(installDirectoryName) ||
                    installDirectoryName.Contains(Path.DirectorySeparatorChar) ||
                    installDirectoryName.Contains(Path.AltDirectorySeparatorChar) ||
                    installDirectoryName is "." or "..")
                {
                    continue;
                }

                var manifestFileName = Path.GetFileNameWithoutExtension(manifestPath);
                const string manifestPrefix = "appmanifest_";
                if (!manifestFileName.StartsWith(manifestPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(manifestFileName[manifestPrefix.Length..], appId.ToString(CultureInfo.InvariantCulture),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    var installDirectory = Path.GetFullPath(Path.Combine(commonDirectory, installDirectoryName));
                    if (!IsChildPath(installDirectory, commonDirectory) || !Directory.Exists(installDirectory))
                    {
                        continue;
                    }

                    var installAttributes = TryGetAttributes(installDirectory);
                    if (installAttributes is null ||
                        (installAttributes.Value & FileAttributes.Directory) == 0 ||
                        (installAttributes.Value & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var key = appId.ToString(CultureInfo.InvariantCulture);
                    discovered.TryAdd(key, new DiscoveredSteamGame(key, name, installDirectory));
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or
                                                   UnauthorizedAccessException or NotSupportedException)
                {
                    // A malformed manifest or unavailable game folder must not stop other libraries.
                }
            }
        }

        return discovered.Values
            .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveSteamLibraries(IEnumerable<string> steamInstallRoots, ScanBudget budget)
    {
        var pending = new Queue<string>(steamInstallRoots.Where(path => !string.IsNullOrWhiteSpace(path)));
        var libraries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0 && libraries.Count < MaximumLibraries && !budget.IsExhausted)
        {
            var candidate = pending.Dequeue();
            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(candidate.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                continue;
            }

            if (!seen.Add(fullPath) || !Directory.Exists(fullPath))
            {
                continue;
            }

            var rootAttributes = TryGetAttributes(fullPath);
            var steamAppsDirectory = Path.Combine(fullPath, "steamapps");
            var steamAppsAttributes = TryGetAttributes(steamAppsDirectory);
            if (rootAttributes is null || steamAppsAttributes is null ||
                (rootAttributes.Value & FileAttributes.ReparsePoint) != 0 ||
                (steamAppsAttributes.Value & FileAttributes.ReparsePoint) != 0 ||
                (steamAppsAttributes.Value & FileAttributes.Directory) == 0)
            {
                continue;
            }

            libraries.Add(fullPath);

            var libraryFoldersPath = Path.Combine(steamAppsDirectory, "libraryfolders.vdf");
            var libraryFolders = TryReadVdf(libraryFoldersPath, budget);
            if (libraryFolders is null)
            {
                continue;
            }

            foreach (var path in FindStringValues(libraryFolders, "path"))
            {
                if (libraries.Count + pending.Count >= MaximumLibraries)
                {
                    break;
                }

                pending.Enqueue(path);
            }
        }

        return libraries;
    }

    private static IEnumerable<string> GetSteamInstallRoots()
    {
        var roots = new List<string>();

        AddRegistrySteamPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                AddRegistrySteamPath(roots, localMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
                AddRegistrySteamPath(roots, localMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               SecurityException or PlatformNotSupportedException)
            {
                // Registry discovery is best-effort. The caller can still supply an explicit root.
            }
        }

        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                 })
        {
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                roots.Add(Path.Combine(programFiles, "Steam"));
            }
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumLibraries).ToArray();
    }

    private static void AddRegistrySteamPath(List<string> roots, RegistryKey hive, string subKeyPath, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKeyPath, writable: false);
            var path = key?.GetValue(valueName) as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                roots.Add(path);
            }

            if (string.Equals(valueName, "SteamPath", StringComparison.OrdinalIgnoreCase))
            {
                var steamExe = key?.GetValue("SteamExe") as string;
                var executableDirectory = string.IsNullOrWhiteSpace(steamExe) ? null : Path.GetDirectoryName(steamExe);
                if (!string.IsNullOrWhiteSpace(executableDirectory))
                {
                    roots.Add(executableDirectory);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           SecurityException or ArgumentException)
        {
            // Missing or restricted registry keys are normal on portable/custom Steam installs.
        }
    }

    private static Dictionary<string, object?>? TryReadVdf(string path, ScanBudget budget)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            if (!budget.TryConsumeFile(info.Length) || info.Length > MaximumVdfBytes)
            {
                return null;
            }

            var text = File.ReadAllText(path, Encoding.UTF8);
            return new VdfParser(text).Parse();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           SecurityException or InvalidDataException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static FileAttributes? TryGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           SecurityException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?>? GetObject(Dictionary<string, object?> source, string key) =>
        source.TryGetValue(key, out var value) ? value as Dictionary<string, object?> : null;

    private static string? GetString(Dictionary<string, object?> source, string key) =>
        source.TryGetValue(key, out var value) ? value as string : null;

    private static string? CleanDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return cleaned.Length is > 0 and <= 120 ? cleaned : null;
    }

    private static bool IsChildPath(string candidate, string parent)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var normalizedCandidate = Path.GetFullPath(candidate);
        var prefix = normalizedParent + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindStringValues(Dictionary<string, object?> values, string key)
    {
        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                pair.Value is string value &&
                !string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }

            if (pair.Value is Dictionary<string, object?> nested)
            {
                foreach (var nestedValue in FindStringValues(nested, key))
                {
                    yield return nestedValue;
                }
            }
        }
    }

    private sealed class ScanBudget
    {
        private int _manifestCandidatesScanned;
        private int _vdfFilesRead;
        private long _vdfBytesRead;

        public bool IsExhausted { get; private set; }

        public bool TryConsumeManifestCandidate()
        {
            if (_manifestCandidatesScanned >= MaximumManifestCandidatesScanned)
            {
                IsExhausted = true;
                return false;
            }

            _manifestCandidatesScanned++;
            return true;
        }

        public bool TryConsumeFile(long fileLength)
        {
            if (_vdfFilesRead >= MaximumVdfFilesRead ||
                fileLength < 0 ||
                fileLength > MaximumTotalVdfBytes - _vdfBytesRead)
            {
                IsExhausted = true;
                return false;
            }

            _vdfFilesRead++;
            _vdfBytesRead += fileLength;
            return true;
        }
    }

    private sealed class VdfParser
    {
        private const int MaximumDepth = 32;
        private const int MaximumTokens = 100_000;
        private const int MaximumTokenLength = 16_384;

        private readonly string _text;
        private int _position;
        private int _tokenCount;

        public VdfParser(string text) => _text = text;

        public Dictionary<string, object?> Parse()
        {
            var result = ParseObject(0, expectClosingBrace: false);
            if (ReadToken() is not null)
            {
                throw new InvalidDataException("The Steam metadata file contains unexpected trailing data.");
            }

            return result;
        }

        private Dictionary<string, object?> ParseObject(int depth, bool expectClosingBrace)
        {
            if (depth > MaximumDepth)
            {
                throw new InvalidDataException("The Steam metadata nesting limit was exceeded.");
            }

            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var key = ReadToken();
                if (key is null)
                {
                    if (expectClosingBrace)
                    {
                        throw new InvalidDataException("The Steam metadata file has an unclosed object.");
                    }

                    return result;
                }

                if (key == "}")
                {
                    if (!expectClosingBrace)
                    {
                        throw new InvalidDataException("The Steam metadata file contains an unexpected closing brace.");
                    }

                    return result;
                }

                if (key == "{")
                {
                    throw new InvalidDataException("The Steam metadata file contains an unexpected opening brace.");
                }

                var value = ReadToken()
                    ?? throw new InvalidDataException("The Steam metadata file ends before a value.");

                if (value == "{")
                {
                    result[key] = ParseObject(depth + 1, expectClosingBrace: true);
                }
                else if (value == "}")
                {
                    throw new InvalidDataException("The Steam metadata file contains a key without a value.");
                }
                else
                {
                    result[key] = value;
                }
            }
        }

        private string? ReadToken()
        {
            SkipWhitespaceAndComments();
            if (_position >= _text.Length)
            {
                return null;
            }

            if (++_tokenCount > MaximumTokens)
            {
                throw new InvalidDataException("The Steam metadata token limit was exceeded.");
            }

            var current = _text[_position++];
            if (current is '{' or '}')
            {
                return current.ToString();
            }

            if (current == '"')
            {
                var builder = new StringBuilder();
                while (_position < _text.Length)
                {
                    current = _text[_position++];
                    if (current == '"')
                    {
                        return builder.ToString();
                    }

                    if (current == '\\' && _position < _text.Length)
                    {
                        var escaped = _text[_position++];
                        if (escaped is '\\' or '"')
                        {
                            builder.Append(escaped);
                        }
                        else
                        {
                            builder.Append('\\').Append(escaped);
                        }
                    }
                    else
                    {
                        builder.Append(current);
                    }

                    if (builder.Length > MaximumTokenLength)
                    {
                        throw new InvalidDataException("A Steam metadata value is too long.");
                    }
                }

                throw new InvalidDataException("The Steam metadata file contains an unterminated quoted value.");
            }

            var start = _position - 1;
            while (_position < _text.Length &&
                   !char.IsWhiteSpace(_text[_position]) &&
                   _text[_position] is not '{' and not '}')
            {
                _position++;
            }

            var token = _text[start.._position];
            if (token.Length > MaximumTokenLength)
            {
                throw new InvalidDataException("A Steam metadata value is too long.");
            }

            return token;
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_position]))
                {
                    _position++;
                    continue;
                }

                if (_position + 1 < _text.Length &&
                    _text[_position] == '/' &&
                    _text[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < _text.Length && _text[_position] is not '\r' and not '\n')
                    {
                        _position++;
                    }

                    continue;
                }

                break;
            }
        }
    }
}
