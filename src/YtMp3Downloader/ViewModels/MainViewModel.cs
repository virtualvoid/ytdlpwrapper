using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtMp3Downloader.Models;
using YtMp3Downloader.Services;

namespace YtMp3Downloader.ViewModels;

public sealed class QueueFile
{
    public List<SavedPlaylist> Playlists { get; set; } = new();
}

public sealed class SavedPlaylist
{
    public PlaylistInfo Info { get; set; } = new("", "", null, null, [], false);
    public List<SavedEntry> Entries { get; set; } = new();
}

public sealed class SavedEntry
{
    public string Id { get; set; } = "";
    public bool Selected { get; set; } = true;
    public DownloadState State { get; set; }
}

public partial class MainViewModel : ObservableObject
{
    const int MaxLogLines = 300;

    readonly ToolsService _tools = new();
    readonly YtDlpService _yt;
    readonly Queue<string> _logLines = new();
    CancellationTokenSource? _runCts;
    readonly CancellationTokenSource _lifetimeCts = new();

    public ToolsService Tools => _tools;
    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<PlaylistViewModel> Playlists { get; } = new();

    [ObservableProperty] string urlText = "";
    [ObservableProperty] string statusText = "Pripravené.";
    [ObservableProperty] double overallProgress;
    [ObservableProperty] bool isLogVisible;
    [ObservableProperty] string logText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    bool isRunning;

    [ObservableProperty] bool isAdding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowToolsBanner))]
    bool toolsReady;

    [ObservableProperty] bool toolsBusy;
    [ObservableProperty] string toolsMessage = "";
    [ObservableProperty] double toolsProgress;

    public bool ShowToolsBanner => !ToolsReady;
    public bool CanEdit => !IsRunning;

    public MainViewModel()
    {
        _yt = new YtDlpService(_tools);
        Playlists.CollectionChanged += (_, _) => RefreshAggregates();
    }

    public async Task InitializeAsync()
    {
        Settings = JsonStore.LoadOrDefault<AppSettings>(AppPaths.SettingsFile);
        LoadQueue();
        UpdateToolsState();
        await Task.CompletedTask;
    }

    void UpdateToolsState()
    {
        ToolsReady = _tools.AllAvailable;
        if (!ToolsReady)
        {
            var mb = _tools.Missing.Sum(m => m switch { "ffmpeg" => 175, "deno" => 45, _ => 20 });
            ToolsMessage = $"Pri prvom spustení je potrebné stiahnuť nástroje: {string.Join(", ", _tools.Missing)} " +
                           $"(spolu približne {mb} MB, z GitHubu). Uložia sa do {_tools.ToolsDir}.";
        }

        RefreshCommands();
    }

    public void ApplySettings(AppSettings s)
    {
        Settings = s;
        JsonStore.Save(AppPaths.SettingsFile, s);
    }

    // ---------------------------------------------------------------- nástroje

    [RelayCommand(CanExecute = nameof(CanSetupTools))]
    async Task SetupToolsAsync()
    {
        ToolsBusy = true;
        ToolsProgress = 0;
        try
        {
            var progress = new Progress<ToolProgress>(p =>
            {
                ToolsMessage = p.Message;
                ToolsProgress = p.Percent;
            });
            await _tools.EnsureInstalledAsync(progress, _lifetimeCts.Token);
            StatusText = "Nástroje sú pripravené.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log($"Sťahovanie nástrojov zlyhalo: {ex}");
            ToolsMessage = $"Sťahovanie zlyhalo: {ex.Message} Skúste to znova.";
        }
        finally
        {
            ToolsBusy = false;
            if (_tools.AllAvailable)
                UpdateToolsState();
            else
                RefreshCommands();
        }
    }

    bool CanSetupTools() => !ToolsBusy;

    partial void OnToolsBusyChanged(bool value) => SetupToolsCommand.NotifyCanExecuteChanged();

    // ----------------------------------------------------------------- pridanie

    [RelayCommand(CanExecute = nameof(CanAdd))]
    async Task AddAsync()
    {
        var urls = UrlText
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
                        uri.Scheme is "http" or "https")
            .Distinct()
            .ToList();

        if (urls.Count == 0)
        {
            StatusText = "Zadajte platnú URL (http/https).";
            return;
        }

        IsAdding = true;
        try
        {
            foreach (var url in urls)
            {
                if (Playlists.Any(p => p.Info.Url == url))
                {
                    StatusText = "Táto URL už v zozname je.";
                    continue;
                }

                StatusText = $"Načítavam náhľad: {url}";
                var info = await _yt.GetPreviewAsync(url, Settings, _lifetimeCts.Token);
                var vm = new PlaylistViewModel(info);
                Hook(vm);
                Playlists.Add(vm);
                StatusText = $"Pridané: {info.Title}";
            }

            UrlText = "";
            SaveQueue();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log($"Náhľad zlyhal: {ex.Message}");
            StatusText = $"Náhľad zlyhal: {ex.Message}";
        }
        finally
        {
            IsAdding = false;
        }
    }

    bool CanAdd() => !IsAdding && ToolsReady && !string.IsNullOrWhiteSpace(UrlText);

    partial void OnUrlTextChanged(string value) => AddCommand.NotifyCanExecuteChanged();
    partial void OnIsAddingChanged(bool value) => AddCommand.NotifyCanExecuteChanged();
    partial void OnToolsReadyChanged(bool value) => RefreshCommands();

    [RelayCommand]
    void Paste()
    {
        try
        {
            if (Clipboard.ContainsText())
                UrlText = Clipboard.GetText().Trim();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // schránka je práve obsadená
        }
    }

    // ------------------------------------------------------------ sťahovanie

    [RelayCommand(CanExecute = nameof(CanStart))]
    async Task StartAsync()
    {
        IsRunning = true;
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;
        EntryViewModel? current = null;
        int done = 0, failed = 0, skipped = 0;

        try
        {
            for (var i = 0; i < Playlists.Count; i++)
            {
                var pl = Playlists[i];
                var folder = Path.Combine(Settings.OutputDirectory, pl.FolderName);

                foreach (var entry in pl.Entries.ToList())
                {
                    if (!entry.IsSelected || entry.State is not (DownloadState.Pending or DownloadState.Cancelled))
                        continue;

                    ct.ThrowIfCancellationRequested();
                    current = entry;
                    entry.Reset();
                    StatusText = $"{pl.Title}: {entry.Title}";
                    var progress = new Progress<DownloadProgress>(entry.Apply);

                    DownloadResult result;
                    try
                    {
                        result = await _yt.DownloadAsync(entry.Info, pl.Info, folder, Settings, progress, Log, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result = new DownloadResult(DownloadState.Failed, ex.Message);
                    }

                    // Progress<T> doručuje správy asynchrónne – finálny stav nastavíme až po nich.
                    await Task.Yield();
                    entry.Apply(new DownloadProgress(result.State, 100, result.Error));
                    if (result.Error is not null)
                        Log($"Chyba [{entry.Title}]: {result.Error}");

                    switch (result.State)
                    {
                        case DownloadState.Done: done++; break;
                        case DownloadState.Skipped: skipped++; break;
                        case DownloadState.Failed: failed++; break;
                    }

                    current = null;
                    SaveQueue();
                }
            }

            StatusText = $"Hotovo: {done} stiahnuté, {skipped} preskočené, {failed} s chybou.";
            SystemSounds.Asterisk.Play();
        }
        catch (OperationCanceledException)
        {
            current?.Apply(new DownloadProgress(DownloadState.Cancelled));
            StatusText = "Zastavené.";
        }
        finally
        {
            IsRunning = false;
            _runCts.Dispose();
            _runCts = null;
            SaveQueue();
            RefreshCommands();
        }
    }

    bool CanStart() => !IsRunning && ToolsReady && Playlists.Any(p => p.HasPending);

    partial void OnIsRunningChanged(bool value) => RefreshCommands();

    [RelayCommand(CanExecute = nameof(IsRunning))]
    void Stop() => _runCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRetry))]
    async Task RetryFailedAsync()
    {
        foreach (var e in Playlists.SelectMany(p => p.Entries).Where(e => e.State == DownloadState.Failed))
            e.Reset();
        await StartAsync();
    }

    bool CanRetry() => !IsRunning && ToolsReady && Playlists.Any(p => p.HasFailed);

    // ---------------------------------------------------------------- zoznam

    [RelayCommand(CanExecute = nameof(CanEdit))]
    void Remove(PlaylistViewModel? playlist)
    {
        if (playlist is null)
            return;
        playlist.PropertyChanged -= OnPlaylistChanged;
        Playlists.Remove(playlist);
        SaveQueue();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    void ClearFinished()
    {
        foreach (var p in Playlists.Where(p => p.IsFinished).ToList())
        {
            p.PropertyChanged -= OnPlaylistChanged;
            Playlists.Remove(p);
        }

        SaveQueue();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    void ClearAll()
    {
        foreach (var p in Playlists)
            p.PropertyChanged -= OnPlaylistChanged;
        Playlists.Clear();
        SaveQueue();
    }

    [RelayCommand(CanExecute = nameof(CanUpdateYtDlp))]
    async Task UpdateYtDlpAsync()
    {
        StatusText = "Aktualizujem yt-dlp…";
        try
        {
            var output = await _tools.UpdateYtDlpAsync(_lifetimeCts.Token);
            Log(output);
            StatusText = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault() ?? "yt-dlp je aktuálny.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log($"Aktualizácia yt-dlp zlyhala: {ex.Message}");
            StatusText = $"Aktualizácia yt-dlp zlyhala: {ex.Message}";
        }
    }

    bool CanUpdateYtDlp() => !IsRunning && ToolsReady;

    [RelayCommand]
    void OpenFolder(PlaylistViewModel? playlist)
    {
        var dir = playlist is null
            ? Settings.OutputDirectory
            : Path.Combine(Settings.OutputDirectory, playlist.FolderName);
        if (!Directory.Exists(dir))
            dir = Settings.OutputDirectory;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Priečinok sa nepodarilo otvoriť: {ex.Message}";
        }
    }

    // ------------------------------------------------------------ pomocné

    void Hook(PlaylistViewModel vm) => vm.PropertyChanged += OnPlaylistChanged;

    void OnPlaylistChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlaylistViewModel.OverallProgress)
            or nameof(PlaylistViewModel.HasPending)
            or nameof(PlaylistViewModel.HasFailed))
            RefreshAggregates();
    }

    void RefreshAggregates()
    {
        var selected = Playlists.SelectMany(p => p.Entries).Where(e => e.IsSelected).ToList();
        OverallProgress = selected.Count == 0 ? 0 : selected.Average(e => e.Percent);
        RefreshCommands();
    }

    void RefreshCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RetryFailedCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ClearFinishedCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
        UpdateYtDlpCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
    }

    void Log(string line)
    {
        void Append()
        {
            _logLines.Enqueue($"{DateTime.Now:HH:mm:ss}  {line}");
            while (_logLines.Count > MaxLogLines)
                _logLines.Dequeue();
            LogText = string.Join(Environment.NewLine, _logLines);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Append();
        else
            dispatcher.BeginInvoke(Append);

        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------ perzistencia

    void LoadQueue()
    {
        var file = JsonStore.LoadOrDefault<QueueFile>(AppPaths.QueueFile);
        foreach (var saved in file.Playlists)
        {
            if (saved.Info.Entries.Count == 0)
                continue;
            var vm = new PlaylistViewModel(saved.Info);
            foreach (var e in vm.Entries)
            {
                var s = saved.Entries.FirstOrDefault(x => x.Id == e.Info.Id);
                if (s is null)
                    continue;
                e.IsSelected = s.Selected;
                // Rozpracované položky sa začnú odznova.
                var state = s.State is DownloadState.Downloading or DownloadState.Converting
                    ? DownloadState.Pending
                    : s.State;
                e.Apply(new DownloadProgress(state, 100));
                if (state is DownloadState.Pending or DownloadState.Cancelled or DownloadState.Failed)
                    e.Percent = 0;
            }

            Hook(vm);
            Playlists.Add(vm);
            vm.Refresh();
        }
    }

    public void SaveQueue()
    {
        try
        {
            var file = new QueueFile
            {
                Playlists = Playlists.Select(p => new SavedPlaylist
                {
                    Info = p.Info,
                    Entries = p.Entries.Select(e => new SavedEntry
                    {
                        Id = e.Info.Id,
                        Selected = e.IsSelected,
                        State = e.State,
                    }).ToList(),
                }).ToList(),
            };
            JsonStore.Save(AppPaths.QueueFile, file);
        }
        catch (IOException ex)
        {
            Log($"Uloženie fronty zlyhalo: {ex.Message}");
        }
    }

    public void Shutdown()
    {
        _runCts?.Cancel();
        _lifetimeCts.Cancel();
        SaveQueue();
    }
}
