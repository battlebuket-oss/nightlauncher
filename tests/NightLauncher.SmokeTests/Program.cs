using System.IO;
using System.Reflection;
using System.Windows;
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
                Category = "Test"
            };

            var steamGame = new GameEntry
            {
                Name = "Example Steam Game",
                LaunchMode = "Steam",
                SteamAppId = "271590",
                Category = "Steam"
            };

            service.Save([game, steamGame]);
            var loaded = service.Load();
            Assert(loaded.Count == 2, "library save/load should preserve both executable and Steam entries");
            Assert(loaded[0].Name == game.Name, "library save/load should preserve the game name");
            Assert(loaded[0].ExecutablePath == game.ExecutablePath, "library save/load should preserve the executable path");
            Assert(loaded[1].LaunchMode == "Steam", "library save/load should preserve the Steam launch mode");
            Assert(loaded[1].SteamAppId == "271590", "library save/load should preserve the Steam App ID");

            var exportPath = Path.Combine(root, "backup.json");
            service.Export(exportPath, loaded);
            var imported = service.Import(exportPath);
            Assert(imported.Count == 2, "JSON export/import should preserve both launch modes");
            Assert(imported.All(item => item.NeedsLaunchConfirmation), "imported entries should require a one-time review");
            Assert(imported[1].LaunchMode == "Steam" && imported[1].SteamAppId == "271590",
                "JSON export/import should preserve Steam launch metadata");

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
            settingsService.SaveBackupDirectory(customBackupDirectory);
            var reloadedSettings = new NightLauncherSettingsService(settingsPath);
            Assert(string.Equals(reloadedSettings.LoadBackupDirectory(), Path.GetFullPath(customBackupDirectory), StringComparison.OrdinalIgnoreCase),
                "settings should persist and reload the custom backup directory");

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

            var mainWindow = new NightLauncher.MainWindow();
            Assert(mainWindow.Title == "NightLauncher", "main WPF window should initialize with the expected title");
            Assert(HasGeneratedField(typeof(NightLauncher.MainWindow), "SearchBox"),
                "main WPF window should expose its search control in the generated XAML fields");
            Assert(HasGeneratedField(typeof(NightLauncher.MainWindow), "LaunchButton"),
                "main WPF window should expose its launch control in the generated XAML fields");
            Assert(HasGeneratedField(typeof(NightLauncher.MainWindow), "ScanSteamButton"),
                "main WPF window should expose the installed Steam library scan action");

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
