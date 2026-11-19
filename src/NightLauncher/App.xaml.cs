using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace NightLauncher;

public partial class App : Application
{
    private const long MaximumLogBytes = 2 * 1024 * 1024;
    private const int RetainedLogFiles = 3;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NightLauncher",
        "logs");

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
            var folder = LogDirectory;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "latest.log");

            if (File.Exists(path) && new FileInfo(path).Length >= MaximumLogBytes)
            {
                for (var index = RetainedLogFiles; index >= 1; index--)
                {
                    var source = index == 1 ? path : Path.Combine(folder, $"latest.{index - 1}.log");
                    var destination = Path.Combine(folder, $"latest.{index}.log");
                    if (File.Exists(source))
                    {
                        File.Move(source, destination, overwrite: true);
                    }
                }
            }

            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never cause a second application failure.
        }
    }
}
