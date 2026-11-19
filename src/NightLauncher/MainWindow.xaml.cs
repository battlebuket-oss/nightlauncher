using System.IO;
using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using NightLauncher.Models;
using NightLauncher.Services;
using NightLauncher.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace NightLauncher;

public partial class MainWindow : Window
{
    private readonly GameLibraryService _library = new();
    private GameLibraryBackupService _backups;
    private readonly NightLauncherSettingsService _settings = new();
    private readonly GameDiscoveryService _discovery = new();
    private readonly SteamLibraryDiscoveryService _steamDiscovery = new();
    private readonly GameLauncher _launcher = new();
    private readonly ObservableCollection<GameEntry> _games = [];
    private ICollectionView? _gamesView;
    private bool _isUpdatingCategoryFilter;

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
        _gamesView.SortDescriptions.Add(new SortDescription(nameof(GameEntry.IsFavorite), ListSortDirection.Descending));
        _gamesView.SortDescriptions.Add(new SortDescription(nameof(GameEntry.Name), ListSortDirection.Ascending));
        _gamesView.Filter = MatchesSearch;
        RefreshCategoryFilterChoices();
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
        var selectedCategory = (CategoryFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var matchesCategory = string.IsNullOrWhiteSpace(selectedCategory) ||
                              string.Equals(selectedCategory, "All categories", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(game.Category, selectedCategory, StringComparison.OrdinalIgnoreCase);

        return matchesCategory && (query.Length == 0 ||
               game.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               game.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               game.LaunchTargetDescription.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               game.SteamAppId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               game.SteamInstallDirectory.Contains(query, StringComparison.OrdinalIgnoreCase));
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

    private void RefreshCategoryFilterChoices()
    {
        var selectedCategory = (CategoryFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All categories";
        _isUpdatingCategoryFilter = true;
        try
        {
            CategoryFilterCombo.Items.Clear();
            var allCategories = new ComboBoxItem { Content = "All categories" };
            CategoryFilterCombo.Items.Add(allCategories);

            foreach (var category in _games
                         .Select(game => game.Category.Trim())
                         .Where(category => category.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
            {
                CategoryFilterCombo.Items.Add(new ComboBoxItem { Content = category });
            }

            CategoryFilterCombo.SelectedItem = CategoryFilterCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), selectedCategory, StringComparison.OrdinalIgnoreCase))
                ?? allCategories;
        }
        finally
        {
            _isUpdatingCategoryFilter = false;
        }
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingCategoryFilter)
        {
            RefreshLibraryView();
        }
    }

    private void RefreshLibraryView()
    {
        RefreshCategoryFilterChoices();
        _gamesView?.Refresh();
        var visibleCount = _gamesView?.Cast<object>().Count() ?? 0;
        CountText.Text = visibleCount == _games.Count
            ? $"{_games.Count} game{(_games.Count == 1 ? string.Empty : "s")}"
            : $"{visibleCount} of {_games.Count} games";

        var hasVisibleGames = visibleCount > 0;
        EmptyState.Visibility = hasVisibleGames ? Visibility.Collapsed : Visibility.Visible;

        if (_games.Count > 0 && !hasVisibleGames)
        {
            EmptyTitle.Text = "No matching games";
            EmptyMessage.Text = "Try another search term or select All categories.";
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

        if (string.Equals(edited.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(edited.SteamAppId, selected.SteamAppId, StringComparison.Ordinal))
        {
            // The edit dialog does not change the Steam install location; retain the discovered metadata.
            edited.SteamInstallDirectory = selected.SteamInstallDirectory;
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

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry selected)
        {
            return;
        }

        selected.IsFavorite = !selected.IsFavorite;
        SaveLibrary();
        RefreshLibraryView();
        GamesList.SelectedItem = selected;
        GamesList.ScrollIntoView(selected);
        UpdateDetails(selected);
        SetStatus(selected.IsFavorite
            ? $"Added {selected.Name} to favorites"
            : $"Removed {selected.Name} from favorites");
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

    private void SetDiscoveryBusy(bool isBusy, string? status = null)
    {
        FindGamesButton.IsEnabled = !isBusy;
        ScanSteamButton.IsEnabled = !isBusy;
        System.Windows.Input.Mouse.OverrideCursor = isBusy
            ? System.Windows.Input.Cursors.Wait
            : null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            SetStatus(status);
        }
    }

    private async void DiscoverGames_Click(object sender, RoutedEventArgs e)
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
        SetDiscoveryBusy(true, "Scanning selected folder for game executables…");
        try
        {
            var selectedFolder = folderDialog.FolderName;
            candidates = await Task.Run(() => _discovery.DiscoverExecutables(selectedFolder));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, $"NightLauncher could not scan that folder.\n\n{exception.Message}",
                "Game discovery failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            SetDiscoveryBusy(false);
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

    private async void DiscoverSteamGames_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<DiscoveredSteamGame> discovered;
        SetDiscoveryBusy(true, "Checking installed Steam libraries…");
        try
        {
            discovered = await Task.Run(() => _steamDiscovery.DiscoverInstalledGames());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or SecurityException or ArgumentException)
        {
            MessageBox.Show(this, $"NightLauncher could not read the local Steam library metadata.\n\n{exception.Message}",
                "Steam library scan failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            SetDiscoveryBusy(false);
        }

        if (discovered.Count == 0)
        {
            var chooseFolder = MessageBox.Show(this,
                "NightLauncher could not find an installed Steam game automatically. Would you like to select a Steam installation or library folder manually?",
                "Steam folder not detected", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (chooseFolder == MessageBoxResult.Yes)
            {
                var folderDialog = new OpenFolderDialog
                {
                    Title = "Select a Steam installation or library folder (the folder containing steamapps)",
                    Multiselect = false
                };

                if (folderDialog.ShowDialog(this) == true)
                {
                    var selectedRoot = folderDialog.FolderName;
                    var selectedName = Path.GetFileName(Path.TrimEndingDirectorySeparator(selectedRoot));
                    if (string.Equals(selectedName, "steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedRoot = Directory.GetParent(selectedRoot)?.FullName ?? selectedRoot;
                    }
                    else if (string.Equals(selectedName, "common", StringComparison.OrdinalIgnoreCase))
                    {
                        var steamApps = Directory.GetParent(selectedRoot);
                        if (steamApps is not null &&
                            string.Equals(steamApps.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                        {
                            selectedRoot = steamApps.Parent?.FullName ?? selectedRoot;
                        }
                    }

                    SetDiscoveryBusy(true, "Scanning selected Steam library…");
                    try
                    {
                        var manualSteamRoot = selectedRoot;
                        discovered = await Task.Run(() => _steamDiscovery.DiscoverInstalledGames([manualSteamRoot]));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                       InvalidDataException or SecurityException or ArgumentException)
                    {
                        MessageBox.Show(this, $"NightLauncher could not scan that Steam folder.\n\n{exception.Message}",
                            "Steam library scan failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    finally
                    {
                        SetDiscoveryBusy(false);
                    }
                }
            }
        }

        if (discovered.Count == 0)
        {
            MessageBox.Show(this,
                "No installed Steam games were found. You can scan again and choose a different Steam installation or library folder.",
                "No installed Steam games found", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("Steam scan complete · no installed games found");
            return;
        }

        var additions = new List<GameEntry>();
        var updated = 0;
        var unchanged = 0;
        foreach (var steamGame in discovered)
        {
            var existing = _games.FirstOrDefault(game =>
                string.Equals(game.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(game.SteamAppId, steamGame.AppId, StringComparison.Ordinal));

            if (existing is not null)
            {
                var metadataChanged =
                    !string.Equals(existing.Name, steamGame.Name, StringComparison.Ordinal) ||
                    !string.Equals(existing.SteamInstallDirectory, steamGame.InstallDirectory, StringComparison.OrdinalIgnoreCase);

                if (metadataChanged)
                {
                    existing.Name = steamGame.Name;
                    existing.SteamInstallDirectory = steamGame.InstallDirectory;
                    updated++;
                }
                else
                {
                    unchanged++;
                }

                continue;
            }

            additions.Add(new GameEntry
            {
                Name = steamGame.Name,
                LaunchMode = "Steam",
                SteamAppId = steamGame.AppId,
                SteamInstallDirectory = steamGame.InstallDirectory,
                Category = "Steam",
                NeedsLaunchConfirmation = true
            });
        }

        if (additions.Count == 0 && updated == 0)
        {
            MessageBox.Show(this, $"Found {discovered.Count} installed Steam game(s). They are already up to date.",
                "Steam library is up to date", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus($"Steam scan complete · {unchanged} already up to date");
            return;
        }

        foreach (var game in additions)
        {
            _games.Add(game);
        }

        SaveLibrary();
        RefreshLibraryView();

        if (additions.Count > 0)
        {
            GamesList.SelectedItem = additions[^1];
            GamesList.ScrollIntoView(additions[^1]);
        }
        else if (GamesList.SelectedItem is GameEntry selected)
        {
            UpdateDetails(selected);
        }

        SetStatus($"Steam scan complete · added {additions.Count} · refreshed {updated}");
        MessageBox.Show(this,
            $"Found {discovered.Count} installed Steam game(s). Added {additions.Count}; refreshed {updated}; unchanged {unchanged}.\n\nNew entries require a one-time launch review and are handed to the official Steam client. Steam continues to perform its normal sign-in and ownership checks.",
            "Steam library scan complete", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && (SearchBox.IsKeyboardFocusWithin || SearchBox.Text.Length > 0))
        {
            SearchBox.Clear();
            GamesList.Focus();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None || !GamesList.IsKeyboardFocusWithin)
        {
            return;
        }

        if (e.Key == Key.Enter && GamesList.SelectedItem is GameEntry)
        {
            Launch_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && GamesList.SelectedItem is GameEntry)
        {
            Remove_Click(this, new RoutedEventArgs());
            e.Handled = true;
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

    private static string? ResolveInstallDirectory(GameEntry game)
    {
        try
        {
            var candidate = string.Equals(game.LaunchMode, "Steam", StringComparison.OrdinalIgnoreCase)
                ? game.SteamInstallDirectory
                : string.IsNullOrWhiteSpace(game.WorkingDirectory)
                    ? Path.GetDirectoryName(game.ExecutablePath)
                    : game.WorkingDirectory;

            if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(candidate);
            if (!Directory.Exists(fullPath))
            {
                return null;
            }

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            return fullPath;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
                                           UnauthorizedAccessException or NotSupportedException or SecurityException)
        {
            return null;
        }
    }

    private void OpenInstallFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry selected)
        {
            return;
        }

        var installDirectory = ResolveInstallDirectory(selected);
        if (installDirectory is null)
        {
            UpdateDetails(selected);
            SetStatus("The install folder is no longer available");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installDirectory,
                UseShellExecute = true
            });
            SetStatus($"Opened install folder for {selected.Name}");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or
                                           UnauthorizedAccessException or IOException)
        {
            MessageBox.Show(this, $"Windows could not open that folder.\n\n{exception.Message}",
                "Open folder failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("Unable to open install folder");
        }
    }

    private void UpdateDetails(GameEntry game)
    {
        DetailInitials.Text = game.Initials;
        DetailName.Text = game.Name;
        DetailCategory.Text = game.Category;
        DetailPath.Text = game.LaunchTargetDescription;
        DetailLastLaunched.Text = game.LastLaunchedUtc?.ToLocalTime().ToString("g") ?? "Not launched yet";
        ToggleFavoriteButton.Content = game.IsFavorite ? "★   Remove from favorites" : "☆   Add to favorites";
        var installDirectory = ResolveInstallDirectory(game);
        DetailInstallDirectory.Text = installDirectory ?? string.Empty;
        InstallLocationPanel.Visibility = installDirectory is null ? Visibility.Collapsed : Visibility.Visible;
        OpenInstallFolderButton.IsEnabled = installDirectory is not null;
        LaunchButton.IsEnabled = true;
        EditButton.IsEnabled = true;
        RemoveButton.IsEnabled = true;
        ToggleFavoriteButton.IsEnabled = true;
    }

    private void ClearSelection()
    {
        DetailInitials.Text = "—";
        DetailName.Text = "Select a game";
        DetailCategory.Text = "No game selected";
        DetailPath.Text = "Choose a game from your library to see its launch target.";
        DetailLastLaunched.Text = "Not launched yet";
        DetailInstallDirectory.Text = string.Empty;
        InstallLocationPanel.Visibility = Visibility.Collapsed;
        OpenInstallFolderButton.IsEnabled = false;
        LaunchButton.IsEnabled = false;
        EditButton.IsEnabled = false;
        RemoveButton.IsEnabled = false;
        ToggleFavoriteButton.IsEnabled = false;
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
