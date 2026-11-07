using System.Globalization;

namespace HyperFidel;

/// <summary>
/// Builds and parses the stable, credential-free references used to persist
/// remote HyperFidel Server tracks and albums in playlists and queues.
/// </summary>
/// <remarks>
/// Track references use the form <c>hyperfidel://serverId/track/trackId</c>;
/// album drag references use <c>hyperfidel-album:serverId:albumId</c>. These
/// references never contain an authenticated stream URL or API key. The
/// historical <c>hyperfi</c> and <c>orynivo</c> forms are still parsed so
/// playlists and queues created by previous product names keep working.
/// </remarks>
internal static class PlaylistReferences
{
    /// <summary>The URI scheme used by persisted remote track references.</summary>
    internal const string TrackScheme = "hyperfidel";

    /// <summary>The historical HyperFi URI scheme accepted for migration.</summary>
    internal const string LegacyTrackScheme = "hyperfi";

    /// <summary>The historical Orynivo URI scheme accepted for migration.</summary>
    internal const string OlderLegacyTrackScheme = "orynivo";

    /// <summary>The prefix used by remote album drag references.</summary>
    internal const string AlbumPrefix = "hyperfidel-album:";

    /// <summary>The historical HyperFi album prefix accepted for migration.</summary>
    internal const string LegacyAlbumPrefix = "hyperfi-album:";

    /// <summary>The historical Orynivo album prefix accepted for migration.</summary>
    internal const string OlderLegacyAlbumPrefix = "orynivo-album:";

    /// <summary>Determines whether a value is a current or historical remote track reference.</summary>
    /// <param name="path">Candidate reference.</param>
    /// <returns><see langword="true"/> when the value uses a supported track scheme.</returns>
    internal static bool IsTrackReference(string? path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return false;
        return HasTrackScheme(uri);
    }

    /// <summary>Determines whether a parsed URI uses the current or a historical track scheme.</summary>
    /// <param name="uri">Parsed candidate reference.</param>
    /// <returns><see langword="true"/> when the scheme is supported.</returns>
    private static bool HasTrackScheme(Uri uri) =>
        uri.Scheme.Equals(TrackScheme, StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(LegacyTrackScheme, StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(OlderLegacyTrackScheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds a stable, credential-free reference for a remote track.</summary>
    /// <param name="serverId">Stable configured server identifier.</param>
    /// <param name="trackId">Provider-local track identifier.</param>
    /// <returns>The <c>hyperfidel://</c> track reference.</returns>
    internal static string BuildTrack(string serverId, long trackId) =>
        $"{TrackScheme}://{Uri.EscapeDataString(serverId)}/track/{trackId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Parses a stable remote track reference.</summary>
    /// <param name="path">Candidate reference.</param>
    /// <param name="serverId">Receives the encoded server identifier.</param>
    /// <param name="trackId">Receives the track identifier.</param>
    /// <returns><see langword="true"/> when the value is a valid track reference.</returns>
    internal static bool TryParseTrack(string? path, out string serverId, out long trackId)
    {
        serverId = string.Empty;
        trackId = 0;
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !HasTrackScheme(uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2 ||
            !segments[0].Equals("track", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
        {
            return false;
        }

        serverId = Uri.UnescapeDataString(uri.Host);
        return serverId.Length > 0;
    }

    /// <summary>Builds a remote album drag reference.</summary>
    /// <param name="serverId">Stable configured server identifier.</param>
    /// <param name="albumId">Provider-local album identifier.</param>
    /// <returns>The <c>hyperfidel-album:</c> reference.</returns>
    internal static string BuildAlbum(string serverId, long albumId) =>
        $"{AlbumPrefix}{serverId}:{albumId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Parses a remote album drag reference.</summary>
    /// <param name="token">Candidate reference.</param>
    /// <param name="serverId">Receives the server identifier.</param>
    /// <param name="albumId">Receives the album identifier.</param>
    /// <returns><see langword="true"/> when the value is a valid album reference.</returns>
    internal static bool TryParseAlbum(string? token, out string serverId, out long albumId)
    {
        serverId = string.Empty;
        albumId = 0;
        if (string.IsNullOrEmpty(token))
            return false;
        var prefix = token.StartsWith(AlbumPrefix, StringComparison.Ordinal)
            ? AlbumPrefix
            : token.StartsWith(LegacyAlbumPrefix, StringComparison.Ordinal)
                ? LegacyAlbumPrefix
                : token.StartsWith(OlderLegacyAlbumPrefix, StringComparison.Ordinal)
                    ? OlderLegacyAlbumPrefix
                    : null;
        if (prefix is null)
            return false;

        var rest = token[prefix.Length..];
        var separator = rest.LastIndexOf(':');
        if (separator <= 0 ||
            !long.TryParse(rest[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out albumId))
        {
            return false;
        }

        serverId = rest[..separator];
        return true;
    }
}
