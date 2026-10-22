using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Salli;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("AppDomain: " + args.ExceptionObject);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Dispatcher: " + e.Exception);
        System.Windows.MessageBox.Show(
            "Salli столкнулся с ошибкой:\n\n" + e.Exception.Message +
            "\n\nПодробности записаны в:\n" + LogPath,
            "Salli", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Salli", "error.log");

    private static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
        }
        catch { /* logging must never crash the app */ }
    }
}
