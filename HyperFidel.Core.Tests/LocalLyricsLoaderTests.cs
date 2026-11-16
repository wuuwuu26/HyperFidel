using System.Text;
using HyperFidel.Library;
using Xunit;

namespace HyperFidel.Core.Tests;

/// <summary>
/// Verifies that HyperFidel loads lyrics only from local sidecar files and that
/// Chinese <c>.lrc</c> files encoded in legacy code pages decode without
/// mojibake. This is the offline replacement for the removed LRCLIB client.
/// </summary>
public sealed class LocalLyricsLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "hyperfidel-lyrics-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the per-test temporary track directory.</summary>
    public LocalLyricsLoaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Removes the per-test temporary track directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string TrackPath(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    /// <summary>A UTF-8 sidecar is read verbatim, including its LRC timestamps.</summary>
    [Fact]
    public void TryLoadLocalLyrics_Utf8Sidecar_ReturnsSyncedText()
    {
        var track = TrackPath("song.flac");
        const string lrc = "[00:05.00]Hello world\n[00:10.00]Second line";
        File.WriteAllText(Path.ChangeExtension(track, ".lrc"), lrc, new UTF8Encoding(false));

        var result = LyricsService.TryLoadLocalLyrics(track);

        Assert.NotNull(result);
        Assert.Equal(lrc, result!.SyncedLyrics);
        Assert.Null(result.PlainLyrics);
    }

    /// <summary>A UTF-8 BOM is stripped instead of appearing as a stray glyph.</summary>
    [Fact]
    public void TryLoadLocalLyrics_Utf8Bom_StripsBom()
    {
        var track = TrackPath("bom.mp3");
        const string lrc = "[00:01.00]BOM line";
        File.WriteAllText(Path.ChangeExtension(track, ".lrc"), lrc, new UTF8Encoding(true));

        var result = LyricsService.TryLoadLocalLyrics(track);

        Assert.NotNull(result);
        Assert.Equal(lrc, result!.SyncedLyrics);
    }

    /// <summary>A GBK/GB18030 Chinese sidecar decodes without mojibake.</summary>
    [Fact]
    public void TryLoadLocalLyrics_GbkChineseSidecar_DecodesChineseText()
    {
        var track = TrackPath("chinese.flac");
        const string line = "[00:05.00]中文歌词测试";
        // GB18030 (code page 54936) covers GBK/GB2312 bytes used by Chinese .lrc files.
        var legacy = Encoding.GetEncoding(54936);
        File.WriteAllBytes(Path.ChangeExtension(track, ".lrc"), legacy.GetBytes(line));

        var result = LyricsService.TryLoadLocalLyrics(track);

        Assert.NotNull(result);
        Assert.Equal(line, result!.SyncedLyrics);
    }

    /// <summary>A UTF-16 LE BOM sidecar decodes without mojibake.</summary>
    [Fact]
    public void TryLoadLocalLyrics_Utf16LeBom_DecodesText()
    {
        var track = TrackPath("utf16.m4a");
        const string lrc = "[00:02.00]UTF-16 line";
        File.WriteAllBytes(Path.ChangeExtension(track, ".lrc"), Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(lrc)).ToArray());

        var result = LyricsService.TryLoadLocalLyrics(track);

        Assert.NotNull(result);
        Assert.Equal(lrc, result!.SyncedLyrics);
    }

    /// <summary>When no sidecar exists the loader returns null.</summary>
    [Fact]
    public void TryLoadLocalLyrics_MissingSidecar_ReturnsNull()
    {
        var track = TrackPath("nope.flac");

        Assert.Null(LyricsService.TryLoadLocalLyrics(track));
    }

    /// <summary>An empty sidecar is treated as no lyrics.</summary>
    [Fact]
    public void TryLoadLocalLyrics_EmptySidecar_ReturnsNull()
    {
        var track = TrackPath("empty.flac");
        File.WriteAllBytes(Path.ChangeExtension(track, ".lrc"), []);

        Assert.Null(LyricsService.TryLoadLocalLyrics(track));
    }
}
