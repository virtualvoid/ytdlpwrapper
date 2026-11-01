using YtMp3Downloader.Services;

namespace YtMp3Downloader.Tests;

public class YtDlpParserTests
{
    const string PlaylistJson = """
    {
      "_type": "playlist",
      "id": "PL123",
      "title": "Môj playlist",
      "uploader": "Janko",
      "extractor_key": "YoutubeTab",
      "entries": [
        { "id": "aaaaaaaaaaa", "title": "Prvá", "url": "https://www.youtube.com/watch?v=aaaaaaaaaaa", "duration": 200 },
        null,
        { "id": "bbbbbbbbbbb", "title": "Druhá", "url": "https://www.youtube.com/watch?v=bbbbbbbbbbb" },
        { "title": "bez id" }
      ]
    }
    """;

    const string VideoJson = """
    {
      "id": "ccccccccccc",
      "title": "Sólo video",
      "channel": "Kanál",
      "webpage_url": "https://www.youtube.com/watch?v=ccccccccccc",
      "extractor_key": "Youtube",
      "duration": 123.5
    }
    """;

    [Fact]
    public void ParsePreview_Playlist_SkipsInvalidEntriesAndKeepsPosition()
    {
        var info = YtDlpParser.ParsePreview(PlaylistJson, "https://www.youtube.com/playlist?list=PL123");

        Assert.True(info.IsPlaylist);
        Assert.Equal("Môj playlist", info.Title);
        Assert.Equal("Janko", info.Uploader);
        Assert.Equal(2, info.Entries.Count);
        Assert.Equal(1, info.Entries[0].Index);
        Assert.Equal(3, info.Entries[1].Index); // pozícia v playliste sa nemení, aj keď je položka 2 null
        Assert.Equal("https://i.ytimg.com/vi/aaaaaaaaaaa/mqdefault.jpg", info.ThumbnailUrl);
    }

    [Fact]
    public void ParsePreview_SingleVideo_ReturnsOneEntry()
    {
        var info = YtDlpParser.ParsePreview(VideoJson, "https://youtu.be/ccccccccccc");

        Assert.False(info.IsPlaylist);
        Assert.Equal("Sólo video", info.Title);
        Assert.Equal("Kanál", info.Uploader);
        var e = Assert.Single(info.Entries);
        Assert.Equal("ccccccccccc", e.Id);
        Assert.Equal(123.5, e.DurationSeconds);
        Assert.Equal("https://i.ytimg.com/vi/ccccccccccc/mqdefault.jpg", info.ThumbnailUrl);
    }

    [Theory]
    [InlineData("YTP|downloading|500|1000|NA", 50)]
    [InlineData("YTP|downloading|250|NA|1000", 25)]
    [InlineData("YTP|downloading|NA|NA|NA", 0)]
    [InlineData("YTP|finished|1000|1000|NA", 100)]
    public void ParseLine_Progress(string line, double expected)
    {
        var parsed = YtDlpParser.ParseLine(line);
        Assert.Equal(YtDlpLineKind.Progress, parsed.Kind);
        Assert.Equal(expected, parsed.Percent, 3);
    }

    [Fact]
    public void ParseLine_RecognizesOtherKinds()
    {
        Assert.Equal(YtDlpLineKind.Converting,
            YtDlpParser.ParseLine("[ExtractAudio] Destination: C:\\x\\001 - a.mp3").Kind);
        Assert.Equal(YtDlpLineKind.AlreadyDone,
            YtDlpParser.ParseLine("[youtube] abc: has already been recorded in the archive").Kind);
        var err = YtDlpParser.ParseLine("ERROR: [youtube] abc: Video unavailable");
        Assert.Equal(YtDlpLineKind.Error, err.Kind);
        Assert.Equal("[youtube] abc: Video unavailable", err.Text);
        Assert.Equal(YtDlpLineKind.Other, YtDlpParser.ParseLine("[download] Destination: x").Kind);
    }
}
