using System.Globalization;
using System.Text.Json;
using YtMp3Downloader.Models;

namespace YtMp3Downloader.Services;

public enum YtDlpLineKind
{
    Other,
    Progress,
    Converting,
    AlreadyDone,
    Error,
}

public sealed record YtDlpLine(YtDlpLineKind Kind, double Percent = 0, string Text = "");

public static class YtDlpParser
{
    public const string ProgressPrefix = "YTP|";

    /// <summary>Šablóna pre --progress-template (typ "download").</summary>
    public const string ProgressTemplate =
        "download:" + ProgressPrefix +
        "%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s";

    public static YtDlpLine ParseLine(string line)
    {
        if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
        {
            var parts = line.Split('|');
            var status = parts.Length > 1 ? parts[1] : "";
            var downloaded = Num(parts, 2);
            var total = Num(parts, 3) ?? Num(parts, 4);

            double percent = 0;
            if (downloaded is not null && total is > 0)
                percent = Math.Clamp(downloaded.Value / total.Value * 100, 0, 100);
            if (status == "finished")
                percent = 100;
            return new YtDlpLine(YtDlpLineKind.Progress, percent, line);
        }

        if (line.StartsWith("[ExtractAudio]", StringComparison.Ordinal))
            return new YtDlpLine(YtDlpLineKind.Converting, 100, line);

        if (line.Contains("has already been recorded in the archive", StringComparison.Ordinal) ||
            line.Contains("has already been downloaded", StringComparison.Ordinal))
            return new YtDlpLine(YtDlpLineKind.AlreadyDone, 100, line);

        if (line.StartsWith("ERROR:", StringComparison.Ordinal))
            return new YtDlpLine(YtDlpLineKind.Error, 0, line["ERROR:".Length..].Trim());

        return new YtDlpLine(YtDlpLineKind.Other, 0, line);
    }

    static double? Num(string[] parts, int i) =>
        i < parts.Length && double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    public static PlaylistInfo ParsePreview(string json, string sourceUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var isYoutube = IsYoutube(root, sourceUrl);

        if (!root.TryGetProperty("entries", out var entriesEl) || entriesEl.ValueKind != JsonValueKind.Array)
        {
            // Jedno video.
            var id = Str(root, "id") ?? "";
            var title = Str(root, "title") ?? id;
            var url = Str(root, "webpage_url") ?? sourceUrl;
            var entry = new EntryInfo(id, title, url, 1, Dbl(root, "duration"));
            var thumb = Thumb(root, id, isYoutube) ?? Str(root, "thumbnail");
            return new PlaylistInfo(sourceUrl, title, Str(root, "uploader") ?? Str(root, "channel"), thumb,
                [entry], false);
        }

        var entries = new List<EntryInfo>();
        string? firstThumb = null;
        var index = 0;
        foreach (var e in entriesEl.EnumerateArray())
        {
            index++;
            if (e.ValueKind != JsonValueKind.Object)
                continue;
            var id = Str(e, "id");
            if (string.IsNullOrEmpty(id))
                continue;

            var url = Str(e, "url");
            if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = isYoutube ? $"https://www.youtube.com/watch?v={id}" : Str(e, "webpage_url") ?? url ?? sourceUrl;

            entries.Add(new EntryInfo(id, Str(e, "title") ?? id, url, index, Dbl(e, "duration")));
            firstThumb ??= Thumb(e, id, isYoutube);
        }

        var playlistTitle = Str(root, "title") ?? "Playlist";
        var uploader = Str(root, "uploader") ?? Str(root, "channel");
        return new PlaylistInfo(sourceUrl, playlistTitle, uploader, firstThumb, entries, true);
    }

    static bool IsYoutube(JsonElement root, string sourceUrl)
    {
        var key = Str(root, "extractor_key") ?? "";
        return key.StartsWith("Youtube", StringComparison.OrdinalIgnoreCase) ||
               sourceUrl.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               sourceUrl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
    }

    static string? Thumb(JsonElement el, string id, bool isYoutube)
    {
        if (isYoutube && !string.IsNullOrEmpty(id))
            return $"https://i.ytimg.com/vi/{id}/mqdefault.jpg";

        if (el.TryGetProperty("thumbnails", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            string? last = null;
            foreach (var t in arr.EnumerateArray())
                last = Str(t, "url") ?? last;
            return last;
        }

        return Str(el, "thumbnail");
    }

    static string? Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    static double? Dbl(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
