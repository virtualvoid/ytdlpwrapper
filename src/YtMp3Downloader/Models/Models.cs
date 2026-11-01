using System.Text.Json.Serialization;

namespace YtMp3Downloader.Models;

public sealed record EntryInfo(string Id, string Title, string Url, int Index, double? DurationSeconds);

public sealed record PlaylistInfo(
    string Url,
    string Title,
    string? Uploader,
    string? ThumbnailUrl,
    IReadOnlyList<EntryInfo> Entries,
    bool IsPlaylist);

public enum DownloadState
{
    Pending,
    Downloading,
    Converting,
    Done,
    Skipped,
    Failed,
    Cancelled,
}

public sealed record DownloadProgress(DownloadState State, double Percent = 0, string? Message = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CookieMode
{
    None,
    File,
    Browser,
}

public sealed class AppSettings
{
    public string OutputDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "YtMp3Downloader");

    public int MaxItems { get; set; } = 50;

    /// <summary>Hodnota pre --audio-quality: "0" (VBR V0) alebo bitrate, napr. "320K".</summary>
    public string AudioQuality { get; set; } = "0";

    public CookieMode CookieMode { get; set; } = CookieMode.None;

    public string? CookieFile { get; set; }

    public string CookieBrowser { get; set; } = "chrome";
}
