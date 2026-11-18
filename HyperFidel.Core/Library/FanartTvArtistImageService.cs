using System.Text.Json;

namespace HyperFidel.Library;

/// <summary>Contains a downloaded artist image that has not yet been persisted.</summary>
/// <param name="ImageData">Raw image bytes.</param>
/// <param name="MimeType">Image MIME type.</param>
/// <param name="SourceUrl">Public source URL of the image.</param>
public sealed record ArtistImageDownload(byte[] ImageData, string? MimeType, string SourceUrl);

/// <summary>
/// Offline artist-thumbnail helper. Online Fanart.tv lookup is unavailable in the
/// local-only player; the pure selection helpers remain for compatibility.
/// </summary>
public static class FanartTvArtistImageService
{
    /// <summary>Returns <see langword="null"/>; online downloads are not available offline.</summary>
    /// <param name="artistId">Local cache identifier.</param>
    /// <param name="artistName">Artist display name.</param>
    /// <param name="musicBrainzArtistId">Known MusicBrainz artist ID.</param>
    /// <param name="apiKey">Fanart.tv personal API key (unused).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Always <see langword="null"/> in the offline player.</returns>
    public static Task<string?> DownloadBestAsync(
        long artistId,
        string artistName,
        string? musicBrainzArtistId,
        string? apiKey,
        CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    /// <summary>Returns <see langword="null"/>; online lookups are not available offline.</summary>
    /// <param name="artistName">Artist display name.</param>
    /// <param name="musicBrainzArtistId">Known MusicBrainz artist ID.</param>
    /// <param name="apiKey">Fanart.tv personal API key (unused).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Always <see langword="null"/> in the offline player.</returns>
    public static Task<ArtistImageDownload?> FindBestAsync(
        string artistName,
        string? musicBrainzArtistId,
        string? apiKey,
        CancellationToken cancellationToken = default) => Task.FromResult<ArtistImageDownload?>(null);

    /// <summary>Selects the best HTTPS artist thumbnail from parsed Fanart.tv JSON.</summary>
    /// <param name="root">Parsed JSON root element.</param>
    /// <returns>The selected thumbnail URL, or <see langword="null"/>.</returns>
    internal static string? SelectBestArtistThumbnailUrl(JsonElement root)
    {
        if (!root.TryGetProperty("artistthumb", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return thumbnails.EnumerateArray()
            .Select(image => new
            {
                Url = image.TryGetProperty("url", out var url) ? url.GetString() : null,
                Likes = ReadInteger(image, "likes"),
                Width = ReadInteger(image, "width"),
                Height = ReadInteger(image, "height")
            })
            .Where(image =>
                Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps)
            .OrderByDescending(image => image.Likes)
            .ThenBy(image => Math.Abs(image.Width - image.Height))
            .ThenByDescending(image => image.Width * image.Height)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static int ReadInteger(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;
        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), out number)
            ? number
            : 0;
    }
}
