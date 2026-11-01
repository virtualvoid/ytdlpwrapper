namespace YtMp3Downloader.Services;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YtMp3Downloader");

    public static string ToolsDir => Path.Combine(DataDir, "tools");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static string QueueFile => Path.Combine(DataDir, "queue.json");

    public static string LogFile => Path.Combine(DataDir, "app.log");
}
