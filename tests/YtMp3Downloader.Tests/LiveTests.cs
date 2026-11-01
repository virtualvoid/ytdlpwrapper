using YtMp3Downloader.Models;
using YtMp3Downloader.Services;

namespace YtMp3Downloader.Tests;

/// <summary>
/// Testy proti reálnemu YouTube a nástrojom. Spúšťajú sa iba ak je nastavené YTMP3_LIVE_URL
/// (a voliteľne YTMP3_TOOLS_DIR s existujúcimi nástrojmi, inak sa stiahnu do dočasného priečinka).
/// </summary>
public class LiveTests
{
    [Fact]
    public async Task Preview_And_Download_FirstEntry()
    {
        var url = Environment.GetEnvironmentVariable("YTMP3_LIVE_URL");
        if (string.IsNullOrEmpty(url))
            return;

        var toolsDir = Environment.GetEnvironmentVariable("YTMP3_TOOLS_DIR")
                       ?? Path.Combine(Path.GetTempPath(), "ytmp3-live-tools");
        var outDir = Path.Combine(Path.GetTempPath(), "ytmp3-live-out-" + Guid.NewGuid().ToString("N")[..6]);

        var tools = new ToolsService(toolsDir);
        if (!tools.AllAvailable)
            await tools.EnsureInstalledAsync(new Progress<ToolProgress>(), CancellationToken.None);
        Assert.True(tools.AllAvailable);

        var yt = new YtDlpService(tools);
        var settings = new AppSettings { OutputDirectory = outDir, MaxItems = 2 };

        var info = await yt.GetPreviewAsync(url, settings, CancellationToken.None);
        Assert.NotEmpty(info.Entries);
        Assert.False(string.IsNullOrEmpty(info.ThumbnailUrl));

        var folder = Path.Combine(outDir, FileNames.Sanitize(info.Title));
        var states = new List<DownloadState>();
        var progress = new SyncProgress(p => states.Add(p.State));
        var log = new List<string>();

        var result = await yt.DownloadAsync(info.Entries[0], info, folder, settings, progress, log.Add,
            CancellationToken.None);
        Assert.True(result.State == DownloadState.Done, result.Error + "\n" + string.Join("\n", log));
        Assert.Single(Directory.GetFiles(folder, "*.mp3"));
        Assert.Contains(DownloadState.Converting, states);

        // Druhé spustenie musí byť preskočené cez archív.
        var again = await yt.DownloadAsync(info.Entries[0], info, folder, settings, progress, log.Add,
            CancellationToken.None);
        Assert.Equal(DownloadState.Skipped, again.State);
    }

    sealed class SyncProgress(Action<DownloadProgress> a) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => a(value);
    }
}

public class LiveDownloadTests
{
    /// <summary>Stiahne a overí iba chýbajúce nástroje. YTMP3_LIVE_DL_PATH sa pridá do PATH (ffmpeg, deno už existujú).</summary>
    [Fact]
    public async Task EnsureInstalled_DownloadsAndVerifiesMissingTools()
    {
        var extra = Environment.GetEnvironmentVariable("YTMP3_LIVE_DL_PATH");
        if (string.IsNullOrEmpty(extra))
            return;

        // "ISOLATE" = PATH bez ffmpeg/deno, takže sa stiahne všetko; inak sa extra cesty iba pridajú.
        var path = extra == "ISOLATE"
            ? Environment.SystemDirectory
            : Environment.GetEnvironmentVariable("PATH") + ";" + extra;
        Environment.SetEnvironmentVariable("PATH", path);
        var dir = Path.Combine(Path.GetTempPath(), "ytmp3-dl-" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            var tools = new ToolsService(dir);
            await tools.EnsureInstalledAsync(new Progress<ToolProgress>(), CancellationToken.None);

            Assert.True(tools.AllAvailable);
            Assert.True(File.Exists(Path.Combine(dir, "yt-dlp.exe")));
            Assert.Empty(Directory.GetFiles(dir, "*.part"));
            Assert.Matches(@"^\d{4}\.\d{2}\.\d{2}", await tools.GetYtDlpVersionAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
