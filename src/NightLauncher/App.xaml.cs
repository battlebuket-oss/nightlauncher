using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace NightLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog(e.Exception);
        MessageBox.Show(
            "NightLauncher encountered an unexpected error. Details were written to the local log folder.\n\n" +
            e.Exception.Message,
            "NightLauncher error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteLog(exception);
        }
    }

    private static void WriteLog(Exception exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NightLauncher",
                "logs");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "latest.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never cause a second application failure.
        }
    }
}
