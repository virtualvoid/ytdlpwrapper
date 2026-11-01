using System.Windows;
using System.Windows.Threading;
using YtMp3Downloader.Services;

namespace YtMp3Downloader;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:O}  NEOŠETRENÁ CHYBA: {e.Exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }

        MessageBox.Show($"Nastala neočakávaná chyba:\n{e.Exception.Message}", "YtMp3 Downloader",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
