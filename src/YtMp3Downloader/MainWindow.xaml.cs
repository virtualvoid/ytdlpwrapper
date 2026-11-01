using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YtMp3Downloader.ViewModels;

namespace YtMp3Downloader;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    void OnSettingsExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var settingsVm = new SettingsViewModel(_vm.Settings, _vm.Tools);
        var dlg = new SettingsWindow(settingsVm) { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.ApplySettings(settingsVm.ToSettings());
    }

    void OnAboutExecuted(object sender, ExecutedRoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    void OnExitExecuted(object sender, ExecutedRoutedEventArgs e) => Close();

    /// <summary>Panel je zamknutý a položky sa vždy zmestia, takže overflow šípku skryjeme.</summary>
    void OnToolBarLoaded(object sender, RoutedEventArgs e)
    {
        var bar = (ToolBar)sender;
        if (bar.Template.FindName("OverflowGrid", bar) is FrameworkElement overflow)
            overflow.Visibility = Visibility.Collapsed;
        if (bar.Template.FindName("MainPanelBorder", bar) is FrameworkElement border)
            border.Margin = new Thickness(0);
    }

    void OnLogTextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsRunning)
        {
            var answer = MessageBox.Show(this, "Sťahovanie ešte beží. Naozaj ho zastaviť a ukončiť aplikáciu?",
                "YtMp3 Downloader", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _vm.Shutdown();
        base.OnClosing(e);
    }
}
