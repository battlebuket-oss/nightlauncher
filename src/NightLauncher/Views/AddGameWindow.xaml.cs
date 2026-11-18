using Microsoft.Win32;
using NightLauncher.Models;
using System.Windows;

namespace NightLauncher.Views;

public partial class AddGameWindow : Window
{
    private readonly GameEntry? _original;

    public AddGameWindow(GameEntry? game = null)
    {
        InitializeComponent();
        _original = game?.Copy();

        if (_original is not null)
        {
            HeadingText.Text = "Edit game";
            NameTextBox.Text = _original.Name;
            ExecutableTextBox.Text = _original.ExecutablePath;
            ArgumentsTextBox.Text = _original.LaunchArguments;
        }
    }

    public GameEntry? ResultGame { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a game executable",
            Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        var currentPath = ExecutableTextBox.Text.Trim();
        if (File.Exists(currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
        }

        if (dialog.ShowDialog(this) == true)
        {
            ExecutableTextBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                NameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var executable = ExecutableTextBox.Text.Trim();
        var arguments = ArgumentsTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowValidation("Enter a display name for this game.");
            return;
        }

        if (string.IsNullOrWhiteSpace(executable) ||
            !Path.IsPathFullyQualified(executable) ||
            !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executable))
        {
            ShowValidation("Choose an existing Windows .exe file.");
            return;
        }

        var directory = Path.GetDirectoryName(executable);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            ShowValidation("The executable's folder could not be found.");
            return;
        }

        if (arguments.Length > 4096)
        {
            ShowValidation("Launch arguments are too long.");
            return;
        }

        ResultGame = new GameEntry
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Name = name,
            ExecutablePath = executable,
            LaunchArguments = arguments,
            WorkingDirectory = directory,
            Category = _original?.Category ?? "PC Game",
            AddedUtc = _original?.AddedUtc ?? DateTimeOffset.UtcNow,
            LastLaunchedUtc = _original?.LastLaunchedUtc
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowValidation(string message)
    {
        MessageBox.Show(this, message, "Check game details", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
