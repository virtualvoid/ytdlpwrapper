using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace YtMp3Downloader.Services;

public sealed record ToolProgress(string Message, double Percent);

/// <summary>Vyhľadá, stiahne (s overením SHA-256) a aktualizuje yt-dlp, ffmpeg a deno.</summary>
public sealed class ToolsService
{
    const string YtDlpBase = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
    const string FfmpegBase = "https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/";
    const string FfmpegZip = "ffmpeg-master-latest-win64-lgpl.zip";
    const string DenoBase = "https://github.com/denoland/deno/releases/latest/download/";
    const string DenoZip = "deno-x86_64-pc-windows-msvc.zip";

    static readonly HttpClient Http = CreateClient();

    public string ToolsDir { get; }

    public ToolsService(string? toolsDir = null) => ToolsDir = toolsDir ?? AppPaths.ToolsDir;

    public string? YtDlpPath => Resolve("yt-dlp.exe");
    public string? FfmpegPath => Resolve("ffmpeg.exe");
    public string? FfprobePath => Resolve("ffprobe.exe");
    public string? DenoPath => Resolve("deno.exe");
    public string? FfmpegDir => FfmpegPath is { } p ? Path.GetDirectoryName(p) : null;

    public IReadOnlyList<string> Missing
    {
        get
        {
            var list = new List<string>();
            if (YtDlpPath is null) list.Add("yt-dlp");
            if (FfmpegPath is null || FfprobePath is null) list.Add("ffmpeg");
            if (DenoPath is null) list.Add("deno");
            return list;
        }
    }

    public bool AllAvailable => Missing.Count == 0;

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("YtMp3Downloader", "1.0"));
        return c;
    }

    /// <summary>Najprv vlastný priečinok s nástrojmi, potom PATH.</summary>
    string? Resolve(string exeName)
    {
        var own = Path.Combine(ToolsDir, exeName);
        if (File.Exists(own))
            return own;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // neplatná položka v PATH
            }
        }

        return null;
    }

    public async Task EnsureInstalledAsync(IProgress<ToolProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ToolsDir);

        if (YtDlpPath is null)
        {
            var dest = Path.Combine(ToolsDir, "yt-dlp.exe");
            await DownloadVerifiedAsync(YtDlpBase + "yt-dlp.exe", YtDlpBase + "SHA2-256SUMS", "yt-dlp.exe",
                dest, "yt-dlp", progress, ct);
        }

        if (FfmpegPath is null || FfprobePath is null)
        {
            var zip = Path.Combine(ToolsDir, FfmpegZip + ".part");
            try
            {
                await DownloadVerifiedAsync(FfmpegBase + FfmpegZip, FfmpegBase + "checksums.sha256", FfmpegZip,
                    zip, "ffmpeg", progress, ct);
                progress.Report(new ToolProgress("Rozbaľujem ffmpeg…", 100));
                ExtractByName(zip, ["ffmpeg.exe", "ffprobe.exe"]);
            }
            finally
            {
                TryDelete(zip);
            }
        }

        if (DenoPath is null)
        {
            var zip = Path.Combine(ToolsDir, DenoZip + ".part");
            try
            {
                await DownloadVerifiedAsync(DenoBase + DenoZip, DenoBase + DenoZip + ".sha256sum", DenoZip,
                    zip, "deno", progress, ct);
                progress.Report(new ToolProgress("Rozbaľujem deno…", 100));
                ExtractByName(zip, ["deno.exe"]);
            }
            finally
            {
                TryDelete(zip);
            }
        }
    }

    public async Task<string> GetYtDlpVersionAsync(CancellationToken ct)
    {
        if (YtDlpPath is not { } exe)
            return "nenainštalované";

        var sb = new StringBuilder();
        await ProcessRunner.RunAsync(exe, ["--version"], (l, _) => sb.Append(l.Trim()), ct);
        return sb.ToString();
    }

    /// <summary>Spustí <c>yt-dlp -U</c>. Funguje iba pre kópiu v priečinku nástrojov aplikácie.</summary>
    public async Task<string> UpdateYtDlpAsync(CancellationToken ct)
    {
        if (YtDlpPath is not { } exe)
            return "yt-dlp nie je nainštalovaný.";
        if (!exe.StartsWith(ToolsDir, StringComparison.OrdinalIgnoreCase))
            return $"yt-dlp sa používa z {exe} (PATH) – aktualizujte ho tam.";

        var sb = new StringBuilder();
        await ProcessRunner.RunAsync(exe, ["-U"], (l, _) => sb.AppendLine(l), ct);
        return sb.ToString().Trim();
    }

    static async Task DownloadVerifiedAsync(
        string url, string checksumUrl, string checksumName, string dest, string label,
        IProgress<ToolProgress> progress, CancellationToken ct)
    {
        var part = dest.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ? dest : dest + ".part";
        try
        {
            progress.Report(new ToolProgress($"Sťahujem {label}…", 0));
            await DownloadFileAsync(url, part, label, progress, ct);

            progress.Report(new ToolProgress($"Overujem {label}…", 100));
            var expected = await GetExpectedHashAsync(checksumUrl, checksumName, ct);
            string actual;
            await using (var fs = File.OpenRead(part))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Kontrolný súčet súboru {label} nesedí – sťahovanie je poškodené.");

            if (part != dest)
                File.Move(part, dest, overwrite: true);
        }
        catch
        {
            if (part != dest)
                TryDelete(part);
            throw;
        }
    }

    static async Task DownloadFileAsync(
        string url, string dest, string label, IProgress<ToolProgress> progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long read = 0;
        var lastReported = -1;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;

            var percent = total is > 0 ? (double)read / total.Value * 100 : 0;
            if ((int)percent != lastReported)
            {
                lastReported = (int)percent;
                var text = total is > 0
                    ? $"Sťahujem {label}… {read / 1048576} / {total / 1048576} MB"
                    : $"Sťahujem {label}… {read / 1048576} MB";
                progress.Report(new ToolProgress(text, percent));
            }
        }
    }

    static async Task<string> GetExpectedHashAsync(string checksumUrl, string fileName, CancellationToken ct) =>
        ParseExpectedHash(await Http.GetStringAsync(checksumUrl, ct), fileName);

    /// <summary>
    /// Podporuje formáty "hash  názov" / "hash *názov" (sha256sum), samotný hash na riadku
    /// a výstup PowerShell Get-FileHash ("Hash : XXXX").
    /// </summary>
    public static string ParseExpectedHash(string text, string fileName)
    {
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith("Hash", StringComparison.OrdinalIgnoreCase) && raw.Contains(':'))
                return raw[(raw.IndexOf(':') + 1)..].Trim();

            var parts = raw.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;
            var name = parts.Length > 1 ? parts[1].TrimStart('*').Trim() : null;
            if (name is null || string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0];
        }

        throw new InvalidDataException($"V súbore s kontrolnými súčtami chýba {fileName}.");
    }

    void ExtractByName(string zipPath, string[] names)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var name in names)
        {
            var entry = zip.Entries.FirstOrDefault(e =>
                              string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidDataException($"V archíve chýba {name}.");
            var dest = Path.Combine(ToolsDir, name);
            var tmp = dest + ".tmp";
            entry.ExtractToFile(tmp, overwrite: true);
            File.Move(tmp, dest, overwrite: true);
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* nevadí */ }
    }
}
