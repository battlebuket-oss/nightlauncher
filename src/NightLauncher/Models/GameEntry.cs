using System.Text.Json.Serialization;

namespace NightLauncher.Models;

public sealed class GameEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Category { get; set; } = "PC Game";
    public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLaunchedUtc { get; set; }
    public bool NeedsLaunchConfirmation { get; set; }

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
                : string.Concat(words.Take(2).Select(word => word[0])).ToUpperInvariant();
        }
    }

    public GameEntry Copy() => new()
    {
        Id = Id,
        Name = Name,
        ExecutablePath = ExecutablePath,
        LaunchArguments = LaunchArguments,
        WorkingDirectory = WorkingDirectory,
        Category = Category,
        AddedUtc = AddedUtc,
        LastLaunchedUtc = LastLaunchedUtc,
        NeedsLaunchConfirmation = NeedsLaunchConfirmation
    };
}
