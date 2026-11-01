using System.Text;

namespace YtMp3Downloader.Services;

public static class FileNames
{
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Sanitize(string? name, string fallback = "Playlist")
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        var s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length > 100)
            s = s[..100].TrimEnd('.', ' ');
        if (s.Length == 0)
            return fallback;
        if (Reserved.Contains(s))
            s += "_";
        return s;
    }
}
