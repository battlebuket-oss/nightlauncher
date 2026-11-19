using System.IO;
using System.IO.Compression;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using NightLauncher.Models;
using NightLauncher.Services;

internal static class Program
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"SMOKE TEST FAILED: {message}");
        }
    }

    private static bool HasGeneratedField(Type windowType, string fieldName) =>
        windowType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) is not null;

    [STAThread]
    public static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "NightLauncher-SmokeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var storage = Path.Combine(root, "data");
            var service = new GameLibraryService(storage);
            var game = new GameEntry
            {
                Name = "Example Game",
                ExecutablePath = Path.Combine(root, "ExampleGame.exe"),
                WorkingDirectory = root,
                Category = "Test",
                IsFavorite = true
            };

            var steamGame = new GameEntry
            {
                Name = "Example Steam Game",
                LaunchMode = "Steam",
                SteamAppId = "271590",
                SteamInstallDirectory = Path.Combine(root, "Steam", "steamapps", "common", "Example Steam Game"),
                Category = "Steam"
            };

            service.Save([game, steamGame]);
            var loaded = service.Load();
            Assert(loaded.Count == 2, "library save/load should preserve both executable and Steam entries");
            Assert(loaded[0].Name == game.Name, "library save/load should preserve the game name");
            Assert(loaded[0].ExecutablePath == game.ExecutablePath, "library save/load should preserve the executable path");
            Assert(loaded[0].IsFavorite, "library save/load should preserve the favorite flag");
            Assert(loaded[0].FavoriteIndicator == "★", "favorite entries should expose the star indicator for the library view");
            Assert(loaded[0].Copy().IsFavorite, "game entry copies should preserve the favorite flag");

            var favoriteNotifications = new List<string?>();
            game.PropertyChanged += (_, args) => favoriteNotifications.Add(args.PropertyName);
            game.IsFavorite = false;
            game.IsFavorite = true;
            Assert(favoriteNotifications.Contains(nameof(GameEntry.IsFavorite)) &&
                   favoriteNotifications.Contains(nameof(GameEntry.FavoriteIndicator)),
                "favorite changes should notify WPF bindings for the favorite flag and star indicator");
            Assert(loaded[1].LaunchMode == "Steam", "library save/load should preserve the Steam launch mode");
            Assert(loaded[1].SteamAppId == "271590", "library save/load should preserve the Steam App ID");
            Assert(loaded[1].SteamInstallDirectory == steamGame.SteamInstallDirectory,
                "library save/load should preserve the discovered Steam install directory");
            Assert(steamGame.Copy().SteamInstallDirectory == steamGame.SteamInstallDirectory,
                "game entry copies should preserve the Steam install directory");

            var exportPath = Path.Combine(root, "backup.json");
            service.Export(exportPath, loaded);
            var imported = service.Import(exportPath);
            Assert(imported.Count == 2, "JSON export/import should preserve both launch modes");
            Assert(imported.All(item => item.NeedsLaunchConfirmation), "imported entries should require a one-time review");
            Assert(imported[0].IsFavorite, "JSON export/import should preserve favorite flags");

            var sortableGames = new ObservableCollection<GameEntry>
            {
                new() { Name = "Zulu non-favorite" },
                new() { Name = "Zulu favorite", IsFavorite = true },
                new() { Name = "Alpha favorite", IsFavorite = true }
            };
            var sortedGames = new ListCollectionView(sortableGames);
            sortedGames.SortDescriptions.Add(new SortDescription(nameof(GameEntry.IsFavorite), ListSortDirection.Descending));
            sortedGames.SortDescriptions.Add(new SortDescription(nameof(GameEntry.Name), ListSortDirection.Ascending));
            Assert(sortedGames.Cast<GameEntry>().Select(item => item.Name).SequenceEqual(
                    ["Alpha favorite", "Zulu favorite", "Zulu non-favorite"]),
                "library view should pin favorites first and sort each group alphabetically");
            Assert(imported[1].LaunchMode == "Steam" && imported[1].SteamAppId == "271590",
                "JSON export/import should preserve Steam launch metadata");
            Assert(imported[1].SteamInstallDirectory == steamGame.SteamInstallDirectory,
                "JSON export/import should preserve the Steam install directory");

            var steamUri = GameLauncher.CreateSteamLaunchUri("271590");
            Assert(steamUri.Scheme == "steam", "Steam launch URI should use the official steam protocol");
            Assert(steamUri.AbsoluteUri == "steam://rungameid/271590", "Steam launch URI should target the requested App ID");

            var invalidSteamIdRejected = false;
            try
            {
                service.Save([new GameEntry
                {
                    Name = "Invalid Steam Entry",
                    LaunchMode = "Steam",
                    SteamAppId = "../not-an-app-id"
                }]);
            }
            catch (InvalidDataException)
            {
                invalidSteamIdRejected = true;
            }

            Assert(invalidSteamIdRejected, "library validation must reject malformed Steam App IDs");

            var invalidSteamInstallDirectoryRejected = false;
            try
            {
                service.Save([new GameEntry
                {
                    Name = "Invalid Steam install path",
                    LaunchMode = "Steam",
                    SteamAppId = "271590",
                    SteamInstallDirectory = "relative\\steam\\game"
                }]);
            }
            catch (InvalidDataException)
            {
                invalidSteamInstallDirectoryRejected = true;
            }

            Assert(invalidSteamInstallDirectoryRejected,
                "library validation must reject relative Steam install directories");

            var backupDirectory = Path.Combine(root, "backups");
            var backupService = new GameLibraryBackupService(service, backupDirectory, maximumBackups: 3);
            var backupPath = backupService.CreateBackup();
            Assert(File.Exists(backupPath), "backup creation should produce a file");
            Assert(backupService.GetBackups().Count == 1, "backup enumeration should find the new backup");

            service.Save([new GameEntry
            {
                Name = "Changed Library",
                ExecutablePath = Path.Combine(root, "ChangedGame.exe"),
                WorkingDirectory = root
            }]);

            backupService.RestoreBackup(backupPath);
            var restored = service.Load();
            Assert(restored.Count == 2, "restore should recover the backed-up game entries");
            Assert(restored[0].IsFavorite, "backup restore should preserve favorite flags");
            Assert(restored.Any(item => item.Name == "Example Steam Game"), "restore should recover the Steam entry");
            Assert(restored.All(item => item.NeedsLaunchConfirmation), "restored entries should require a one-time launch review");
            Assert(backupService.GetBackups().Count == 2, "restore should preserve the pre-restore library as a safety backup");

            var outsideRestoreRejected = false;
            try
            {
                backupService.RestoreBackup(exportPath);
            }
            catch (InvalidDataException)
            {
                outsideRestoreRejected = true;
            }

            Assert(outsideRestoreRejected, "restore should reject files outside the NightLauncher backup directory");

            for (var index = 0; index < 5; index++)
            {
                backupService.CreateBackup();
            }

            Assert(backupService.GetBackups().Count <= 3, "backup retention should keep no more than the configured maximum");

            var settingsDirectory = Path.Combine(root, "configuration");
            var settingsPath = Path.Combine(settingsDirectory, "settings.json");
            var settingsService = new NightLauncherSettingsService(settingsPath);
            Assert(settingsService.LoadBackupDirectory() is null, "a new settings file should use the default backup directory");
            var customBackupDirectory = Path.Combine(root, "external-backups");
            var settingsTempPath = settingsPath + ".tmp";
            File.WriteAllText(settingsTempPath, "pre-existing user file must remain untouched");
            settingsService.SaveBackupDirectory(customBackupDirectory);
            Assert(File.ReadAllText(settingsTempPath) == "pre-existing user file must remain untouched",
                "settings writes should not overwrite an existing neighboring .tmp file");
            var reloadedSettings = new NightLauncherSettingsService(settingsPath);
            Assert(string.Equals(reloadedSettings.LoadBackupDirectory(), Path.GetFullPath(customBackupDirectory), StringComparison.OrdinalIgnoreCase),
                "settings should persist and reload the custom backup directory");
            Assert(reloadedSettings.LoadPreferredSortMode() == "Name",
                "older settings documents should default to alphabetical sorting");
            var legacySettingsPath = Path.Combine(settingsDirectory, "legacy-settings.json");
            File.WriteAllText(legacySettingsPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                BackupDirectory = customBackupDirectory
            }));
            Assert(new NightLauncherSettingsService(legacySettingsPath).LoadPreferredSortMode() == "Name",
                "settings files created before sort preferences should default to Name A-Z");
            settingsService.SavePreferredSortMode("RecentlyPlayed");
            Assert(reloadedSettings.LoadPreferredSortMode() == "RecentlyPlayed",
                "settings should persist the preferred sort mode");
            settingsService.SaveBackupDirectory(customBackupDirectory);
            Assert(reloadedSettings.LoadPreferredSortMode() == "RecentlyPlayed",
                "changing the backup directory should preserve the preferred sort mode");
            settingsService.SavePreferredSortMode("RecentlyAdded");
            Assert(string.Equals(reloadedSettings.LoadBackupDirectory(), Path.GetFullPath(customBackupDirectory), StringComparison.OrdinalIgnoreCase),
                "changing the sort mode should preserve the custom backup directory");

            var invalidSortModeRejected = false;
            try
            {
                settingsService.SavePreferredSortMode("RunAnything");
            }
            catch (ArgumentException)
            {
                invalidSortModeRejected = true;
            }

            Assert(invalidSortModeRejected, "settings should reject unsupported sort modes");

            var diagnosticsDirectory = Path.Combine(root, "diagnostics-logs");
            Directory.CreateDirectory(diagnosticsDirectory);
            File.WriteAllText(Path.Combine(diagnosticsDirectory, "latest.log"), "latest error details");
            File.WriteAllText(Path.Combine(diagnosticsDirectory, "latest.1.log"), "older error details");
            File.WriteAllText(Path.Combine(diagnosticsDirectory, "private-settings.json"), "private token material that must not be exported");
            File.WriteAllText(Path.Combine(diagnosticsDirectory, "library.json"), "local game library data");
            File.WriteAllText(Path.Combine(diagnosticsDirectory, "backups.zip"), "private recovery data");
            var diagnosticsArchivePath = Path.Combine(root, "NightLauncher-Diagnostics.zip");
            var diagnosticsTempPath = diagnosticsArchivePath + ".tmp";
            File.WriteAllText(diagnosticsTempPath, "pre-existing user file must remain untouched");
            var diagnosticsResult = new DiagnosticsService(diagnosticsDirectory).Export(diagnosticsArchivePath);
            Assert(diagnosticsResult.LogFilesIncluded == 2, "diagnostics export should include only recognized log files");
            Assert(File.ReadAllText(diagnosticsTempPath) == "pre-existing user file must remain untouched",
                "diagnostics export should not overwrite an existing neighboring .tmp file");
            using (var diagnosticsArchive = ZipFile.OpenRead(diagnosticsArchivePath))
            {
                var entryNames = diagnosticsArchive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert(entryNames.SetEquals(["latest.log", "latest.1.log", "diagnostics-summary.txt"]),
                    "diagnostics ZIP should contain only approved log files and the summary");
                using var summaryReader = new StreamReader(diagnosticsArchive.GetEntry("diagnostics-summary.txt")!.Open());
                var summary = summaryReader.ReadToEnd();
                Assert(summary.Contains("NightLauncher version:", StringComparison.Ordinal) &&
                       summary.Contains("Runtime:", StringComparison.Ordinal) &&
                       summary.Contains("Log files included: 2", StringComparison.Ordinal),
                    "diagnostics ZIP should include a minimal app/runtime summary");
            }

            File.WriteAllBytes(Path.Combine(diagnosticsDirectory, "latest.2.log"), new byte[(2 * 1024 * 1024) + 1]);
            var oversizedDiagnosticsRejected = false;
            try
            {
                _ = new DiagnosticsService(diagnosticsDirectory).Export(Path.Combine(root, "oversized-diagnostics.zip"));
            }
            catch (InvalidDataException)
            {
                oversizedDiagnosticsRejected = true;
            }

            Assert(oversizedDiagnosticsRejected, "diagnostics export should reject oversized log files");
            var archiveInsideLogFolderRejected = false;
            try
            {
                _ = new DiagnosticsService(diagnosticsDirectory).Export(Path.Combine(diagnosticsDirectory, "bundle.zip"));
            }
            catch (ArgumentException)
            {
                archiveInsideLogFolderRejected = true;
            }

            Assert(archiveInsideLogFolderRejected, "diagnostics archives should not be written inside the source log folder");

            var invalidBackupDirectoryRejected = false;
            try
            {
                settingsService.SaveBackupDirectory("relative\\backup");
            }
            catch (ArgumentException)
            {
                invalidBackupDirectoryRejected = true;
            }

            Assert(invalidBackupDirectoryRejected, "settings should reject relative backup paths");

            var malformedSettingsPath = Path.Combine(settingsDirectory, "malformed-settings.json");
            File.WriteAllText(malformedSettingsPath, "{ invalid json");
            var malformedSettingsRejected = false;
            try
            {
                _ = new NightLauncherSettingsService(malformedSettingsPath).LoadBackupDirectory();
            }
            catch (System.Text.Json.JsonException)
            {
                malformedSettingsRejected = true;
            }

            Assert(malformedSettingsRejected, "settings service should reject malformed JSON");
            var malformedSortSettingsRejected = false;
            try
            {
                _ = new NightLauncherSettingsService(malformedSettingsPath).LoadPreferredSortMode();
            }
            catch (System.Text.Json.JsonException)
            {
                malformedSortSettingsRejected = true;
            }

            Assert(malformedSortSettingsRejected, "settings service should reject malformed JSON when loading sort preferences");

            var corruptLibraryDirectory = Path.Combine(root, "corrupt-library");
            Directory.CreateDirectory(corruptLibraryDirectory);
            var corruptLibraryPath = Path.Combine(corruptLibraryDirectory, "library.json");
            File.WriteAllText(corruptLibraryPath, "{ malformed library");
            var corruptLibraryRejected = false;
            try
            {
                _ = new GameLibraryService(corruptLibraryDirectory).Load();
            }
            catch (InvalidDataException)
            {
                corruptLibraryRejected = true;
            }

            Assert(corruptLibraryRejected, "library loader should reject malformed JSON");
            Assert(!File.Exists(corruptLibraryPath) &&
                   Directory.EnumerateFiles(corruptLibraryDirectory, "library.corrupt-*.json").Any(),
                "library loader should preserve malformed data in a quarantine file");

            var blockedStoragePath = Path.Combine(root, "storage-is-a-file");
            File.WriteAllText(blockedStoragePath, "not a directory");
            var storageFailureHandled = false;
            try
            {
                new GameLibraryService(blockedStoragePath).Save(Array.Empty<GameEntry>());
            }
            catch (IOException)
            {
                storageFailureHandled = true;
            }
            catch (UnauthorizedAccessException)
            {
                storageFailureHandled = true;
            }

            Assert(storageFailureHandled, "library save should surface a predictable failure when its storage directory is blocked by a file");

            var discoveryRoot = Path.Combine(root, "discovery");
            var gameFolder = Path.Combine(discoveryRoot, "Games", "ExampleTitle");
            var redistFolder = Path.Combine(discoveryRoot, "Games", "Redist");
            Directory.CreateDirectory(gameFolder);
            Directory.CreateDirectory(redistFolder);
            var discoveredExe = Path.Combine(gameFolder, "ExampleTitle.exe");
            File.WriteAllText(discoveredExe, "test fixture");
            File.WriteAllText(Path.Combine(redistFolder, "Setup.exe"), "installer fixture");

            var discovered = new GameDiscoveryService().DiscoverExecutables(discoveryRoot);
            Assert(discovered.Any(item => string.Equals(item.ExecutablePath, discoveredExe, StringComparison.OrdinalIgnoreCase)),
                "folder discovery should include a game executable inside the selected folder");
            Assert(!discovered.Any(item => item.ExecutablePath.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase)),
                "folder discovery should ignore common redistributable folders");

            var limitedDiscovery = new GameDiscoveryService().DiscoverExecutables(discoveryRoot, maximumDepth: 0);
            Assert(limitedDiscovery.Count == 0, "folder discovery should respect the configured maximum depth");

            var legacyStorage = Path.Combine(root, "legacy");
            Directory.CreateDirectory(legacyStorage);
            var legacyEntry = new
            {
                Id = Guid.NewGuid(),
                Name = "Legacy Executable Entry",
                ExecutablePath = Path.Combine(root, "LegacyGame.exe"),
                LaunchArguments = string.Empty,
                WorkingDirectory = root,
                Category = "PC Game",
                AddedUtc = DateTimeOffset.UtcNow,
                LastLaunchedUtc = (DateTimeOffset?)null,
                NeedsLaunchConfirmation = false
            };
            File.WriteAllText(
                Path.Combine(legacyStorage, "library.json"),
                System.Text.Json.JsonSerializer.Serialize(new[] { legacyEntry }));
            var legacyLoaded = new GameLibraryService(legacyStorage).Load();
            Assert(legacyLoaded.Count == 1 && legacyLoaded[0].LaunchMode == "Executable",
                "libraries created before 1.1.0 should default to direct executable mode");

            Assert(legacyLoaded[0].SteamInstallDirectory == string.Empty,
                "legacy executable entries should default to an empty Steam install directory");
            Assert(!legacyLoaded[0].IsFavorite,
                "legacy libraries without a favorite field should default to non-favorite entries");

            var legacySteamStorage = Path.Combine(root, "legacy-steam");
            Directory.CreateDirectory(legacySteamStorage);
            var legacySteamEntry = new
            {
                Id = Guid.NewGuid(),
                Name = "Legacy Steam Entry",
                LaunchMode = "Steam",
                SteamAppId = "271590",
                Category = "Steam",
                AddedUtc = DateTimeOffset.UtcNow,
                LastLaunchedUtc = (DateTimeOffset?)null,
                NeedsLaunchConfirmation = false
            };
            File.WriteAllText(
                Path.Combine(legacySteamStorage, "library.json"),
                System.Text.Json.JsonSerializer.Serialize(new[] { legacySteamEntry }));
            var legacySteamLoaded = new GameLibraryService(legacySteamStorage).Load();
            Assert(legacySteamLoaded.Count == 1 && legacySteamLoaded[0].SteamInstallDirectory == string.Empty,
                "legacy Steam entries without an install path should remain compatible");

            var missingResult = new GameLauncher().Launch(new GameEntry
            {
                Name = "Missing executable",
                ExecutablePath = Path.Combine(root, "missing.exe"),
                WorkingDirectory = root
            });
            Assert(!missingResult.Succeeded, "launcher must reject a missing executable");
            Assert(missingResult.Message.Contains("could not be found", StringComparison.OrdinalIgnoreCase),
                "missing executable should produce a useful validation message");

            var existingFixture = Path.Combine(root, "working-directory-validation.exe");
            File.WriteAllText(existingFixture, "test fixture");
            var relativeWorkingDirectoryResult = new GameLauncher().Launch(new GameEntry
            {
                Name = "Relative working directory",
                ExecutablePath = existingFixture,
                WorkingDirectory = "relative-folder"
            });
            Assert(!relativeWorkingDirectoryResult.Succeeded,
                "launcher must reject a relative working directory before starting a process");
            Assert(relativeWorkingDirectoryResult.Message.Contains("absolute path", StringComparison.OrdinalIgnoreCase),
                "relative working directory should produce a clear corrective message");

            var steamDiscoveryRoot = Path.Combine(root, "steam-discovery");
            var secondarySteamLibrary = Path.Combine(root, "steam-secondary-library");
            var primarySteamApps = Path.Combine(steamDiscoveryRoot, "steamapps");
            var secondarySteamApps = Path.Combine(secondarySteamLibrary, "steamapps");
            var gtaInstallDirectory = Path.Combine(primarySteamApps, "common", "Grand Theft Auto V");
            var secondaryInstallDirectory = Path.Combine(secondarySteamApps, "common", "Secondary Game");
            Directory.CreateDirectory(gtaInstallDirectory);
            Directory.CreateDirectory(secondaryInstallDirectory);
            Directory.CreateDirectory(Path.Combine(primarySteamApps, "outside"));

            var escapedPrimaryPath = steamDiscoveryRoot.Replace("\\", "\\\\", StringComparison.Ordinal);
            var escapedSecondaryPath = secondarySteamLibrary.Replace("\\", "\\\\", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(primarySteamApps, "libraryfolders.vdf"),
                $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{escapedPrimaryPath}\" }} \"1\" {{ \"path\" \"{escapedSecondaryPath}\" }} }}");
            File.WriteAllText(Path.Combine(primarySteamApps, "appmanifest_271590.acf"),
                "\"AppState\" { \"appid\" \"271590\" \"name\" \"Grand Theft Auto V\" \"installdir\" \"Grand Theft Auto V\" }");
            File.WriteAllText(Path.Combine(primarySteamApps, "appmanifest_999.acf"),
                "\"AppState\" { \"appid\" \"999\" \"name\" \"Missing Install\" \"installdir\" \"Not Installed\" }");
            File.WriteAllText(Path.Combine(primarySteamApps, "appmanifest_1001.acf"),
                "\"AppState\" { \"appid\" \"1001\" \"name\" \"Path Escape Fixture\" \"installdir\" \"..\\\\outside\" }");
            File.WriteAllText(Path.Combine(secondarySteamApps, "appmanifest_440.acf"),
                "\"AppState\" { \"appid\" \"440\" \"name\" \"Secondary Game\" \"installdir\" \"Secondary Game\" }");
            File.WriteAllText(Path.Combine(secondarySteamApps, "appmanifest_441.acf"),
                "\"AppState\" { \"appid\" \"not-a-number\" \"name\" \"Invalid App ID\" \"installdir\" \"Secondary Game\" }");

            var discoveredSteamGames = new SteamLibraryDiscoveryService().DiscoverInstalledGames([steamDiscoveryRoot]);
            Assert(discoveredSteamGames.Count == 2,
                "Steam discovery should return installed games from the primary and secondary libraries only");
            Assert(discoveredSteamGames.Any(item => item.AppId == "271590" && item.Name == "Grand Theft Auto V"),
                "Steam discovery should read the GTA V App ID and display name from local metadata");
            Assert(discoveredSteamGames.Any(item => item.AppId == "440" && item.InstallDirectory == secondaryInstallDirectory),
                "Steam discovery should follow libraryfolders.vdf to a secondary Steam library");
            Assert(discoveredSteamGames.All(item => item.AppId is not "999" and not "1001"),
                "Steam discovery must ignore missing installs and manifest paths that escape the common game directory");

            var malformedSteamRoot = Path.Combine(root, "malformed-steam");
            var malformedSteamApps = Path.Combine(malformedSteamRoot, "steamapps");
            Directory.CreateDirectory(Path.Combine(malformedSteamApps, "common", "Malformed Fixture"));
            File.WriteAllText(Path.Combine(malformedSteamApps, "appmanifest_555.acf"), "\"AppState\" { \"appid\" \"555\"");
            var malformedSteamGames = new SteamLibraryDiscoveryService().DiscoverInstalledGames([malformedSteamRoot]);
            Assert(malformedSteamGames.Count == 0,
                "Steam discovery should safely ignore malformed app manifest files");

            var oversizedSteamRoot = Path.Combine(root, "oversized-steam");
            var oversizedSteamApps = Path.Combine(oversizedSteamRoot, "steamapps");
            var oversizedInstallDirectory = Path.Combine(oversizedSteamApps, "common", "Oversized Metadata Fixture");
            Directory.CreateDirectory(oversizedInstallDirectory);
            File.WriteAllText(Path.Combine(oversizedSteamApps, "appmanifest_556.acf"),
                "\"AppState\" { \"appid\" \"556\" \"name\" \"Oversized Metadata Fixture\" \"installdir\" \"Oversized Metadata Fixture\" }");
            File.WriteAllText(Path.Combine(oversizedSteamApps, "libraryfolders.vdf"),
                "\"libraryfolders\" { }" + new string('x', 1024 * 1024 + 1));
            var oversizedSteamGames = new SteamLibraryDiscoveryService().DiscoverInstalledGames([oversizedSteamRoot]);
            Assert(oversizedSteamGames.Any(item => item.AppId == "556"),
                "Steam discovery should skip an oversized library metadata file but still scan the valid primary library");

            var nestedInstallRoot = Path.Combine(root, "nested-install-steam");
            var nestedInstallApps = Path.Combine(nestedInstallRoot, "steamapps");
            var nestedInstallDirectory = Path.Combine(nestedInstallApps, "common", "Nested", "Install");
            Directory.CreateDirectory(nestedInstallDirectory);
            File.WriteAllText(Path.Combine(nestedInstallApps, "appmanifest_557.acf"),
                "\"AppState\" { \"appid\" \"557\" \"name\" \"Nested Install Fixture\" \"installdir\" \"Nested\\\\Install\" }");
            var nestedInstallGames = new SteamLibraryDiscoveryService().DiscoverInstalledGames([nestedInstallRoot]);
            Assert(nestedInstallGames.All(item => item.AppId != "557"),
                "Steam discovery should reject nested install-directory names rather than traversing path fragments");

            var externalGameDirectory = Path.Combine(root, "external-steam-game");
            Directory.CreateDirectory(externalGameDirectory);
            var linkedInstallRoot = Path.Combine(root, "linked-install-steam");
            var linkedInstallApps = Path.Combine(linkedInstallRoot, "steamapps");
            var linkedInstallCommon = Path.Combine(linkedInstallApps, "common");
            var linkedGameDirectory = Path.Combine(linkedInstallCommon, "Linked Game");
            Directory.CreateDirectory(linkedInstallCommon);
            var linkedInstallCreated = false;
            try
            {
                Directory.CreateSymbolicLink(linkedGameDirectory, externalGameDirectory);
                linkedInstallCreated = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               NotSupportedException)
            {
                // Some Windows configurations do not grant symbolic-link creation to the test user.
            }

            if (linkedInstallCreated)
            {
                File.WriteAllText(Path.Combine(linkedInstallApps, "appmanifest_558.acf"),
                    "\"AppState\" { \"appid\" \"558\" \"name\" \"Linked Game Fixture\" \"installdir\" \"Linked Game\" }");
                var linkedInstallGames = new SteamLibraryDiscoveryService().DiscoverInstalledGames([linkedInstallRoot]);
                Assert(linkedInstallGames.All(item => item.AppId != "558"),
                    "Steam discovery must reject game install directories that are reparse points");
            }

            var externalCommonDirectory = Path.Combine(root, "external-steam-common");
            Directory.CreateDirectory(Path.Combine(externalCommonDirectory, "Common Link Game"));
            var linkedCommonRoot = Path.Combine(root, "linked-common-steam");
            var linkedCommonApps = Path.Combine(linkedCommonRoot, "steamapps");
            Directory.CreateDirectory(linkedCommonApps);
            var linkedCommonDirectory = Path.Combine(linkedCommonApps, "common");
            var linkedCommonCreated = false;
            try
            {
                Directory.CreateSymbolicLink(linkedCommonDirectory, externalCommonDirectory);
                linkedCommonCreated = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               NotSupportedException)
            {
                // Some Windows configurations do not grant symbolic-link creation to the test user.
            }

            if (linkedCommonCreated)
            {
                File.WriteAllText(Path.Combine(linkedCommonApps, "appmanifest_559.acf"),
                    "\"AppState\" { \"appid\" \"559\" \"name\" \"Common Link Fixture\" \"installdir\" \"Common Link Game\" }");
                var linkedCommonGames = new SteamLibraryDiscoveryService().DiscoverInstalledGames([linkedCommonRoot]);
                Assert(linkedCommonGames.Count == 0,
                    "Steam discovery must reject a common game directory that is a reparse point");
            }

            // Instantiate the WPF application and windows on an STA thread so CI also parses
            // the XAML resources, resolves event handlers, and creates the UI control trees.
            var app = new NightLauncher.App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var categoryEditWindow = new NightLauncher.Views.AddGameWindow(loaded[0]);
            var editCategoryTextBox = categoryEditWindow.FindName("CategoryTextBox") as TextBox;
            Assert(editCategoryTextBox?.Text == "Test", "edit dialog should preserve the existing game category");
            var newGameDialog = new NightLauncher.Views.AddGameWindow();
            var newCategoryTextBox = newGameDialog.FindName("CategoryTextBox") as TextBox;
            Assert(newCategoryTextBox?.Text == "PC Game", "new games should default to the PC Game category");

            var mainWindow = new NightLauncher.MainWindow();
            Assert(mainWindow.Title == "NightLauncher", "main WPF window should initialize with the expected title");
            Assert(HasGeneratedField(typeof(NightLauncher.MainWindow), "SearchBox"),
                "main WPF window should expose its search control in the generated XAML fields");
            Assert(HasGeneratedField(typeof(NightLauncher.MainWindow), "LaunchButton"),
                "main WPF window should expose its launch control in the generated XAML fields");
            Assert(HasGeneratedField(typeof(NightLauncher.MainWindow), "ScanSteamButton"),
                "main WPF window should expose the installed Steam library scan action");
            Assert(HasGeneratedField(typeof(NightLauncher.Views.SettingsWindow), "OpenLogsFolderButton") &&
                   HasGeneratedField(typeof(NightLauncher.Views.SettingsWindow), "ExportDiagnosticsButton"),
                "settings window should generate accessible log-folder and diagnostics-export controls");
            Assert(HasGeneratedField(typeof(NightLauncher.MainWindow), "FindGamesButton"),
                "main WPF window should expose the selected-folder discovery action");
            Assert(HasGeneratedField(typeof(NightLauncher.MainWindow), "OpenInstallFolderButton"),
                "main WPF window should expose the open install folder action");

            Assert(typeof(NightLauncher.MainWindow).GetMethod("MainWindow_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
                "main WPF window should expose its keyboard shortcut handler");
            var searchControl = mainWindow.FindName("SearchBox") as TextBox;
            Assert(searchControl is not null &&
                   AutomationProperties.GetName(searchControl) == "Search games by name, category, Steam App ID, or install location",
                "search control should expose an explicit automation name");
            var gamesListControl = mainWindow.FindName("GamesList") as ListBox;
            Assert(gamesListControl is not null &&
                   AutomationProperties.GetName(gamesListControl) == "Games in your library",
                "game list should expose an explicit automation name");
            var categoryFilter = mainWindow.FindName("CategoryFilterCombo") as ComboBox;
            Assert(categoryFilter is not null &&
                   AutomationProperties.GetName(categoryFilter) == "Filter games by category",
                "category filter should expose an explicit automation name");
            var mainGamesField = typeof(NightLauncher.MainWindow).GetField("_games", BindingFlags.Instance | BindingFlags.NonPublic);
            var mainGames = mainGamesField?.GetValue(mainWindow) as ObservableCollection<GameEntry>;
            Assert(mainGames is not null, "main window should initialize its game collection");
            mainGames!.Clear();
            mainGames.Add(new GameEntry { Name = "Category filter action", LaunchMode = "Steam", SteamAppId = "700001", Category = "Action" });
            mainGames.Add(new GameEntry { Name = "Category filter RPG", LaunchMode = "Steam", SteamAppId = "700002", Category = "RPG" });
            var refreshLibraryView = typeof(NightLauncher.MainWindow).GetMethod("RefreshLibraryView", BindingFlags.Instance | BindingFlags.NonPublic);
            refreshLibraryView?.Invoke(mainWindow, null);
            var actionCategory = categoryFilter?.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                string.Equals(item.Content?.ToString(), "Action", StringComparison.OrdinalIgnoreCase));
            Assert(actionCategory is not null, "category filter should populate categories from the library");
            categoryFilter!.SelectedItem = actionCategory;
            Assert(gamesListControl?.Items.Count == 1 &&
                   ((GameEntry)gamesListControl.Items[0]).Category == "Action",
                "category filter should show only games in the selected category");
            searchControl!.Text = "RPG";
            Assert(gamesListControl?.Items.Count == 0,
                "text search should be combined with the selected category filter");
            searchControl.Clear();
            var allCategories = categoryFilter.Items.OfType<ComboBoxItem>().First(item =>
                string.Equals(item.Content?.ToString(), "All categories", StringComparison.OrdinalIgnoreCase));
            categoryFilter.SelectedItem = allCategories;
            Assert(gamesListControl?.Items.Count == 2,
                "selecting All categories should restore the full library view");

            var sortMode = mainWindow.FindName("SortModeCombo") as ComboBox;
            Assert(sortMode is not null && AutomationProperties.GetName(sortMode) == "Sort games",
                "sort selector should expose an explicit automation name");
            var now = DateTimeOffset.UtcNow;
            mainGames.Clear();
            mainGames.Add(new GameEntry
            {
                Name = "Zulu favorite", Category = "Action", IsFavorite = true,
                LastLaunchedUtc = now.AddDays(-3), AddedUtc = now.AddDays(-10)
            });
            mainGames.Add(new GameEntry
            {
                Name = "Alpha recent", Category = "RPG",
                LastLaunchedUtc = now, AddedUtc = now
            });
            mainGames.Add(new GameEntry
            {
                Name = "Beta older", Category = "Action",
                LastLaunchedUtc = now.AddDays(-1), AddedUtc = now.AddDays(-1)
            });
            refreshLibraryView?.Invoke(mainWindow, null);
            var recentPlayOption = sortMode?.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                string.Equals(item.Tag?.ToString(), "RecentlyPlayed", StringComparison.OrdinalIgnoreCase));
            Assert(recentPlayOption is not null, "sort selector should offer Recently played");
            sortMode!.SelectedItem = recentPlayOption;
            Assert(((GameEntry)gamesListControl!.Items[0]).Name == "Zulu favorite" &&
                   ((GameEntry)gamesListControl.Items[1]).Name == "Alpha recent" &&
                   ((GameEntry)gamesListControl.Items[2]).Name == "Beta older",
                "recently played sorting should order by launch time while keeping favorites first");
            var recentAddedOption = sortMode.Items.OfType<ComboBoxItem>().First(item =>
                string.Equals(item.Tag?.ToString(), "RecentlyAdded", StringComparison.OrdinalIgnoreCase));
            sortMode.SelectedItem = recentAddedOption;
            Assert(((GameEntry)gamesListControl.Items[0]).Name == "Zulu favorite" &&
                   ((GameEntry)gamesListControl.Items[1]).Name == "Alpha recent" &&
                   ((GameEntry)gamesListControl.Items[2]).Name == "Beta older",
                "recently added sorting should order by added date while keeping favorites first");
            var alphabeticalOption = sortMode.Items.OfType<ComboBoxItem>().First(item =>
                string.Equals(item.Tag?.ToString(), "Name", StringComparison.OrdinalIgnoreCase));
            sortMode.SelectedItem = alphabeticalOption;
            Assert(((GameEntry)gamesListControl.Items[0]).Name == "Zulu favorite" &&
                   ((GameEntry)gamesListControl.Items[1]).Name == "Alpha recent" &&
                   ((GameEntry)gamesListControl.Items[2]).Name == "Beta older",
                "alphabetical sorting should order names within the favorites and non-favorites groups");

            var favoritesOnlyCheckBox = mainWindow.FindName("FavoritesOnlyCheckBox") as CheckBox;
            Assert(favoritesOnlyCheckBox is not null &&
                   AutomationProperties.GetName(favoritesOnlyCheckBox) == "Show favorites only",
                "favorites-only filter should expose an explicit automation name");
            favoritesOnlyCheckBox!.IsChecked = true;
            Assert(gamesListControl.Items.Count == 1 &&
                   ((GameEntry)gamesListControl.Items[0]).Name == "Zulu favorite",
                "favorites-only filter should hide non-favorite games");
            categoryFilter!.SelectedItem = categoryFilter.Items.OfType<ComboBoxItem>().First(item =>
                string.Equals(item.Content?.ToString(), "Action", StringComparison.OrdinalIgnoreCase));
            Assert(gamesListControl.Items.Count == 1 &&
                   ((GameEntry)gamesListControl.Items[0]).Name == "Zulu favorite",
                "favorites-only filter should combine with the selected category");
            categoryFilter.SelectedItem = categoryFilter.Items.OfType<ComboBoxItem>().First(item =>
                string.Equals(item.Content?.ToString(), "RPG", StringComparison.OrdinalIgnoreCase));
            Assert(gamesListControl.Items.Count == 0,
                "favorites-only and category filters should return no results when their conditions do not overlap");
            categoryFilter.SelectedItem = categoryFilter.Items.OfType<ComboBoxItem>().First(item =>
                string.Equals(item.Content?.ToString(), "All categories", StringComparison.OrdinalIgnoreCase));
            favoritesOnlyCheckBox.IsChecked = false;
            Assert(gamesListControl.Items.Count == 3,
                "clearing favorites-only should restore all games when All categories is selected");

            // Show the real WPF window and drive its controls through Windows UI Automation.
            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.UpdateLayout();
            mainWindow.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            var liveWindowHandle = new WindowInteropHelper(mainWindow).Handle;
            Assert(liveWindowHandle != IntPtr.Zero, "live main window should create a native window handle");
            var liveWindow = AutomationElement.FromHandle(liveWindowHandle);
            Assert(liveWindow.Current.Name == "NightLauncher",
                "UI Automation should attach to the visible NightLauncher window");

            var liveSearch = liveWindow.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty,
                    "Search games by name, category, Steam App ID, or install location"));
            Assert(liveSearch is not null, "UI Automation should find the live search field");
            Assert(liveSearch!.TryGetCurrentPattern(ValuePattern.Pattern, out var liveSearchPattern),
                "UI Automation should find the live search field's Value pattern");
            ((ValuePattern)liveSearchPattern).SetValue("Zulu");
            mainWindow.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

            var liveFavorites = liveWindow.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Show favorites only"));
            Assert(liveFavorites is not null, "UI Automation should find the live favorites-only checkbox");
            Assert(liveFavorites!.TryGetCurrentPattern(TogglePattern.Pattern, out var liveTogglePattern),
                "UI Automation should find the live favorites-only checkbox's Toggle pattern");
            ((TogglePattern)liveTogglePattern).Toggle();
            mainWindow.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Assert(((TogglePattern)liveTogglePattern).Current.ToggleState == ToggleState.On,
                "UI Automation should be able to turn on the live favorites-only checkbox");

            var liveGamesList = liveWindow.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Games in your library"));
            Assert(liveGamesList is not null, "UI Automation should find the live games list");
            var visibleLiveItems = liveGamesList!.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            Assert(visibleLiveItems.Count == 1,
                "the live window should show exactly one game after UI Automation searches Zulu and enables favorites-only");

            ((TogglePattern)liveTogglePattern).Toggle();
            ((ValuePattern)liveSearchPattern).SetValue(string.Empty);
            mainWindow.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            var restoredLiveItems = liveGamesList.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            Assert(restoredLiveItems.Count == 3,
                "UI Automation should be able to reset the live search and favorites-only filter");
            mainWindow.Close();

            var favoriteButton = mainWindow.FindName("ToggleFavoriteButton") as Button;
            Assert(favoriteButton is not null && !favoriteButton.IsEnabled,
                "favorite toggle should initialize disabled until a game is selected");
            Assert(favoriteButton is not null &&
                   AutomationProperties.GetName(favoriteButton) == "Toggle favorite status for selected game",
                "favorite toggle should expose an explicit automation name");

            settingsService.SaveBackupDirectory(backupDirectory);
            Assert(string.Equals(settingsService.LoadBackupDirectory(), Path.GetFullPath(backupDirectory), StringComparison.OrdinalIgnoreCase),
                "settings should persist the active backup folder");
            var settingsWindow = new NightLauncher.Views.SettingsWindow(
                service.LibraryPath,
                backupService,
                settingsService);
            Assert(settingsWindow.Title == "Settings", "settings WPF window should initialize");
            Assert(HasGeneratedField(typeof(NightLauncher.Views.SettingsWindow), "RestoreBackupButton"),
                "settings WPF window should expose the selected-backup restore control");
            Assert(HasGeneratedField(typeof(NightLauncher.Views.SettingsWindow), "BackupStatusText"),
                "settings WPF window should expose backup status text");
            Assert(HasGeneratedField(typeof(NightLauncher.Views.SettingsWindow), "BackupListBox"),
                "settings WPF window should expose the selectable recovery point list");
            Assert(HasGeneratedField(typeof(NightLauncher.Views.SettingsWindow), "SelectBackupFolderButton"),
                "settings WPF window should expose the backup folder selector");
            var recoveryList = settingsWindow.FindName("BackupListBox") as System.Windows.Controls.ListBox;
            Assert(recoveryList is not null && recoveryList.Items.Count == backupService.GetBackups().Count,
                "settings WPF window should populate the selectable recovery-point list");
            Assert(recoveryList?.SelectedIndex == 0,
                "settings WPF window should select the newest recovery point by default");
            Assert(string.Equals(settingsWindow.SelectedBackupDirectory, backupDirectory, StringComparison.OrdinalIgnoreCase),
                "settings window should display the active backup directory");

            app.Shutdown();
            Console.WriteLine("All NightLauncher smoke and WPF initialization tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Temporary test data cleanup is best-effort.
            }
        }
    }
}
