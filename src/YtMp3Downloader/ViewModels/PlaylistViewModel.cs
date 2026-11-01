using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YtMp3Downloader.Models;
using YtMp3Downloader.Services;

namespace YtMp3Downloader.ViewModels;

public partial class PlaylistViewModel : ObservableObject
{
    public PlaylistInfo Info { get; }
    public ObservableCollection<EntryViewModel> Entries { get; } = new();

    [ObservableProperty]
    bool isExpanded;

    public PlaylistViewModel(PlaylistInfo info)
    {
        Info = info;
        foreach (var e in info.Entries)
        {
            var vm = new EntryViewModel(e);
            vm.PropertyChanged += OnEntryChanged;
            Entries.Add(vm);
        }
    }

    public string Title => Info.Title;
    public string? ThumbnailUrl => Info.ThumbnailUrl;
    public string FolderName => FileNames.Sanitize(Info.Title);

    public string Subtitle
    {
        get
        {
            var n = Entries.Count;
            var word = n == 1 ? "skladba" : n is >= 2 and <= 4 ? "skladby" : "skladieb";
            var count = Info.IsPlaylist ? $"{n} {word}" : "1 video";
            return string.IsNullOrEmpty(Info.Uploader) ? count : $"{Info.Uploader} · {count}";
        }
    }

    IEnumerable<EntryViewModel> Selected => Entries.Where(e => e.IsSelected);

    public double OverallProgress => Selected.Any() ? Selected.Average(e => e.Percent) : 0;

    public bool HasPending => Selected.Any(e => e.State is DownloadState.Pending or DownloadState.Cancelled);
    public bool HasFailed => Selected.Any(e => e.State == DownloadState.Failed);
    public bool IsFinished => Selected.Any() && Selected.All(e => e.State is DownloadState.Done or DownloadState.Skipped);

    public string SummaryText
    {
        get
        {
            var sel = Selected.ToList();
            var done = sel.Count(e => e.State is DownloadState.Done or DownloadState.Skipped);
            var failed = sel.Count(e => e.State == DownloadState.Failed);
            var text = $"{done} / {sel.Count} hotovo";
            return failed > 0 ? $"{text} · {failed} s chybou" : text;
        }
    }

    void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EntryViewModel.Percent):
                OnPropertyChanged(nameof(OverallProgress));
                break;
            case nameof(EntryViewModel.State):
            case nameof(EntryViewModel.IsSelected):
                Refresh();
                break;
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(OverallProgress));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasFailed));
        OnPropertyChanged(nameof(IsFinished));
    }
}
