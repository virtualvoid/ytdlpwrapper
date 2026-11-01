using System.Text;
using YtMp3Downloader.Models;

namespace YtMp3Downloader.Services;

public sealed class YtDlpException(string message) : Exception(message);

public sealed record DownloadResult(DownloadState State, string? Error = null);

public sealed class YtDlpService(ToolsService tools)
{
    string Exe => tools.YtDlpPath ?? throw new YtDlpException("yt-dlp nie je nainštalovaný.");

    public async Task<PlaylistInfo> GetPreviewAsync(string url, AppSettings settings, CancellationToken ct)
    {
        var args = new List<string>
        {
            "--flat-playlist", "-J", "--yes-playlist", "--no-warnings",
            "--playlist-items", $"1:{Math.Max(1, settings.MaxItems)}",
        };
        args.AddRange(CommonArgs(settings, tools));
        args.Add(url);

        var json = new StringBuilder();
        var err = new StringBuilder();
        var code = await ProcessRunner.RunAsync(Exe, args, (line, isErr) =>
        {
            if (isErr) err.AppendLine(line);
            else json.Append(line);
        }, ct);

        if (code != 0 || json.Length == 0)
            throw new YtDlpException(LastError(err.ToString()) ?? $"yt-dlp skončil s kódom {code}.");

        return YtDlpParser.ParsePreview(json.ToString(), url);
    }

    public async Task<DownloadResult> DownloadAsync(
        EntryInfo entry,
        PlaylistInfo playlist,
        string folder,
        AppSettings settings,
        IProgress<DownloadProgress> progress,
        Action<string> log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(folder);

        var args = BuildDownloadArgs(entry, playlist, folder, settings, tools);
        var already = false;
        string? lastError = null;

        progress.Report(new DownloadProgress(DownloadState.Downloading, 0));
        var code = await ProcessRunner.RunAsync(Exe, args, (line, _) =>
        {
            var parsed = YtDlpParser.ParseLine(line);
            switch (parsed.Kind)
            {
                case YtDlpLineKind.Progress:
                    progress.Report(new DownloadProgress(DownloadState.Downloading, parsed.Percent));
                    return;
                case YtDlpLineKind.Converting:
                    progress.Report(new DownloadProgress(DownloadState.Converting, 100));
                    break;
                case YtDlpLineKind.AlreadyDone:
                    already = true;
                    break;
                case YtDlpLineKind.Error:
                    lastError = parsed.Text;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(line))
                log(line);
        }, ct);

        if (code == 0)
            return new DownloadResult(already ? DownloadState.Skipped : DownloadState.Done);

        return new DownloadResult(DownloadState.Failed, lastError ?? $"yt-dlp skončil s kódom {code}.");
    }

    public static List<string> BuildDownloadArgs(
        EntryInfo entry, PlaylistInfo playlist, string folder, AppSettings settings, ToolsService tools)
    {
        var args = CommonArgs(settings, tools);
        args.AddRange(
        [
            "--no-playlist",
            "--no-overwrites",
            "--download-archive", Path.Combine(folder, "downloaded.txt"),
            "--format", "bestaudio/best",
            "--extract-audio", "--audio-format", "mp3", "--audio-quality", settings.AudioQuality,
            "--embed-metadata", "--embed-thumbnail", "--convert-thumbnails", "jpg",
            "--newline",
            "--progress-template", YtDlpParser.ProgressTemplate,
            // Dĺžku obmedzujeme len na názov (--trim-filenames by skracoval celú cestu vrátane priečinkov).
            "--output", Path.Combine(folder, $"{entry.Index:000} - ").Replace("%", "%%") + "%(title).100s.%(ext)s",
        ]);

        if (playlist.IsPlaylist)
        {
            args.Add("--parse-metadata");
            args.Add($"{EscapeMeta(playlist.Title)}:%(album)s");
            args.Add("--parse-metadata");
            args.Add($"{entry.Index}:%(track_number)s");
        }

        args.Add(entry.Url);
        return args;
    }

    static List<string> CommonArgs(AppSettings settings, ToolsService tools)
    {
        var args = new List<string> { "--ignore-config", "--encoding", "utf-8", "--color", "never" };

        if (tools.FfmpegDir is { } ff)
            args.AddRange(["--ffmpeg-location", ff]);
        if (tools.DenoPath is { } deno)
            args.AddRange(["--js-runtimes", $"deno:{deno}"]);
        args.AddRange(["--remote-components", "ejs:github"]);

        switch (settings.CookieMode)
        {
            case CookieMode.File when File.Exists(settings.CookieFile):
                args.AddRange(["--cookies", settings.CookieFile!]);
                break;
            case CookieMode.Browser when !string.IsNullOrWhiteSpace(settings.CookieBrowser):
                args.AddRange(["--cookies-from-browser", settings.CookieBrowser]);
                break;
        }

        return args;
    }

    /// <summary>Text použitý ako FROM v --parse-metadata: '%' sa zdvojí a ':' sa escapuje.</summary>
    static string EscapeMeta(string s) => s.Replace("%", "%%").Replace(":", "\\:");

    static string? LastError(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var err = lines.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal));
        if (err is not null)
            return err["ERROR:".Length..].Trim();
        return lines.LastOrDefault();
    }
}
