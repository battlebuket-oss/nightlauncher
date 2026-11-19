using System.IO;
using NightLauncher.Models;
using NightLauncher.Services;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"SMOKE TEST FAILED: {message}");
    }
}

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

    Console.WriteLine("All NightLauncher smoke tests passed.");
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
