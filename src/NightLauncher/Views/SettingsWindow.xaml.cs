using System.Diagnostics;
using System.IO;
using System.Windows;

using NightLauncher.Services;

namespace NightLauncher.Views;

public partial class SettingsWindow : Window
{
    private readonly string _libraryPath;
    private readonly GameLibraryBackupService _backups;

    public SettingsWindow(string libraryPath, GameLibraryBackupService backups)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentNullException.ThrowIfNull(backups);

        InitializeComponent();
        _libraryPath = libraryPath;
        _backups = backups;
        LibraryPathText.Text = libraryPath;
        BackupFolderText.Text = backups.BackupDirectory;
        RefreshBackupStatus();
    }

    public bool LibraryRestored { get; private set; }

    private void RefreshBackupStatus()
    {
        try
        {
            var backupFiles = _backups.GetBackups();
            BackupStatusText.Text = backupFiles.Count switch
            {
                0 => "No backups found.",
                1 => "1 backup available. The newest backup is shown first.",
                _ => $"{backupFiles.Count} backups available. The newest backup is shown first."
            };
            RestoreBackupButton.IsEnabled = backupFiles.Count > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackupStatusText.Text = $"Unable to read backups: {exception.Message}";
            RestoreBackupButton.IsEnabled = false;
        }
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
        var answer = MessageBox.Show(this,
            "Restore the newest backup?\n\nYour current library will first be backed up. Restored launch targets will require a one-time trust confirmation.",
            "Restore latest backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var restoredPath = _backups.RestoreLatestBackup();
            LibraryRestored = true;
            RefreshBackupStatus();
            MessageBox.Show(this, $"Library restored from:\n\n{restoredPath}",
                "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, $"NightLauncher could not restore the backup. Your existing library was not replaced.\n\n{exception.Message}",
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Windows could not open the backup folder.\n\n{exception.Message}",
                "NightLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
