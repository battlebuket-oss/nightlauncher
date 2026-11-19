using System.Windows;
using NightLauncher.Models;

namespace NightLauncher.Views;

public partial class DiscoveredGamesWindow : Window
{
    public DiscoveredGamesWindow(IReadOnlyList<DiscoveredGameExecutable> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        InitializeComponent();
        CandidatesListBox.ItemsSource = candidates;
        CandidateCountText.Text = candidates.Count == 1
            ? "1 executable found. Select the game you want to add."
            : $"{candidates.Count} executables found. Select the games you want to add.";
    }

    public IReadOnlyList<DiscoveredGameExecutable> SelectedExecutables { get; private set; }
        = Array.Empty<DiscoveredGameExecutable>();

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = CandidatesListBox.SelectedItems
            .Cast<DiscoveredGameExecutable>()
            .ToArray();

        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Select at least one executable first.",
                "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedExecutables = selected;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
