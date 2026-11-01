using System.Windows;
using YtMp3Downloader.ViewModels;

namespace YtMp3Downloader;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadToolsInfoAsync();
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        var vm = (SettingsViewModel)DataContext;
        if (string.IsNullOrWhiteSpace(vm.OutputDirectory))
        {
            MessageBox.Show(this, "Zadajte výstupný priečinok.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
