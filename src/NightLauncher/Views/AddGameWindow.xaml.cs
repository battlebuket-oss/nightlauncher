using System.IO;
using Microsoft.Win32;
using NightLauncher.Models;
using System.Windows;
using System.Windows.Controls;

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
            CategoryTextBox.Text = _original.Category;
            ExecutableTextBox.Text = _original.ExecutablePath;
            SteamAppIdTextBox.Text = _original.SteamAppId;
            ArgumentsTextBox.Text = _original.LaunchArguments;
            LaunchModeCombo.SelectedIndex =
                string.Equals(_original.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        else
        {
            CategoryTextBox.Text = "PC Game";
            LaunchModeCombo.SelectedIndex = 0;
        }

        UpdateLaunchModeFields();
    }

    public GameEntry? ResultGame { get; private set; }

    private void LaunchMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateLaunchModeFields();
    }

    private void UpdateLaunchModeFields()
    {
        if (ExecutablePanel is null || SteamPanel is null || ArgumentsPanel is null)
        {
            return;
        }

        var mode = (LaunchModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var isSteam = string.Equals(mode, "Steam", StringComparison.OrdinalIgnoreCase);
        ExecutablePanel.Visibility = isSteam ? Visibility.Collapsed : Visibility.Visible;
        SteamPanel.Visibility = isSteam ? Visibility.Visible : Visibility.Collapsed;
        ArgumentsPanel.Visibility = isSteam ? Visibility.Collapsed : Visibility.Visible;
    }

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
        var mode = (LaunchModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Executable";
        var category = CategoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "PC Game";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowValidation("Enter a display name for this game.");
            return;
        }

        if (string.Equals(mode, "Steam", StringComparison.OrdinalIgnoreCase))
        {
            var appId = SteamAppIdTextBox.Text.Trim();
            if (!uint.TryParse(appId, out var parsedAppId) || parsedAppId == 0)
            {
                ShowValidation("Enter a valid numeric Steam App ID.");
                return;
            }

            ResultGame = new GameEntry
            {
                Id = _original?.Id ?? Guid.NewGuid(),
                Name = name,
                LaunchMode = "Steam",
                SteamAppId = parsedAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Category = category,
                AddedUtc = _original?.AddedUtc ?? DateTimeOffset.UtcNow,
                LastLaunchedUtc = _original?.LastLaunchedUtc,
                NeedsLaunchConfirmation = _original?.NeedsLaunchConfirmation ?? false,
                IsFavorite = _original?.IsFavorite ?? false
            };

            DialogResult = true;
            return;
        }

        var executable = ExecutableTextBox.Text.Trim();
        var arguments = ArgumentsTextBox.Text.Trim();

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
            LaunchMode = "Executable",
            ExecutablePath = executable,
            LaunchArguments = arguments,
            WorkingDirectory = directory,
            Category = category,
            AddedUtc = _original?.AddedUtc ?? DateTimeOffset.UtcNow,
            LastLaunchedUtc = _original?.LastLaunchedUtc,
            NeedsLaunchConfirmation = _original?.NeedsLaunchConfirmation ?? false,
            IsFavorite = _original?.IsFavorite ?? false
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowValidation(string message)
    {
        MessageBox.Show(this, message, "Check game details", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
