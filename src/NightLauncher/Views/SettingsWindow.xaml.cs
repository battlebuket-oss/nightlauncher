using System.Windows;

namespace NightLauncher.Views;

public partial class SettingsWindow : Window
{
    private readonly string _libraryPath;

    public SettingsWindow(string libraryPath)
    {
        InitializeComponent();
        _libraryPath = libraryPath;
        LibraryPathText.Text = libraryPath;
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

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
