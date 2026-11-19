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
    private readonly GameLauncher _launcher = new();
    private readonly ObservableCollection<GameEntry> _games = [];
    private ICollectionView? _gamesView;

    public MainWindow()
    {
        InitializeComponent();
        GamesList.ItemsSource = _games;
        _gamesView = CollectionViewSource.GetDefaultView(_games);
        _gamesView.Filter = MatchesSearch;
        LoadLibrary();
    }

    private void LoadLibrary()
    {
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
               game.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase);
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
            EmptyMessage.Text = "Add a game by selecting its executable file.";
        }
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddGameWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultGame is not { } game)
        {
            return;
        }

        if (_games.Any(existing => string.Equals(existing.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That executable is already in your library.", "Already added",
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
                $"This game entry came from an imported library. Confirm that you trust this executable and its arguments before running it:\n\nExecutable: {selected.ExecutablePath}\nArguments: {selected.LaunchArguments}\n\nContinue?",
                "Review imported executable", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
                if (_games.Any(existing => string.Equals(existing.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
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
            MessageBox.Show(this, $"Imported {added} game(s). Existing executable paths were skipped.",
                "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow(_library.LibraryPath) { Owner = this }.ShowDialog();
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
        DetailPath.Text = game.ExecutablePath;
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
        DetailPath.Text = "Choose a game from your library to see its path.";
        DetailLastLaunched.Text = "Not launched yet";
        LaunchButton.IsEnabled = false;
        EditButton.IsEnabled = false;
        RemoveButton.IsEnabled = false;
    }

    private void SaveLibrary()
    {
        try
        {
            _library.Save(_games);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Your change could not be saved.\n\n{exception.Message}",
                "Library save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Library save failed");
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
