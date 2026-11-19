using System.Text.Json.Serialization;

namespace NightLauncher.Models;

public sealed class GameEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string LaunchMode { get; set; } = "Executable";
    public string SteamAppId { get; set; } = string.Empty;
    public string SteamInstallDirectory { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Category { get; set; } = "PC Game";
    public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLaunchedUtc { get; set; }
    public bool NeedsLaunchConfirmation { get; set; }

    [JsonIgnore]
    public string LaunchTargetDescription =>
        string.Equals(LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase)
            ? $"Steam App ID: {SteamAppId}"
            : ExecutablePath;

    [JsonIgnore]
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0)
            {
                return "NG";
            }

            return words.Length == 1
                ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant()
                : new string(words.Take(2).Select(word => word[0]).ToArray()).ToUpperInvariant();
        }
    }

    public GameEntry Copy() => new()
    {
        Id = Id,
        Name = Name,
        LaunchMode = LaunchMode,
        SteamAppId = SteamAppId,
        SteamInstallDirectory = SteamInstallDirectory,
        ExecutablePath = ExecutablePath,
        LaunchArguments = LaunchArguments,
        WorkingDirectory = WorkingDirectory,
        Category = Category,
        AddedUtc = AddedUtc,
        LastLaunchedUtc = LastLaunchedUtc,
        NeedsLaunchConfirmation = NeedsLaunchConfirmation
    };
}
