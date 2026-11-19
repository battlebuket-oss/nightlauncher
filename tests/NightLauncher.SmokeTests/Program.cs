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

    service.Save([game]);
    var loaded = service.Load();
    Assert(loaded.Count == 1, "library save/load should preserve the item count");
    Assert(loaded[0].Name == game.Name, "library save/load should preserve the game name");
    Assert(loaded[0].ExecutablePath == game.ExecutablePath, "library save/load should preserve the executable path");

    var exportPath = Path.Combine(root, "backup.json");
    service.Export(exportPath, loaded);
    var imported = service.Import(exportPath);
    Assert(imported.Count == 1, "JSON export/import should preserve the item count");
    Assert(imported[0].Name == game.Name, "JSON export/import should preserve the game name");
    Assert(imported[0].NeedsLaunchConfirmation, "imported games should require a one-time executable review");

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
