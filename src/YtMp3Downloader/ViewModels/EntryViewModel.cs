using CommunityToolkit.Mvvm.ComponentModel;
using YtMp3Downloader.Models;

namespace YtMp3Downloader.ViewModels;

public partial class EntryViewModel(EntryInfo info) : ObservableObject
{
    public EntryInfo Info { get; } = info;

    public string Title => Info.Title;
    public string IndexText => Info.Index.ToString("000");

    [ObservableProperty]
    bool isSelected = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsActive), nameof(IsFailed))]
    DownloadState state = DownloadState.Pending;

    /// <summary>Celkový postup 0–100 (sťahovanie tvorí 90 %, konverzia zvyšok).</summary>
    [ObservableProperty]
    double percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    string? message;

    public bool IsActive => State is DownloadState.Downloading or DownloadState.Converting;
    public bool IsFailed => State == DownloadState.Failed;

    public string StatusText => State switch
    {
        DownloadState.Pending => "Čaká",
        DownloadState.Downloading => $"Sťahujem {Percent / 0.9:0} %",
        DownloadState.Converting => "Konvertujem na MP3…",
        DownloadState.Done => "Hotovo",
        DownloadState.Skipped => "Preskočené (už stiahnuté)",
        DownloadState.Failed => string.IsNullOrEmpty(Message) ? "Chyba" : $"Chyba: {Message}",
        DownloadState.Cancelled => "Zrušené",
        _ => "",
    };

    partial void OnPercentChanged(double value)
    {
        if (State == DownloadState.Downloading)
            OnPropertyChanged(nameof(StatusText));
    }

    public void Apply(DownloadProgress p)
    {
        State = p.State;
        Percent = p.State switch
        {
            DownloadState.Downloading => Math.Min(p.Percent, 100) * 0.9,
            DownloadState.Converting => 95,
            DownloadState.Done or DownloadState.Skipped => 100,
            _ => Percent,
        };
        Message = p.Message;
    }

    public void Reset()
    {
        State = DownloadState.Pending;
        Percent = 0;
        Message = null;
    }
}
