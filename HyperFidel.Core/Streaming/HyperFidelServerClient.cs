// Offline stand-in: preserves public signatures but performs no network I/O.
namespace HyperFidel.Streaming;

using HyperFidel.Library;
using HyperFidel.Updates;

public sealed class HyperFidelServerClient : System.IDisposable
{
        public Task<HyperFidelServerInfo?> TestConnectionAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelServerInfo?>(default!);
    }

        public Task<HyperFidelServerUpdateStatus?> GetUpdateStatusAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelServerUpdateStatus?>(default!);
    }

        public Task<bool> UploadUpdateAsync(
        HyperFidelServerSettings server,
        VerifiedReleaseManifest release,
        string packagePath,
        string packageFileName,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> ApplyUpdateAsync(HyperFidelServerSettings server, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<HyperFidelServerCapabilities> GetCapabilitiesAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelServerCapabilities>(default!);
    }

        public Task<DashboardLibrarySummary?> GetLibrarySummaryAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<DashboardLibrarySummary?>(default!);
    }

        public Task<List<HyperFidelArtistInfo>> GetArtistsAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default,
        bool requireComplete = false)
    {
        return Task.FromResult<List<HyperFidelArtistInfo>>(default!);
    }

        public Task<HyperFidelArtistInfo?> GetArtistAsync(
        HyperFidelServerSettings server,
        long artistId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelArtistInfo?>(default!);
    }

        public Task<List<HyperFidelAlbumInfo>> GetAlbumsByArtistAsync(
        HyperFidelServerSettings server,
        long artistId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelAlbumInfo>>(default!);
    }

        public Task<List<HyperFidelAlbumInfo>> GetAlbumsAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default,
        bool requireComplete = false)
    {
        return Task.FromResult<List<HyperFidelAlbumInfo>>(default!);
    }

        public Task<List<HyperFidelRecentAlbum>> GetRecentAlbumsAsync(
        HyperFidelServerSettings server,
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelRecentAlbum>>(default!);
    }

        public Task<List<HyperFidelServerProfile>> GetProfilesAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelServerProfile>>(default!);
    }

        public Task<List<HyperFidelHistorySyncEntry>> GetHistorySyncAsync(
        HyperFidelServerSettings server, int limit = 500, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelHistorySyncEntry>>(default!);
    }

        public Task<bool> PushHistorySyncAsync(
        HyperFidelServerSettings server, IReadOnlyList<HyperFidelHistorySyncEntry> entries,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<HyperFidelAlbumInfo?> GetAlbumAsync(
        HyperFidelServerSettings server,
        long albumId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelAlbumInfo?>(default!);
    }

        public Task<List<HyperFidelAlbumInfo>> GetAlbumsByIdsAsync(
        HyperFidelServerSettings server, IReadOnlyList<long> ids, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelAlbumInfo>>(default!);
    }

        public Task<List<HyperFidelRecommendationAlbum>> GetRecommendationAlbumsAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelRecommendationAlbum>>(default!);
    }

        public Task<HyperFidelArtistInfo?> UpdateArtistProfileAsync(
        HyperFidelServerSettings server,
        long artistId,
        string? biography,
        string? sourceUrl,
        string language,
        byte[]? imageData,
        string? imageMimeType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelArtistInfo?>(default!);
    }

        public Task<HyperFidelArtistRenameResponse?> RenameArtistAsync(
        HyperFidelServerSettings server,
        long artistId,
        string artistName,
        long? preferredArtistId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelArtistRenameResponse?>(default!);
    }

        public Task<bool> UploadAlbumArtworkAsync(
        HyperFidelServerSettings server,
        long albumId,
        byte[] imageData,
        string? mimeType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> DeleteAlbumArtworkAsync(
        HyperFidelServerSettings server,
        long albumId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<List<HyperFidelTrackInfo>> GetTracksByAlbumAsync(
        HyperFidelServerSettings server,
        long albumId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelTrackInfo>>(default!);
    }

        public Task<List<HyperFidelPlaylistInfo>> GetPlaylistsAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelPlaylistInfo>>(default!);
    }

        public Task<List<HyperFidelPlaylistTrackInfo>> GetPlaylistTracksAsync(
        HyperFidelServerSettings server,
        long playlistId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelPlaylistTrackInfo>>(default!);
    }

        public Task<HyperFidelPlaylistInfo?> CreatePlaylistAsync(
        HyperFidelServerSettings server,
        string name,
        IReadOnlyList<long> trackIds,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelPlaylistInfo?>(default!);
    }

        public Task<List<HyperFidelPlaylistTrackInfo>> ResolveSmartPlaylistTracksAsync(
        HyperFidelServerSettings server,
        long playlistId,
        IReadOnlyList<long> favoriteTrackIds,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelPlaylistTrackInfo>>(default!);
    }

        public Task<int?> ResolveSmartPlaylistCountAsync(
        HyperFidelServerSettings server,
        string criteriaJson,
        IReadOnlyList<long> favoriteTrackIds,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<int?>(default!);
    }

        public Task<HyperFidelPlaylistInfo?> CreateSmartPlaylistAsync(
        HyperFidelServerSettings server,
        string name,
        string filterCriteria,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelPlaylistInfo?>(default!);
    }

        public Task<HyperFidelPlaylistInfo?> UpdateSmartPlaylistAsync(
        HyperFidelServerSettings server,
        long playlistId,
        string name,
        string filterCriteria,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelPlaylistInfo?>(default!);
    }

        public Task<bool> AddTracksToPlaylistAsync(
        HyperFidelServerSettings server,
        long playlistId,
        IReadOnlyList<long> trackIds,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> DeletePlaylistAsync(
        HyperFidelServerSettings server,
        long playlistId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> RemovePlaylistEntryAsync(
        HyperFidelServerSettings server,
        long playlistEntryId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<List<HyperFidelTrackInfo>> GetTracksAsync(
        HyperFidelServerSettings server,
        int page = 0,
        int pageSize = 500,
        CancellationToken cancellationToken = default,
        bool requireComplete = false)
    {
        return Task.FromResult<List<HyperFidelTrackInfo>>(default!);
    }

        public Task<List<HyperFidelTrackInfo>> SearchTracksAsync(
        HyperFidelServerSettings server,
        string query,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelTrackInfo>>(default!);
    }

        public Task<HyperFidelFullSearchResult> SearchFullAsync(
        HyperFidelServerSettings server,
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default,
        bool requireComplete = false)
    {
        return Task.FromResult<HyperFidelFullSearchResult>(default!);
    }

        public Task<List<TrackFacetInfo>> GetTrackFacetsAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default,
        bool requireComplete = false)
    {
        return Task.FromResult<List<TrackFacetInfo>>(default!);
    }

        public Task<HyperFidelFullSearchResult> SearchStructuredAsync(
        HyperFidelServerSettings server,
        string? query,
        string resultType,
        int? minimumYear,
        int? maximumYear,
        long? addedFrom,
        long? addedBefore,
        string sort,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelFullSearchResult>(default!);
    }

        public Task<GenreCloudSnapshot> GetGenreCloudAsync(
        HyperFidelServerSettings server,
        string? parentKey = null,
        int maximumCandidates = 250,
        int candidateOffset = 0,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<GenreCloudSnapshot>(default!);
    }

        public Task<List<HyperFidelTrackInfo>> GetTracksByIdsAsync(
        HyperFidelServerSettings server,
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default,
        bool requireComplete = false)
    {
        return Task.FromResult<List<HyperFidelTrackInfo>>(default!);
    }

        public Task<HyperFidelLyrics?> GetTrackLyricsAsync(
        HyperFidelServerSettings server,
        long trackId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelLyrics?>(default!);
    }

        public Task<bool> UpdateTrackRatingAsync(
        HyperFidelServerSettings server,
        long trackId,
        HyperFidelTrackRatingUpdate update,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<double?> GetTrackPositionAsync(
        HyperFidelServerSettings server,
        long trackId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<double?>(default!);
    }

        public Task<bool> SaveTrackPositionAsync(
        HyperFidelServerSettings server,
        long trackId,
        double positionSeconds,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> UpdateTrackFavoriteAsync(
        HyperFidelServerSettings server,
        long trackId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> UpdateTrackGenreAsync(
        HyperFidelServerSettings server,
        long trackId,
        string? genre,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> UpdateArtistFavoriteAsync(
        HyperFidelServerSettings server,
        long artistId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> UpdateAlbumFavoriteAsync(
        HyperFidelServerSettings server,
        long albumId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<HyperFidelTrackWaveform?> GetTrackWaveformAsync(
        HyperFidelServerSettings server,
        long trackId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelTrackWaveform?>(default!);
    }

        public Task<bool> UploadTrackLyricsAsync(
        HyperFidelServerSettings server,
        long trackId,
        string? plainLyrics,
        string? syncedLyrics,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<List<HyperFidelTrackLiteInfo>> GetTrackFoldersAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<HyperFidelTrackLiteInfo>>(default!);
    }

        public Task<bool> UploadArtistImageAsync(
        HyperFidelServerSettings server,
        long artistId,
        byte[] imageData,
        string? mimeType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> DeleteArtistImageAsync(
        HyperFidelServerSettings server,
        long artistId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<List<string>> GetLibraryPathsAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<string>>(default!);
    }

        public Task<bool> SetLibraryPathsAsync(
        HyperFidelServerSettings server,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<HyperFidelDirectoryListing?> GetDirectoriesAsync(
        HyperFidelServerSettings server,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelDirectoryListing?>(default!);
    }

        public Task<bool> TriggerScanAsync(
        HyperFidelServerSettings server,
        bool forceMetadataRefresh = false,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<HyperFidelReplayGainSettings?> GetReplayGainSettingsAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelReplayGainSettings?>(default!);
    }

        public Task<bool> SetReplayGainSettingsAsync(
        HyperFidelServerSettings server,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<List<HyperFidelLibraryDoctorCandidate>?> GetLibraryDoctorAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default,
        bool inspectFiles = true)
    {
        return Task.FromResult<List<HyperFidelLibraryDoctorCandidate>?>(default!);
    }

        public Task<List<SimilarityFeatureVector>> GetSimilarityFeaturesAsync(
        HyperFidelServerSettings server,
        int page = 0,
        int pageSize = 500,
        CancellationToken cancellationToken = default,
        bool requireComplete = false)
    {
        return Task.FromResult<List<SimilarityFeatureVector>>(default!);
    }

        public Task<bool> TriggerAudioFeatureAnalysisAsync(
        HyperFidelServerSettings server,
        int limit = 4,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task<bool> TriggerReplayGainCalculationAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<bool>(default!);
    }

        public Task DownloadLibraryBackupAsync(
        HyperFidelServerSettings server,
        string destinationPath,
        IProgress<double?>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

        public Task<IReadOnlyList<string>> RestoreLibraryBackupAsync(
        HyperFidelServerSettings server,
        string archivePath,
        IProgress<double?>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(default!);
    }

        public Task<HyperFidelScanStatus?> GetScanStatusAsync(
        HyperFidelServerSettings server,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<HyperFidelScanStatus?>(default!);
    }

        public static string GetStreamUrl(HyperFidelServerSettings server, long trackId)
    {
        return string.Empty;
    }

        public static string GetAlbumArtworkUrl(HyperFidelServerSettings server, long albumId, int size)
    {
        return string.Empty;
    }

        public static string GetArtistArtworkUrl(HyperFidelServerSettings server, long artistId)
    {
        return string.Empty;
    }

        public static string GetTrackArtworkUrl(HyperFidelServerSettings server, long trackId, int? size = null)
    {
        return string.Empty;
    }

    public void Dispose() { }
}
