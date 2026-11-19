namespace NightLauncher.Models;

public sealed record DiscoveredGameExecutable(
    string DisplayName,
    string ExecutablePath,
    string WorkingDirectory);
