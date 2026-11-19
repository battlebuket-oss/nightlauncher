using System.IO;
using Microsoft.Win32;
using NightLauncher.Models;
using NightLauncher.Services;
using NightLauncher.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace NightLauncher;

public partial class MainWindow : Window
{
    private readonly GameLibraryService _library = new();
    private GameLibraryBackupService _backups;
    private readonly NightLauncherSettingsService _settings = new();
    private readonly GameDiscoveryService _discovery = new();
    private readonly GameLauncher _launcher = new();
    private readonly ObservableCollection<GameEntry> _games = [];
    private ICollectionView? _gamesView;

    public MainWindow()
    {
        InitializeComponent();
        string? backupDirectory = null;
        try
        {
            backupDirectory = _settings.LoadBackupDirectory();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this,
                $"NightLauncher could not read its settings and will use the default backup folder for this session.\\n\\n{exception.Message}",
                "Settings file needs attention", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _backups = new GameLibraryBackupService(_library, backupDirectory);
        GamesList.ItemsSource = _games;
        _gamesView = CollectionViewSource.GetDefaultView(_games);
        _gamesView.Filter = MatchesSearch;
        LoadLibrary();
    }

    private void LoadLibrary()
    {
        _games.Clear();
        try
        {
            foreach (var game in _library.Load().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                _games.Add(game);
            }

            SetStatus($"Library loaded · {_games.Count} game(s)");
        }
        catch (InvalidDataException exception)
        {
            SetStatus("Library file needs attention");
            MessageBox.Show(this, exception.Message, "Library could not be loaded",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("Unable to read the local library");
            MessageBox.Show(this, exception.Message, "NightLauncher",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RefreshLibraryView();
    }

    private bool MatchesSearch(object item)
    {
        if (item is not GameEntry game)
        {
            return false;
        }

        var query = SearchBox.Text.Trim();
        return query.Length == 0 ||
               game.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               game.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               game.LaunchTargetDescription.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               game.SteamAppId.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSameLaunchTarget(GameEntry first, GameEntry second)
    {
        if (string.Equals(first.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(second.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(first.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(second.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(first.SteamAppId, second.SteamAppId, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(first.ExecutablePath, second.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshLibraryView()
    {
        _gamesView?.Refresh();
        CountText.Text = $"{_games.Count} game{(_games.Count == 1 ? string.Empty : "s")}";

        var hasVisibleGames = _gamesView?.Cast<object>().Any() == true;
        EmptyState.Visibility = hasVisibleGames ? Visibility.Collapsed : Visibility.Visible;

        if (_games.Count > 0 && !hasVisibleGames)
        {
            EmptyTitle.Text = "No matching games";
            EmptyMessage.Text = "Try another search term or clear the search box.";
        }
        else
        {
            EmptyTitle.Text = "Your library is empty";
            EmptyMessage.Text = "Add a game by selecting its executable file or a Steam App ID.";
        }
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddGameWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultGame is not { } game)
        {
            return;
        }

        if (_games.Any(existing => HasSameLaunchTarget(existing, game)))
        {
            MessageBox.Show(this, "That launch target is already in your library.", "Already added",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _games.Add(game);
        SaveLibrary();
        GamesList.SelectedItem = game;
        RefreshLibraryView();
        SetStatus($"Added {game.Name}");
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry selected)
        {
            return;
        }

        var dialog = new AddGameWindow(selected) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultGame is not { } edited)
        {
            return;
        }

        if (_games.Any(existing => existing.Id != selected.Id && HasSameLaunchTarget(existing, edited)))
        {
            MessageBox.Show(this, "That launch target is already in your library.", "Already added",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var index = _games.IndexOf(selected);
        if (index >= 0)
        {
            _games[index] = edited;
            GamesList.SelectedItem = edited;
            SaveLibrary();
            RefreshLibraryView();
            SetStatus($"Updated {edited.Name}");
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry selected)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"Remove '{selected.Name}' from your NightLauncher library?\n\nThe game files will not be deleted.",
            "Remove game", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _games.Remove(selected);
        SaveLibrary();
        ClearSelection();
        RefreshLibraryView();
        SetStatus($"Removed {selected.Name} from the library");
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry selected)
        {
            return;
        }

        var needsConfirmation = selected.NeedsLaunchConfirmation;
        if (needsConfirmation)
        {
            var answer = MessageBox.Show(this,
                $"This game entry was imported or discovered from a folder. Confirm that you trust this launch target before running it:\n\nLaunch target: {selected.LaunchTargetDescription}\nArguments: {selected.LaunchArguments}\n\nContinue?",
                "Review imported launch target", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var result = _launcher.Launch(selected);
        SetStatus(result.Message);

        if (!result.Succeeded)
        {
            MessageBox.Show(this, result.Message, "Could not launch game",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        selected.NeedsLaunchConfirmation = false;
        selected.LastLaunchedUtc = DateTimeOffset.UtcNow;
        SaveLibrary();
        UpdateDetails(selected);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a NightLauncher library",
            Filter = "NightLauncher library (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imported = _library.Import(dialog.FileName);
            var added = 0;
            foreach (var game in imported)
            {
                if (_games.Any(existing => HasSameLaunchTarget(existing, game)))
                {
                    continue;
                }

                if (_games.Any(existing => existing.Id == game.Id))
                {
                    game.Id = Guid.NewGuid();
                }

                _games.Add(game);
                added++;
            }

            SaveLibrary();
            RefreshLibraryView();
            SetStatus($"Imported {added} game(s)");
            MessageBox.Show(this, $"Imported {added} game(s). Existing launch targets were skipped.",
                "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message, "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export your NightLauncher library",
            Filter = "NightLauncher library (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "NightLauncher-library.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _library.Export(dialog.FileName, _games);
            SetStatus("Library exported");
            MessageBox.Show(this, "Your library backup was exported successfully.",
                "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message, "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DiscoverGames_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = "Choose a folder to scan for game executables",
            Multiselect = false
        };

        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        IReadOnlyList<DiscoveredGameExecutable> candidates;
        try
        {
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            candidates = _discovery.DiscoverExecutables(folderDialog.FolderName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, $"NightLauncher could not scan that folder.\n\n{exception.Message}",
                "Game discovery failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
        }

        if (candidates.Count == 0)
        {
            MessageBox.Show(this,
                "No executable candidates were found within the scan depth and result limits.",
                "No games found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selection = new DiscoveredGamesWindow(candidates) { Owner = this };
        if (selection.ShowDialog() != true || selection.SelectedExecutables.Count == 0)
        {
            return;
        }

        var additions = new List<GameEntry>();
        foreach (var candidate in selection.SelectedExecutables)
        {
            if (_games.Any(existing =>
                    string.Equals(existing.ExecutablePath, candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            additions.Add(new GameEntry
            {
                Name = candidate.DisplayName,
                LaunchMode = "Executable",
                ExecutablePath = candidate.ExecutablePath,
                WorkingDirectory = candidate.WorkingDirectory,
                Category = "PC Game",
                NeedsLaunchConfirmation = true
            });
        }

        if (additions.Count == 0)
        {
            MessageBox.Show(this, "All selected executables are already in your library.",
                "Nothing new to add", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var game in additions)
        {
            _games.Add(game);
        }

        SaveLibrary();
        RefreshLibraryView();
        GamesList.SelectedItem = additions[^1];
        GamesList.ScrollIntoView(additions[^1]);
        SetStatus($"Discovered and added {additions.Count} game(s) · launch review required");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(_library.LibraryPath, _backups, _settings) { Owner = this };
        settings.ShowDialog();

        if (settings.BackupDirectoryChanged)
        {
            _backups = new GameLibraryBackupService(_library, settings.SelectedBackupDirectory);
            SetStatus($"Backup folder updated · {_backups.BackupDirectory}");
        }

        if (settings.LibraryRestored)
        {
            LoadLibrary();
            SetStatus($"Library restored · {_games.Count} game(s)");
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshLibraryView();

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesList.SelectedItem is GameEntry selected)
        {
            UpdateDetails(selected);
        }
        else
        {
            ClearSelection();
        }
    }

    private void UpdateDetails(GameEntry game)
    {
        DetailInitials.Text = game.Initials;
        DetailName.Text = game.Name;
        DetailCategory.Text = game.Category;
        DetailPath.Text = game.LaunchTargetDescription;
        DetailLastLaunched.Text = game.LastLaunchedUtc?.ToLocalTime().ToString("g") ?? "Not launched yet";
        LaunchButton.IsEnabled = true;
        EditButton.IsEnabled = true;
        RemoveButton.IsEnabled = true;
    }

    private void ClearSelection()
    {
        DetailInitials.Text = "—";
        DetailName.Text = "Select a game";
        DetailCategory.Text = "No game selected";
        DetailPath.Text = "Choose a game from your library to see its launch target.";
        DetailLastLaunched.Text = "Not launched yet";
        LaunchButton.IsEnabled = false;
        EditButton.IsEnabled = false;
        RemoveButton.IsEnabled = false;
    }

    private void SaveLibrary()
    {
        try
        {
            if (File.Exists(_library.LibraryPath))
            {
                _backups.CreateBackup();
            }

            _library.Save(_games);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, $"Your change could not be saved.\n\n{exception.Message}",
                "Library save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Library save failed");
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
