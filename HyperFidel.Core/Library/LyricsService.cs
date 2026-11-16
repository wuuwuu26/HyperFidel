using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyperFidel.Library;

/// <summary>Plain and synchronised lyrics loaded from a local source.</summary>
/// <param name="PlainLyrics">Unsynced plain-text lyrics, or <see langword="null"/>.</param>
/// <param name="SyncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
public sealed record LyricsDownloadResult(string? PlainLyrics, string? SyncedLyrics);

/// <summary>A single search result from the LRCLIB search endpoint.</summary>
/// <param name="Id">LRCLIB track ID.</param>
/// <param name="TrackName">Track title.</param>
/// <param name="ArtistName">Artist name.</param>
/// <param name="AlbumName">Album name, or <see langword="null"/>.</param>
/// <param name="Duration">Duration in seconds, or <see langword="null"/>.</param>
/// <param name="PlainLyrics">Unsynced lyrics, or <see langword="null"/>.</param>
/// <param name="SyncedLyrics">LRC-formatted lyrics, or <see langword="null"/>.</param>
public sealed record LyricsSearchResult(
    long Id,
    string TrackName,
    string ArtistName,
    string? AlbumName,
    double? Duration,
    string? PlainLyrics,
    string? SyncedLyrics);

/// <summary>A single timestamped line from an LRC lyrics file.</summary>
/// <param name="Time">Playback offset of the line.</param>
/// <param name="Text">Lyric text for this line.</param>
/// <summary>One word of an enhanced-LRC line with its own start time.</summary>
/// <param name="Time">Word start time.</param>
/// <param name="Text">Word text without its marker.</param>
public sealed record TimedLyricWord(TimeSpan Time, string Text);

/// <summary>One timestamped lyric line.</summary>
/// <param name="Time">Line start time.</param>
/// <param name="Text">Line text with every timestamp marker removed.</param>
public sealed record TimedLyricLine(TimeSpan Time, string Text)
{
    /// <summary>
    /// Gets the word-level timings of an enhanced-LRC line, or an empty list for a
    /// plain synchronized line.
    /// </summary>
    public IReadOnlyList<TimedLyricWord> Words { get; init; } = [];
}

/// <summary>
/// Client for the LRCLIB lyrics API supporting automatic download, manual search, and LRC parsing.
/// </summary>
public static partial class LyricsService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    static LyricsService()
    {
        // Register the code-page provider once so GB18030/GBK decoding is
        // available on every platform. This assembly ships with the runtime.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Loads synchronised lyrics from a local LRC sidecar next to
    /// <paramref name="trackPath"/>. The sidecar uses the track file name with an
    /// <c>.lrc</c> extension (for example <c>song.lrc</c> for <c>song.flac</c>).
    /// Encoding is sniffed from the byte-order mark, then validated as UTF-8, and
    /// finally falls back to GB18030 so legacy Chinese <c>.lrc</c> files decode
    /// without mojibake.
    /// </summary>
    /// <param name="trackPath">Absolute path of the audio track.</param>
    /// <returns>
    /// The decoded lyrics, or <see langword="null"/> when no sidecar exists, the
    /// sidecar is empty, or it contains no timestamped lines.
    /// </returns>
    public static LyricsDownloadResult? TryLoadLocalLyrics(string trackPath)
    {
        if (string.IsNullOrWhiteSpace(trackPath))
            return null;

        string sidecar;
        try
        {
            sidecar = Path.ChangeExtension(trackPath, ".lrc");
            if (!File.Exists(sidecar))
                return null;

            var bytes = File.ReadAllBytes(sidecar);
            if (bytes.Length == 0)
                return null;

            var text = DecodeLyricsBytes(bytes);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Only treat content with at least one LRC timestamp as synced
            // lyrics. Plain sidecars are still returned as unsynced text.
            return TimestampRegex().IsMatch(text)
                ? new LyricsDownloadResult(null, text)
                : new LyricsDownloadResult(text, null);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes raw lyrics bytes by inspecting the byte-order mark first, then
    /// attempting strict UTF-8, and finally falling back to GB18030 for legacy
    /// Chinese encodings (GBK/GB2312).
    /// </summary>
    /// <param name="bytes">Raw bytes read from an <c>.lrc</c> file.</param>
    /// <returns>The decoded text without a preceding byte-order mark.</returns>
    private static string DecodeLyricsBytes(byte[] bytes)
    {
        // UTF-8 BOM.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false, throwOnInvalidBytes: true)
                .GetString(bytes, 3, bytes.Length - 3);

        // UTF-16 LE / BE BOM.
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // UTF-32 BOMs (rare, but cheap to support before UTF-8 validation).
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return new UTF32Encoding(bigEndian: true, byteOrderMark: false)
                .GetString(bytes, 4, bytes.Length - 4);

        // Strict UTF-8: if every byte is valid, trust it. Otherwise the file is
        // almost certainly a legacy single/double-byte Chinese encoding.
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(54936).GetString(bytes);
        }
    }

    /// <summary>
    /// Downloads plain and/or synchronised lyrics for <paramref name="track"/> from LRCLIB.
    /// Returns <see langword="null"/> when the track lacks required tags or LRCLIB returns no result.
    /// </summary>
    /// <param name="track">Track whose lyrics to fetch; <c>Title</c> and <c>Artist</c> must be set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<LyricsDownloadResult?> DownloadAsync(
        TrackRecord track,
        CancellationToken cancellationToken = default)
        => DownloadAsync(track.Title, track.Artist, track.Album, track.Duration, cancellationToken);

    /// <summary>
    /// Downloads plain and/or synchronised lyrics for the given track metadata from LRCLIB.
    /// Returns <see langword="null"/> when title or artist is missing or LRCLIB returns no result.
    /// </summary>
    /// <param name="title">Track title; required.</param>
    /// <param name="artist">Track artist; required.</param>
    /// <param name="album">Album name, or <see langword="null"/>.</param>
    /// <param name="duration">Duration in seconds, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<LyricsDownloadResult?> DownloadAsync(
        string? title,
        string? artist,
        string? album,
        double? duration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;

        var parameters = new List<string>
        {
            $"track_name={Uri.EscapeDataString(title)}",
            $"artist_name={Uri.EscapeDataString(artist)}"
        };
        if (!string.IsNullOrWhiteSpace(album))
            parameters.Add($"album_name={Uri.EscapeDataString(album)}");
        if (duration is > 0)
            parameters.Add($"duration={Math.Round(duration.Value).ToString(CultureInfo.InvariantCulture)}");

        using var response = await Client.GetAsync(
            "https://lrclib.net/api/get?" + string.Join("&", parameters),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<LrclibResponse>(
            stream,
            JsonOptions,
            cancellationToken: cancellationToken);
        if (result is null || result.Instrumental)
            return null;

        var plain = NullIfWhiteSpace(result.PlainLyrics);
        var synced = NullIfWhiteSpace(result.SyncedLyrics);
        return plain is null && synced is null
            ? null
            : new LyricsDownloadResult(plain, synced);
    }

    /// <summary>
    /// Searches LRCLIB for lyrics matching the given track and artist name.
    /// Instrumental results and entries with neither plain nor synced lyrics are excluded.
    /// </summary>
    /// <param name="trackName">Track title query.</param>
    /// <param name="artistName">Artist name query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IReadOnlyList<LyricsSearchResult>> SearchAsync(
        string trackName,
        string artistName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackName) && string.IsNullOrWhiteSpace(artistName))
            return [];

        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(trackName))
            parameters.Add($"track_name={Uri.EscapeDataString(trackName.Trim())}");
        if (!string.IsNullOrWhiteSpace(artistName))
            parameters.Add($"artist_name={Uri.EscapeDataString(artistName.Trim())}");

        using var response = await Client.GetAsync(
            "https://lrclib.net/api/search?" + string.Join("&", parameters),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var results = await JsonSerializer.DeserializeAsync<List<LrclibSearchResponse>>(
            stream,
            JsonOptions,
            cancellationToken);
        if (results is null)
            return [];

        return results
            .Where(result => !result.Instrumental)
            .Select(result => new LyricsSearchResult(
                result.Id,
                result.TrackName,
                result.ArtistName,
                result.AlbumName,
                result.Duration,
                NullIfWhiteSpace(result.PlainLyrics),
                NullIfWhiteSpace(result.SyncedLyrics)))
            .Where(result => result.PlainLyrics is not null || result.SyncedLyrics is not null)
            .ToList();
    }

    /// <summary>
    /// Parses an LRC-formatted string into a list of timestamped lyric lines sorted
    /// by time. Enhanced-LRC word markers (<c>&lt;mm:ss.xx&gt;</c>) are removed from
    /// the line text and exposed as <see cref="TimedLyricLine.Words"/>. Returns an
    /// empty list when <paramref name="lyrics"/> is null or whitespace.
    /// </summary>
    /// <param name="lyrics">LRC content with <c>[mm:ss.xx]</c> timestamp tags.</param>
    /// <returns>The parsed lines ordered by start time.</returns>
    public static IReadOnlyList<TimedLyricLine> ParseLrc(string? lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics))
            return [];

        var result = new List<TimedLyricLine>();
        foreach (var rawLine in lyrics.Replace("\r\n", "\n").Split('\n'))
        {
            var matches = TimestampRegex().Matches(rawLine);
            if (matches.Count == 0)
                continue;

            var (text, words) = ParseLineContent(TimestampRegex().Replace(rawLine, string.Empty).Trim());
            foreach (Match match in matches)
            {
                if (!TryParseTimestamp(match, out var time))
                    continue;

                result.Add(new TimedLyricLine(time, text) { Words = words });
            }
        }

        return result
            .OrderBy(line => line.Time)
            .ToList();
    }

    /// <summary>
    /// Splits an enhanced-LRC line into its word timings and the plain line text. A
    /// line without word markers keeps its text unchanged and reports no words.
    /// </summary>
    /// <param name="content">Line content with the line timestamp already removed.</param>
    /// <returns>The plain line text and the word timings.</returns>
    private static (string Text, IReadOnlyList<TimedLyricWord> Words) ParseLineContent(string content)
    {
        var markers = WordTimestampRegex().Matches(content);
        var text = WordTimestampRegex().Replace(content, string.Empty);
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (markers.Count == 0)
            return (text, []);

        var words = new List<TimedLyricWord>(markers.Count);
        for (var index = 0; index < markers.Count; index++)
        {
            var match = markers[index];
            var start = match.Index + match.Length;
            var end = index + 1 < markers.Count ? markers[index + 1].Index : content.Length;
            var word = content[start..end].Trim();
            if (word.Length == 0 || !TryParseTimestamp(match, out var time))
                continue;
            words.Add(new TimedLyricWord(time, word));
        }

        return words.Count == 0 ? (text, []) : (text, words);
    }

    /// <summary>Parses the <c>minutes</c> and <c>seconds</c> groups of a timestamp match.</summary>
    /// <param name="match">Timestamp match with <c>minutes</c> and <c>seconds</c> groups.</param>
    /// <param name="time">Parsed timestamp when the groups are valid.</param>
    /// <returns><see langword="true"/> when the timestamp could be parsed.</returns>
    private static bool TryParseTimestamp(Match match, out TimeSpan time)
    {
        time = default;
        if (!int.TryParse(match.Groups["minutes"].Value, out var minutes) ||
            !double.TryParse(
                match.Groups["seconds"].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            return false;
        }

        time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HyperFidel/1.0");
        client.DefaultRequestHeaders.Add("Lrclib-Client", "HyperFidel");
        return client;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex(@"\[(?<minutes>\d+):(?<seconds>\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"<(?<minutes>\d+):(?<seconds>\d{1,2}(?:\.\d{1,3})?)>")]
    private static partial Regex WordTimestampRegex();

    private sealed record LrclibResponse(
        bool Instrumental,
        string? PlainLyrics,
        string? SyncedLyrics);

    private sealed record LrclibSearchResponse(
        long Id,
        string TrackName,
        string ArtistName,
        string? AlbumName,
        double? Duration,
        bool Instrumental,
        string? PlainLyrics,
        string? SyncedLyrics);
}
