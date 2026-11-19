using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NightLauncher.Models;
using NightLauncher.Services;

namespace NightLauncher.Views;

public partial class SettingsWindow : Window
{
    private readonly string _libraryPath;
    private readonly NightLauncherSettingsService _settings;
    private readonly DiagnosticsService _diagnostics = new();
    private readonly ObservableCollection<BackupFileViewModel> _backupItems = [];
    private GameLibraryBackupService _backups;

    public SettingsWindow(
        string libraryPath,
        GameLibraryBackupService backups,
        NightLauncherSettingsService settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        _libraryPath = libraryPath;
        _backups = backups;
        _settings = settings;

        LibraryPathText.Text = libraryPath;
        BackupFolderText.Text = backups.BackupDirectory;
        BackupListBox.ItemsSource = _backupItems;
        RefreshBackupStatus();
    }

    public bool LibraryRestored { get; private set; }

    public string SelectedBackupDirectory => _backups.BackupDirectory;

    public bool BackupDirectoryChanged { get; private set; }

    private void RefreshBackupStatus()
    {
        try
        {
            _backupItems.Clear();
            var backupFiles = _backups.GetBackups();
            foreach (var path in backupFiles)
            {
                var created = File.GetLastWriteTimeUtc(path);
                _backupItems.Add(new BackupFileViewModel(
                    path,
                    Path.GetFileName(path),
                    created.ToLocalTime().ToString("g")));
            }

            BackupStatusText.Text = backupFiles.Count switch
            {
                0 => "No backups found in the selected folder.",
                1 => "1 recovery point available. The newest point is selected.",
                _ => $"{backupFiles.Count} recovery points available. Select one to restore."
            };

            BackupListBox.SelectedIndex = backupFiles.Count > 0 ? 0 : -1;
            RestoreBackupButton.IsEnabled = BackupListBox.SelectedItem is BackupFileViewModel;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            BackupStatusText.Text = $"Unable to read backups: {exception.Message}";
            RestoreBackupButton.IsEnabled = false;
        }
    }

    private void BackupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RestoreBackupButton.IsEnabled = BackupListBox.SelectedItem is BackupFileViewModel;
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_libraryPath);
            MessageBox.Show(this, "Library path copied.", "NightLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "Windows could not access the clipboard.", "NightLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SelectBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where NightLauncher stores recovery backups",
            Multiselect = false,
            InitialDirectory = Directory.Exists(_backups.BackupDirectory)
                ? _backups.BackupDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var selectedDirectory = Path.GetFullPath(dialog.FolderName);
            Directory.CreateDirectory(selectedDirectory);

            // Verify the selected destination is writable before persisting the preference.
            var probePath = Path.Combine(selectedDirectory, $".nightlauncher-write-test-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
            File.Delete(probePath);

            _settings.SaveBackupDirectory(selectedDirectory);
            _backups = new GameLibraryBackupService(
                new GameLibraryService(_libraryPath),
                selectedDirectory);
            BackupDirectoryChanged = true;
            BackupFolderText.Text = _backups.BackupDirectory;
            RefreshBackupStatus();

            MessageBox.Show(this,
                "Backup location updated. Existing backups in the previous folder have not been moved.",
                "Backup location changed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            MessageBox.Show(this, $"NightLauncher could not use that backup folder. The previous location is unchanged.\n\n{exception.Message}",
                "Backup folder unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var backupPath = _backups.CreateBackup();
            RefreshBackupStatus();
            MessageBox.Show(this, $"Backup created successfully:\n\n{backupPath}",
                "Backup complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, $"NightLauncher could not create a backup.\n\n{exception.Message}",
                "Backup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupListBox.SelectedItem is not BackupFileViewModel selected)
        {
            MessageBox.Show(this, "Select a recovery point first.",
                "No backup selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"Restore this recovery point?\n\n{selected.DisplayName}\nCreated: {selected.CreatedLabel}\n\nYour current library will first be backed up. Restored launch targets will require a one-time trust confirmation.",
            "Restore selected backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _backups.RestoreBackup(selected.FullPath);
            LibraryRestored = true;
            RefreshBackupStatus();
            MessageBox.Show(this, $"Library restored from:\n\n{selected.FullPath}",
                "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, $"NightLauncher could not restore the selected backup. Your existing library was not replaced.\n\n{exception.Message}",
                "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_backups.BackupDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _backups.BackupDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Windows could not open the backup folder.\n\n{exception.Message}",
                "NightLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_diagnostics.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _diagnostics.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Windows could not open the log folder.\n\n{exception.Message}",
                "NightLauncher diagnostics", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export NightLauncher diagnostics",
            Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"NightLauncher-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = _diagnostics.Export(dialog.FileName);
            MessageBox.Show(this,
                $"Diagnostics bundle exported to:\n\n{result.ArchivePath}\n\nLog files included: {result.LogFilesIncluded}. Review the ZIP before sharing it; log content may contain local file paths.",
                "Diagnostics exported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, $"NightLauncher could not export diagnostics.\n\n{exception.Message}",
                "Diagnostics export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
