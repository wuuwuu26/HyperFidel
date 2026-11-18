using System.Net.Http;

namespace HyperFidel.Library;

/// <summary>Offline artist profile result.</summary>
/// <param name="Biography">Extracted biography text.</param>
/// <param name="ImagePath">Absolute path to a cached artist image, or <see langword="null"/>.</param>
/// <param name="SourceUrl">Canonical source URL of the profile.</param>
/// <param name="Language">BCP 47 language code of the content.</param>
public sealed record ArtistProfileDownload(
    string Biography,
    string? ImagePath,
    string SourceUrl,
    string Language);

/// <summary>
/// Offline artist-profile provider. Online biography/image downloads (Wikipedia,
/// Last.fm, Fanart.tv) are unavailable in the local-only player; locally cached
/// profiles are read from the database by the desktop providers instead.
/// </summary>
public static class ArtistProfileService
{
    /// <summary>Active biography source (retained for settings compatibility).</summary>
    public static ArtistInfoSource Source { get; set; } = ArtistInfoSource.Wikipedia;

    /// <summary>Last.fm API key (retained for settings compatibility; unused offline).</summary>
    public static string? LastFmApiKey { get; set; }

    /// <summary>Fanart.tv API key (retained for settings compatibility; unused offline).</summary>
    public static string? FanartTvApiKey { get; set; }

    /// <summary>No download is attempted offline, so no diagnostic is produced.</summary>
    public static string? LastImageDiagnostic { get; private set; }

    /// <summary>Returns <see langword="null"/>; online downloads are unavailable offline.</summary>
    /// <param name="artistId">Database artist ID.</param>
    /// <param name="artistName">Artist name.</param>
    /// <param name="language">Preferred content language.</param>
    /// <param name="downloadImage">Whether an image would be downloaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="musicBrainzArtistId">Known MusicBrainz artist ID.</param>
    /// <returns>Always <see langword="null"/> in the offline player.</returns>
    public static Task<ArtistProfileDownload?> DownloadAsync(
        long artistId,
        string artistName,
        string language,
        bool downloadImage = true,
        CancellationToken cancellationToken = default,
        string? musicBrainzArtistId = null) => Task.FromResult<ArtistProfileDownload?>(null);

    /// <summary>Returns <see langword="null"/>; online downloads are unavailable offline.</summary>
    /// <param name="artistId">Database artist ID.</param>
    /// <param name="artistName">Artist name.</param>
    /// <param name="language">Preferred content language.</param>
    /// <param name="source">Biography source (unused).</param>
    /// <param name="lastFmApiKey">Last.fm API key (unused).</param>
    /// <param name="downloadImage">Whether an image would be downloaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="musicBrainzArtistId">Known MusicBrainz artist ID.</param>
    /// <returns>Always <see langword="null"/> in the offline player.</returns>
    public static Task<ArtistProfileDownload?> DownloadAsync(
        long artistId,
        string artistName,
        string language,
        ArtistInfoSource source,
        string? lastFmApiKey,
        bool downloadImage = true,
        CancellationToken cancellationToken = default,
        string? musicBrainzArtistId = null) => Task.FromResult<ArtistProfileDownload?>(null);

    /// <summary>
    /// Stages caller-provided image content into the local artist-image cache. This only
    /// reads the supplied stream and writes local files; it performs no network request.
    /// </summary>
    /// <param name="artistId">Database artist ID used as the cache file stem.</param>
    /// <param name="content">Caller-owned content whose stream remains caller-owned.</param>
    /// <param name="imageUrl">Transient URL used only for extension fallback.</param>
    /// <param name="cancellationToken">Cancellation during staging.</param>
    /// <returns>The published path, or <see langword="null"/> when no image format can be resolved.</returns>
    internal static async Task<string?> SaveDownloadedImageAsync(long artistId, HttpContent content,
        string imageUrl, CancellationToken cancellationToken)
    {
        var mediaType = content.Headers.ContentType?.MediaType;
        string extension;
        if (mediaType == "image/png") extension = ".png";
        else if (mediaType == "image/webp") extension = ".webp";
        else if (mediaType?.StartsWith("image/") == true) extension = ".jpg";
        else
        {
            var urlPath = imageUrl.Split('?', '#')[0];
            var ext = Path.GetExtension(urlPath).ToLowerInvariant();
            extension = ext switch { ".png" => ".png", ".webp" => ".webp", ".jpeg" => ".jpg", ".jpg" => ".jpg", _ => "" };
            if (extension.Length == 0)
                return null;
        }
        var mimeType = extension switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ArtistImageSearchService.SaveImageStreamAsync(artistId, source, mimeType, cancellationToken)
            .ConfigureAwait(false);
    }
}
