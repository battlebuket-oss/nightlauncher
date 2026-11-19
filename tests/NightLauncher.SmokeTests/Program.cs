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
