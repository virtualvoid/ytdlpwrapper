using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using YtMp3Downloader.Models;
using YtMp3Downloader.Services;

namespace YtMp3Downloader.ViewModels;

public sealed record QualityOption(string Label, string Value);

public partial class SettingsViewModel : ObservableObject
{
    readonly ToolsService _tools;

    public IReadOnlyList<QualityOption> Qualities { get; } =
    [
        new("Najlepšia (VBR V0)", "0"),
        new("320 kbps", "320K"),
        new("192 kbps", "192K"),
        new("128 kbps", "128K"),
    ];

    public IReadOnlyList<string> Browsers { get; } = ["chrome", "edge", "firefox", "brave", "opera", "vivaldi"];

    [ObservableProperty] string outputDirectory;
    [ObservableProperty] int maxItems;
    [ObservableProperty] string audioQuality;
    [ObservableProperty] bool cookiesNone;
    [ObservableProperty] bool cookiesFile;
    [ObservableProperty] bool cookiesBrowser;
    [ObservableProperty] string cookieFilePath;
    [ObservableProperty] string cookieBrowserName;
    [ObservableProperty] string toolsInfo = "";
    [ObservableProperty] bool isUpdating;

    public SettingsViewModel(AppSettings s, ToolsService tools)
    {
        _tools = tools;
        outputDirectory = s.OutputDirectory;
        maxItems = s.MaxItems;
        audioQuality = s.AudioQuality;
        cookiesNone = s.CookieMode == CookieMode.None;
        cookiesFile = s.CookieMode == CookieMode.File;
        cookiesBrowser = s.CookieMode == CookieMode.Browser;
        cookieFilePath = s.CookieFile ?? "";
        cookieBrowserName = s.CookieBrowser;
    }

    public AppSettings ToSettings() => new()
    {
        OutputDirectory = OutputDirectory.Trim(),
        MaxItems = Math.Clamp(MaxItems, 1, 500),
        AudioQuality = AudioQuality,
        CookieMode = CookiesFile ? CookieMode.File : CookiesBrowser ? CookieMode.Browser : CookieMode.None,
        CookieFile = string.IsNullOrWhiteSpace(CookieFilePath) ? null : CookieFilePath.Trim(),
        CookieBrowser = CookieBrowserName,
    };

    [RelayCommand]
    void BrowseOutput()
    {
        var dlg = new OpenFolderDialog { Title = "Výstupný priečinok", InitialDirectory = OutputDirectory };
        if (dlg.ShowDialog() == true)
            OutputDirectory = dlg.FolderName;
    }

    [RelayCommand]
    void BrowseCookies()
    {
        var dlg = new OpenFileDialog { Title = "cookies.txt", Filter = "Textové súbory|*.txt|Všetky súbory|*.*" };
        if (dlg.ShowDialog() == true)
            CookieFilePath = dlg.FileName;
    }

    public async Task LoadToolsInfoAsync()
    {
        try
        {
            var v = await _tools.GetYtDlpVersionAsync(CancellationToken.None);
            ToolsInfo = $"yt-dlp: {v}\nffmpeg: {_tools.FfmpegPath ?? "chýba"}\ndeno: {_tools.DenoPath ?? "chýba"}";
        }
        catch (Exception ex)
        {
            ToolsInfo = ex.Message;
        }
    }

    [RelayCommand]
    async Task UpdateYtDlpAsync()
    {
        IsUpdating = true;
        ToolsInfo = "Aktualizujem yt-dlp…";
        try
        {
            ToolsInfo = await _tools.UpdateYtDlpAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ToolsInfo = $"Aktualizácia zlyhala: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
