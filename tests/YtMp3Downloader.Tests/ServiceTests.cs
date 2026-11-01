using YtMp3Downloader.Models;
using YtMp3Downloader.Services;

namespace YtMp3Downloader.Tests;

public class ServiceTests
{
    [Theory]
    [InlineData("Môj: playlist?", "Môj_ playlist_")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("trailing. ", "trailing")]
    [InlineData("CON", "CON_")]
    [InlineData("   ", "Playlist")]
    [InlineData(null, "Playlist")]
    public void Sanitize(string? input, string expected) =>
        Assert.Equal(expected, FileNames.Sanitize(input));

    [Fact]
    public void Sanitize_TruncatesLongNames() =>
        Assert.Equal(100, FileNames.Sanitize(new string('x', 300)).Length);

    [Fact]
    public void BuildDownloadArgs_ContainsExpectedOptions()
    {
        var tools = new ToolsService(Path.Combine(Path.GetTempPath(), "ytmp3-tests-no-tools"));
        var playlist = new PlaylistInfo("u", "Hits: 100%", null, null, [], true);
        var entry = new EntryInfo("id1", "Song", "https://www.youtube.com/watch?v=id1", 7, null);
        var settings = new AppSettings { AudioQuality = "320K" };

        var args = YtDlpService.BuildDownloadArgs(entry, playlist, @"C:\out\Hits", settings, tools);

        Assert.Contains("--no-playlist", args);
        Assert.Equal("320K", args[args.IndexOf("--audio-quality") + 1]);
        Assert.Equal(@"C:\out\Hits\downloaded.txt", args[args.IndexOf("--download-archive") + 1]);
        Assert.Equal(@"C:\out\Hits\007 - %(title).100s.%(ext)s", args[args.IndexOf("--output") + 1]);
        Assert.Contains(@"Hits\: 100%%:%(album)s", args);
        Assert.Contains("7:%(track_number)s", args);
        Assert.Equal("https://www.youtube.com/watch?v=id1", args[^1]);
    }

    [Fact]
    public void BuildDownloadArgs_EscapesPercentInFolder()
    {
        var tools = new ToolsService(Path.Combine(Path.GetTempPath(), "ytmp3-tests-no-tools"));
        var playlist = new PlaylistInfo("u", "Single", null, null, [], false);
        var entry = new EntryInfo("id1", "Song", "u", 1, null);

        var args = YtDlpService.BuildDownloadArgs(entry, playlist, @"C:\100%\x", new AppSettings(), tools);

        Assert.Equal(@"C:\100%%\x\001 - %(title).100s.%(ext)s", args[args.IndexOf("--output") + 1]);
        Assert.DoesNotContain("--parse-metadata", args); // album iba pre playlisty
    }
}

public class ChecksumTests
{
    const string Hash = "f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d";

    [Fact]
    public void Parse_Sha256sumFormat_PicksMatchingFile()
    {
        var text = $"aaaa  other.exe\n{Hash} *yt-dlp.exe\nbbbb  yt-dlp_x86.exe\n";
        Assert.Equal(Hash, ToolsService.ParseExpectedHash(text, "yt-dlp.exe"));
    }

    [Fact]
    public void Parse_GetFileHashFormat()
    {
        const string text = "\r\nAlgorithm : SHA256\r\nHash      : CBA84B26E0DF\r\nPath      : C:\\a\\deno.zip\r\n";
        Assert.Equal("CBA84B26E0DF", ToolsService.ParseExpectedHash(text, "deno.zip"));
    }

    [Fact]
    public void Parse_BareHash()
    {
        Assert.Equal(Hash, ToolsService.ParseExpectedHash(Hash + "\n", "anything.zip"));
    }

    [Fact]
    public void Parse_MissingFile_Throws()
    {
        Assert.Throws<InvalidDataException>(() => ToolsService.ParseExpectedHash("aaaa  other.exe\n", "yt-dlp.exe"));
    }
}
