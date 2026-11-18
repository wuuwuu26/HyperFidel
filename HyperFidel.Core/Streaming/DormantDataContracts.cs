// Dormant, network-free data contracts retained so local settings and data load.
using System.Text.Json.Serialization;
using HyperFidel.Library;

namespace HyperFidel.Streaming;

/// <summary>Identifies a supported streaming provider.</summary>
public enum StreamingProvider
{
    /// <summary>Qobuz high-resolution music streaming.</summary>
    Qobuz
}

/// <summary>Represents an artist from the streaming catalogue.</summary>
/// <param name="ProviderArtistId">Provider-specific artist ID.</param>
/// <param name="Name">Display name of the artist.</param>
/// <param name="ImageUri">Optional URL to the artist image.</param>
public sealed record StreamingArtist(
    string ProviderArtistId,
    string Name,
    Uri? ImageUri = null);

/// <summary>Represents an album from the streaming catalogue.</summary>
/// <param name="ProviderAlbumId">Provider-specific album ID.</param>
/// <param name="Title">Album title.</param>
/// <param name="Artist">Name of the primary artist.</param>
/// <param name="Year">Optional release year.</param>
/// <param name="ArtworkUri">Optional URL to the cover artwork.</param>
public sealed record StreamingAlbum(
    string ProviderAlbumId,
    string Title,
    string Artist,
    int? Year = null,
    Uri? ArtworkUri = null);

/// <summary>Represents a single track from the streaming catalogue.</summary>
/// <param name="ProviderTrackId">Provider-specific track ID.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist name.</param>
/// <param name="Album">Optional album title.</param>
/// <param name="Duration">Optional track duration.</param>
/// <param name="ArtworkUri">Optional URL to the cover artwork.</param>
public sealed record StreamingTrack(
    string ProviderTrackId,
    string Title,
    string Artist,
    string? Album,
    TimeSpan? Duration,
    Uri? ArtworkUri = null);

/// <summary>Contains the category-split results of a catalogue search query.</summary>
/// <param name="Artists">Matching artists.</param>
/// <param name="Albums">Matching albums.</param>
/// <param name="Tracks">Matching tracks.</param>
public sealed record StreamingSearchResult(
    IReadOnlyList<StreamingArtist> Artists,
    IReadOnlyList<StreamingAlbum> Albums,
    IReadOnlyList<StreamingTrack> Tracks);

/// <summary>
/// Time-limited source for playing back a streaming track.
/// </summary>
/// <param name="StreamUri">Direct stream URL.</param>
/// <param name="Codec">Optional codec identifier, e.g. <c>flac</c>.</param>
/// <param name="SampleRate">Optional sample rate in Hz.</param>
/// <param name="BitDepth">Optional bit depth.</param>
/// <param name="RequestHeaders">HTTP headers to send when fetching the stream.</param>
/// <param name="ExpiresAt">Optional expiry timestamp of the URL.</param>
public sealed record StreamingPlaybackSource(
    Uri StreamUri,
    string? Codec,
    int? SampleRate,
    int? BitDepth,
    IReadOnlyDictionary<string, string> RequestHeaders,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Credentials for a streaming provider, stored encrypted at rest.
/// </summary>
/// <param name="ClientSecret">Optional application client secret.</param>
/// <param name="AccessToken">Optional short-lived access token.</param>
/// <param name="RefreshToken">Optional long-lived refresh token.</param>
/// <param name="AccessTokenExpiresAt">Optional expiry timestamp of the access token.</param>
public sealed record StreamingCredential(
    string? ClientSecret,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? AccessTokenExpiresAt);

/// <summary>Connection settings for a single Plex Media Server entry.</summary>
public sealed class PlexServerSettings
{
    /// <summary>Gets or sets the unique server identifier (GUID, no dashes).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Gets or sets the user-chosen display name for this server.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the base URL of the Plex server (e.g. <c>http://192.168.1.10:32400</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Connection settings for one remote HyperFidel Server instance.</summary>
public sealed class HyperFidelServerSettings
{
    /// <summary>Gets or sets the unique server identifier (GUID, no dashes).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Gets or sets the user-chosen display name for this server.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the base URL of the server (e.g. <c>http://192.168.1.10:5280</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Gets or sets the pre-shared API key required by every request.</summary>
    [JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the server-side profile selected for the active local user.</summary>
    [JsonIgnore]
    public string ProfileId { get; set; } = "standard";

    /// <summary>
    /// Gets or sets the requested lossy streaming format (<c>opus</c> or <c>aac</c>),
    /// or <see langword="null"/> to stream the original file.
    /// </summary>
    public string? StreamingFormat { get; set; }

    /// <summary>
    /// Gets or sets the requested lossy streaming bitrate in kilobits per second, or
    /// <see langword="null"/> for the format default.
    /// </summary>
    public int? StreamingBitrateKbps { get; set; }
}

/// <summary>Public server profile identity returned by the profile registry endpoint.</summary>
public sealed record HyperFidelServerProfile(string Id, string Name);

/// <summary>Portable playback-history entry exchanged with an HyperFidel Server.</summary>
public sealed record HyperFidelHistorySyncEntry(
    string SyncId, string Path, long StartedAtUnix, double PositionSeconds,
    double? DurationSeconds, string MediaType, string? Title, string? Subtitle,
    string? Album, string? ExternalId, string? Genre);

public sealed record PlexLibrarySection(string Key, string Title);
/// <summary>Describes one artist, album, track, or folder returned by Plex.</summary>
/// <param name="Key">Plex navigation key.</param>
/// <param name="RatingKey">Stable Plex metadata identifier.</param>
/// <param name="Title">Display title.</param>
/// <param name="Artist">Optional artist name.</param>
/// <param name="Album">Optional album title.</param>
/// <param name="Year">Optional release year.</param>
/// <param name="DurationMilliseconds">Optional logical item duration in milliseconds.</param>
/// <param name="Format">Optional media container or codec.</param>
/// <param name="PartKeys">Ordered direct-media part keys forming the logical track.</param>
/// <param name="IsFolder">Whether the item represents a folder.</param>
public sealed record PlexMediaItem(
    string Key,
    string RatingKey,
    string Title,
    string? Artist,
    string? Album,
    int? Year,
    long? DurationMilliseconds,
    string? Format,
    IReadOnlyList<string> PartKeys,
    bool IsFolder,
    string? ParentRatingKey = null,
    string? GrandparentRatingKey = null);

public sealed record PlexMediaPage(IReadOnlyList<PlexMediaItem> Items, int TotalSize);

/// <summary>Artist entry returned by the HyperFidel Server API.</summary>
/// <param name="Id">Database ID of the artist.</param>
/// <param name="Name">Display name of the artist.</param>
/// <param name="IsFavorite">Whether the artist is marked as favorite.</param>
/// <param name="Biography">Cached artist biography, or <see langword="null"/>.</param>
/// <param name="SourceUrl">Source URL for the cached biography, or <see langword="null"/>.</param>
/// <param name="ProfileLanguage">Language code of the cached biography, or <see langword="null"/>.</param>
/// <param name="ProfileFetchedAt">Unix timestamp of the cached profile refresh, or <see langword="null"/>.</param>
/// <param name="HasBiography">Whether a cached biography exists.</param>
/// <param name="HasImage">Whether a cached artist image exists.</param>
/// <param name="ImageIsManual">Whether the artist image was manually selected.</param>
public sealed record HyperFidelArtistInfo(
    long Id,
    string Name,
    bool IsFavorite,
    string? Biography = null,
    string? SourceUrl = null,
    string? ProfileLanguage = null,
    long? ProfileFetchedAt = null,
    bool HasBiography = false,
    bool HasImage = false,
    bool ImageIsManual = false);

/// <summary>Album entry returned by the HyperFidel Server API.</summary>
/// <param name="Id">Database ID of the album.</param>
/// <param name="Album">Album title.</param>
/// <param name="DisplayArtist">Display artist for the album, or <see langword="null"/>.</param>
/// <param name="Year">Album year, or <see langword="null"/>.</param>
/// <param name="ArtworkPath">Server-side 320-px artwork path, or <see langword="null"/> when the album has no cover.</param>
/// <param name="ThumbnailPath">Server-side 96-px thumbnail path, or <see langword="null"/> when the album has no cover.</param>
/// <param name="IsFavorite">Whether the album is marked as favorite on the server.</param>
/// <param name="ArtistId">Database ID of the album artist, or <see langword="null"/>.</param>
/// <param name="AddedAt">Unix timestamp of the newest library addition associated with the album.</param>
public sealed record HyperFidelAlbumInfo(
    long Id,
    string Album,
    string? DisplayArtist,
    int? Year,
    string? ArtworkPath = null,
    string? ThumbnailPath = null,
    bool IsFavorite = false,
    long? ArtistId = null,
    long? AddedAt = null);

/// <summary>Recently added album entry returned by the HyperFidel Server dashboard endpoint.</summary>
/// <param name="Id">Database album identifier.</param>
/// <param name="Title">Album title.</param>
/// <param name="Artist">Display artist name.</param>
/// <param name="ArtistId">Database album-artist identifier, or <see langword="null"/>.</param>
/// <param name="AddedAt">Unix timestamp of the most recently added track in the album.</param>
/// <param name="HasArtwork">Whether the album has server-side artwork.</param>
public sealed record HyperFidelRecentAlbum(
    long Id,
    string Title,
    string Artist,
    long? ArtistId,
    long AddedAt,
    bool HasArtwork);

/// <summary>Compact remote album metadata used for dashboard recommendations.</summary>
/// <param name="Id">Database album identifier.</param>
/// <param name="Title">Album title.</param>
/// <param name="Artist">Album-artist display name.</param>
/// <param name="ArtistId">Database album-artist identifier, or <see langword="null"/>.</param>
/// <param name="Genres">Distinct semicolon-separated track genres.</param>
/// <param name="AverageBpm">Average positive track BPM, or <see langword="null"/>.</param>
/// <param name="ArtworkPath">Server-side artwork path when present.</param>
/// <param name="IsFavorite">Server-side favorite state.</param>
public sealed record HyperFidelRecommendationAlbum(
    long Id,
    string Title,
    string Artist,
    long? ArtistId,
    string? Genres,
    double? AverageBpm,
    string? ArtworkPath,
    bool IsFavorite);

/// <summary>Track entry returned by the HyperFidel Server API.</summary>
/// <param name="Id">Database ID of the track.</param>
/// <param name="Path">Playable library path.</param>
/// <param name="SourcePath">Physical source path used for file-name and folder display, or <see langword="null"/>.</param>
/// <param name="FileName">File name.</param>
/// <param name="Title">Track title, or <see langword="null"/>.</param>
/// <param name="Artist">Track artist, or <see langword="null"/>.</param>
/// <param name="AlbumArtist">Album artist, or <see langword="null"/>.</param>
/// <param name="Album">Album title, or <see langword="null"/>.</param>
/// <param name="SortTitle">Sort title, or <see langword="null"/>.</param>
/// <param name="Year">Track year, or <see langword="null"/>.</param>
/// <param name="TrackNumber">Track number, or <see langword="null"/>.</param>
/// <param name="TrackTotal">Track total, or <see langword="null"/>.</param>
/// <param name="DiscNumber">Disc number, or <see langword="null"/>.</param>
/// <param name="DiscTotal">Disc total, or <see langword="null"/>.</param>
/// <param name="Genre">Genre text, or <see langword="null"/>.</param>
/// <param name="Duration">Duration in seconds, or <see langword="null"/>.</param>
/// <param name="Bitrate">Bitrate in kbps, or <see langword="null"/>.</param>
/// <param name="SampleRate">Sample rate in Hz, or <see langword="null"/>.</param>
/// <param name="BitDepth">Bit depth, or <see langword="null"/>.</param>
/// <param name="Channels">Channel count, or <see langword="null"/>.</param>
/// <param name="Composer">Composer text, or <see langword="null"/>.</param>
/// <param name="Bpm">Beats per minute, or <see langword="null"/>.</param>
/// <param name="FileSize">File size in bytes, or <see langword="null"/>.</param>
/// <param name="AddedAt">Library-added timestamp in Unix seconds, or <see langword="null"/>.</param>
/// <param name="ReplayGainTrack">Track ReplayGain value, or <see langword="null"/>.</param>
/// <param name="ReplayGainAlbum">Album ReplayGain value, or <see langword="null"/>.</param>
/// <param name="Format">Audio format label, or <see langword="null"/>.</param>
/// <param name="IsFavorite">Whether the track is marked as favorite on the server.</param>
/// <param name="IsCueTrack">Whether the track is a CUE or chapter-based virtual segment; the legacy property name is retained for API compatibility.</param>
/// <param name="ArtistId">Database ID of the primary artist, or <see langword="null"/>.</param>
/// <param name="UserRating">Server-side personal zero-to-five-star rating.</param>
/// <param name="MusicBrainzRating">Cached MusicBrainz community rating.</param>
/// <param name="MusicBrainzRatingVotes">Number of contributing MusicBrainz votes.</param>
/// <param name="MusicBrainzTrackId">MusicBrainz recording identifier.</param>
/// <param name="MusicBrainzRatingFetchedAt">Unix timestamp of the latest MusicBrainz rating lookup.</param>
/// <param name="MusicBrainzGenres">JSON array of supplemental MusicBrainz genres.</param>
/// <param name="MusicBrainzTags">JSON array of supplemental positively voted MusicBrainz tags.</param>
public sealed record HyperFidelTrackInfo(
    long Id,
    string Path,
    string? SourcePath,
    string FileName,
    string? Title,
    string? Artist,
    string? AlbumArtist,
    string? Album,
    string? SortTitle = null,
    int? Year = null,
    int? TrackNumber = null,
    int? TrackTotal = null,
    int? DiscNumber = null,
    int? DiscTotal = null,
    string? Genre = null,
    double? Duration = null,
    int? Bitrate = null,
    int? SampleRate = null,
    int? BitDepth = null,
    int? Channels = null,
    string? Composer = null,
    int? Bpm = null,
    long? FileSize = null,
    long? AddedAt = null,
    string? ReplayGainTrack = null,
    string? ReplayGainAlbum = null,
    string? Format = null,
    bool IsFavorite = false,
    bool IsCueTrack = false,
    long? ArtistId = null,
    long? AlbumId = null,
    int UserRating = 0,
    double? MusicBrainzRating = null,
    int? MusicBrainzRatingVotes = null,
    string? MusicBrainzTrackId = null,
    long? MusicBrainzRatingFetchedAt = null,
    string? MusicBrainzGenres = null,
    string? MusicBrainzTags = null,
    string? CamelotKey = null);

/// <summary>Rating mutation sent to an HyperFidel Server.</summary>
/// <param name="UserRating">Optional personal zero-to-five-star rating.</param>
/// <param name="MusicBrainzTrackId">Optional resolved MusicBrainz recording identifier.</param>
/// <param name="MusicBrainzRating">Optional cached MusicBrainz community rating.</param>
/// <param name="MusicBrainzRatingVotes">Optional MusicBrainz vote count.</param>
/// <param name="MusicBrainzRatingFetchedAt">Optional MusicBrainz lookup timestamp.</param>
/// <param name="MusicBrainzGenres">Optional JSON array of supplemental MusicBrainz genres.</param>
/// <param name="MusicBrainzTags">Optional JSON array of supplemental positively voted tags.</param>
public sealed record HyperFidelTrackRatingUpdate(
    int? UserRating = null,
    string? MusicBrainzTrackId = null,
    double? MusicBrainzRating = null,
    int? MusicBrainzRatingVotes = null,
    long? MusicBrainzRatingFetchedAt = null,
    string? MusicBrainzGenres = null,
    string? MusicBrainzTags = null);


/// <summary>Lightweight remote track entry used for folder-tree construction.</summary>
/// <param name="Id">Database ID of the track.</param>
/// <param name="Path">Playable library path.</param>
/// <param name="SourcePath">Physical source path used for folder grouping.</param>
/// <param name="FileName">File name.</param>
/// <param name="Title">Optional track title.</param>
/// <param name="DiscNumber">Optional disc number.</param>
/// <param name="TrackNumber">Optional track number.</param>
/// <param name="Artist">Track artist, or <see langword="null"/>.</param>
/// <param name="AlbumArtist">Album artist, or <see langword="null"/>.</param>
/// <param name="Album">Album title, or <see langword="null"/>.</param>
/// <param name="Duration">Duration in seconds, or <see langword="null"/>.</param>
/// <param name="Format">Audio format label, or <see langword="null"/>.</param>
/// <param name="IsFavorite">Whether the track is marked as favorite on the server.</param>
/// <param name="ArtistId">Database ID of the primary artist, or <see langword="null"/>.</param>
/// <param name="AlbumId">Database ID of the album, or <see langword="null"/>.</param>
public sealed record HyperFidelTrackLiteInfo(
    long Id,
    string Path,
    string SourcePath,
    string FileName,
    string? Title,
    int? DiscNumber,
    int? TrackNumber,
    string? Artist = null,
    string? AlbumArtist = null,
    string? Album = null,
    double? Duration = null,
    string? Format = null,
    bool IsFavorite = false,
    long? ArtistId = null,
    long? AlbumId = null);

/// <summary>Track search response returned by the HyperFidel Server API.</summary>
/// <param name="Tracks">Matching track rows.</param>
public sealed record HyperFidelTrackSearchResult(IReadOnlyList<HyperFidelTrackInfo> Tracks);

/// <summary>Categorised search response (tracks, albums, artists) from the HyperFidel Server API.</summary>
/// <param name="Tracks">Matching track rows.</param>
/// <param name="Albums">Matching album rows.</param>
/// <param name="Artists">Matching artist rows.</param>
public sealed record HyperFidelFullSearchResult(
    IReadOnlyList<HyperFidelTrackInfo> Tracks,
    IReadOnlyList<HyperFidelAlbumInfo> Albums,
    IReadOnlyList<HyperFidelArtistInfo> Artists);

/// <summary>Cached lyrics for a single track returned by the HyperFidel Server API.</summary>
/// <param name="PlainLyrics">Unsynchronised plain-text lyrics, or <see langword="null"/>.</param>
/// <param name="SyncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
/// <param name="FetchedAt">Unix-seconds timestamp of the last lyrics lookup, or <see langword="null"/>.</param>
public sealed record HyperFidelLyrics(string? PlainLyrics, string? SyncedLyrics, long? FetchedAt = null);

/// <summary>
/// Profile-scoped last playback position of a remote track, used to resume it on
/// another device.
/// </summary>
/// <param name="PositionSeconds">
/// Stored position in seconds, or <see langword="null"/> when the track was never
/// played on another device.
/// </param>
public sealed record TrackPositionDto(double? PositionSeconds);

/// <summary>Compact waveform peak data returned by a remote HyperFidel Server.</summary>
/// <param name="Version">Cache format version.</param>
/// <param name="DurationSeconds">Logical track duration in seconds.</param>
/// <param name="SampleCount">Number of peak buckets.</param>
/// <param name="Peaks">Normalized peak amplitudes in the range 0..1.</param>
public sealed record HyperFidelTrackWaveform(
    int Version,
    double DurationSeconds,
    int SampleCount,
    float[] Peaks);

/// <summary>Playlist entry returned by a remote HyperFidel Server.</summary>
/// <param name="Id">Playlist database ID.</param>
/// <param name="Name">Playlist display name.</param>
/// <param name="Description">Optional playlist description.</param>
/// <param name="TrackCount">Number of tracks in the playlist.</param>
/// <param name="IsSmartPlaylist">Whether the playlist is backed by smart criteria.</param>
/// <param name="CreatedAt">Creation timestamp in Unix seconds.</param>
/// <param name="ModifiedAt">Modification timestamp in Unix seconds.</param>
/// <param name="FilterCriteria">Serialized smart-playlist criteria JSON, or <see langword="null"/> for a regular playlist.</param>
public sealed record HyperFidelPlaylistInfo(
    long Id,
    string Name,
    string? Description,
    int TrackCount,
    bool IsSmartPlaylist,
    long CreatedAt,
    long ModifiedAt,
    string? FilterCriteria = null);

/// <summary>Track row inside a remote HyperFidel Server playlist.</summary>
/// <param name="PlaylistEntryId">Playlist entry database ID.</param>
/// <param name="Position">One-based playlist position.</param>
/// <param name="Path">Stored server-side path.</param>
/// <param name="Track">Resolved track metadata, or <see langword="null"/> when the original file no longer exists.</param>
public sealed record HyperFidelPlaylistTrackInfo(long PlaylistEntryId, int Position, string Path, HyperFidelTrackInfo? Track);

/// <summary>Request body for creating a regular playlist on a remote HyperFidel Server.</summary>
/// <param name="Name">Playlist display name.</param>
/// <param name="TrackIds">Initial server-side track IDs.</param>
public sealed record HyperFidelPlaylistCreateRequest(string Name, IReadOnlyList<long> TrackIds);

/// <summary>Request body for appending tracks to a regular playlist on a remote HyperFidel Server.</summary>
/// <param name="TrackIds">Server-side track IDs to append.</param>
public sealed record HyperFidelPlaylistTrackAppendRequest(IReadOnlyList<long> TrackIds);

/// <summary>Request body for creating or updating a smart playlist on a remote HyperFidel Server.</summary>
/// <param name="Name">Playlist display name.</param>
/// <param name="FilterCriteria">Serialized smart-playlist criteria JSON.</param>
public sealed record HyperFidelSmartPlaylistSaveRequest(string Name, string FilterCriteria);

/// <summary>Request body for resolving a remote smart playlist using client-side favourites.</summary>
/// <param name="FavoriteTrackIds">Server-side track IDs the client treats as favourites.</param>
public sealed record HyperFidelSmartPlaylistResolveRequest(IReadOnlyList<long> FavoriteTrackIds);

/// <summary>Request body for counting how many server tracks match ad-hoc smart-playlist criteria.</summary>
/// <param name="Criteria">Serialized smart-playlist criteria JSON.</param>
/// <param name="FavoriteTrackIds">Server-side track IDs the client treats as favourites.</param>
public sealed record HyperFidelSmartCriteriaResolveRequest(string Criteria, IReadOnlyList<long> FavoriteTrackIds);

/// <summary>Match-count response returned by the smart-criteria resolve endpoint.</summary>
/// <param name="Count">Number of tracks that match the supplied criteria.</param>
public sealed record HyperFidelSmartCriteriaCount(int Count);

/// <summary>Server info returned by <c>/api/info</c>.</summary>
/// <param name="Name">Configured server display name.</param>
/// <param name="Version">Build-time informational version.</param>
/// <param name="ApiVersion">Server API compatibility version.</param>
/// <param name="OperatingSystem">Runtime operating-system identifier.</param>
/// <param name="Architecture">Runtime process architecture.</param>
/// <param name="InstallType">Package/install type such as <c>deb</c>, <c>rpm</c>, or <c>portable</c>.</param>
/// <param name="UpdateSupported">Whether this installation supports managed package updates.</param>
public sealed record HyperFidelServerInfo(
    string Name,
    string Version,
    int ApiVersion,
    string? OperatingSystem = null,
    string? Architecture = null,
    string? InstallType = null,
    bool UpdateSupported = false);

/// <summary>Update state returned by a remote HyperFidel Server.</summary>
/// <param name="Enabled">Whether remote updates are enabled in server configuration.</param>
/// <param name="Supported">Whether the installation supports managed updates.</param>
/// <param name="InstallType">Server package type.</param>
/// <param name="Architecture">Server architecture.</param>
/// <param name="Staged">Whether an update package is staged.</param>
/// <param name="Ready">Whether installation has been requested.</param>
/// <param name="Status">Last privileged-updater status.</param>
public sealed record HyperFidelServerUpdateStatus(
    bool Enabled,
    bool Supported,
    string InstallType,
    string Architecture,
    bool Staged,
    bool Ready,
    string? Status);

/// <summary>
/// Feature-support flags probed on a remote HyperFidel Server so the client can warn when an
/// older server lacks specific endpoints. Each value is <see langword="true"/> (supported),
/// <see langword="false"/> (missing), or <see langword="null"/> (probe inconclusive).
/// </summary>
/// <param name="TrackFacets">Whether the <c>/api/tracks/facets</c> endpoint exists.</param>
/// <param name="RecentAlbums">Whether the <c>/api/albums/recent</c> endpoint exists.</param>
/// <param name="Waveforms">Whether the <c>/api/tracks/{id}/waveform</c> endpoint exists.</param>
public sealed record HyperFidelServerCapabilities(bool? TrackFacets, bool? RecentAlbums, bool? Waveforms);

/// <summary>Library path configuration returned by the HyperFidel Server API.</summary>
public sealed record HyperFidelLibraryPaths(IReadOnlyList<string> Paths);

/// <summary>ReplayGain analysis settings returned by the HyperFidel Server API.</summary>
/// <param name="CalculateMissingReplayGainDuringScan">Whether server scans calculate missing ReplayGain values.</param>
public sealed record HyperFidelReplayGainSettings(bool CalculateMissingReplayGainDuringScan);

/// <summary>Directory listing returned by the HyperFidel Server API.</summary>
public sealed record HyperFidelDirectoryListing(
    string Path,
    bool IsRoot,
    IReadOnlyList<HyperFidelDirectoryEntry> Directories);

/// <summary>Single server-side directory entry returned by the HyperFidel Server API.</summary>
public sealed record HyperFidelDirectoryEntry(
    string Path,
    string Name,
    bool HasChildren);

/// <summary>Summary of a completed scan reported by a remote HyperFidel Server.</summary>
public sealed record HyperFidelScanResult(
    int Total,
    int Added,
    int Updated,
    int Removed,
    int Failed);

/// <summary>Current scan progress reported by a remote HyperFidel Server.</summary>
/// <param name="IsRunning">Whether a scan is currently running.</param>
/// <param name="Path">Library root currently being scanned.</param>
/// <param name="Current">Number of processed files in the current root.</param>
/// <param name="Total">Total files discovered in the current root.</param>
/// <param name="CurrentFile">File currently being processed.</param>
/// <param name="LastResult">Summary of the last completed root scan.</param>
/// <param name="Error">Last scan error, if any.</param>
/// <param name="LibraryChangedAt">Unix timestamp of the last scan that changed indexed tracks.</param>
public sealed record HyperFidelScanStatus(
    bool IsRunning,
    string? Path,
    int Current,
    int Total,
    string? CurrentFile,
    HyperFidelScanResult? LastResult,
    string? Error,
    long? LibraryChangedAt = null);

/// <summary>Compact Library Doctor finding returned by an HyperFidel Server.</summary>
/// <param name="Code">Stable finding code.</param>
/// <param name="Severity">Finding severity.</param>
/// <param name="Count">Number of affected items.</param>
/// <param name="RepairCapability">Supported repair category.</param>
public sealed record HyperFidelLibraryDoctorFinding(
    string Code,
    LibraryDoctorSeverity Severity,
    int Count,
    LibraryDoctorRepairCapability RepairCapability);

/// <summary>Compact server-side Library Doctor result for one physical folder.</summary>
/// <param name="FolderPath">Physical folder path on the server.</param>
/// <param name="TrackCount">Number of indexed tracks in the folder.</param>
/// <param name="HighestSeverity">Highest severity among the findings.</param>
/// <param name="Findings">Typed findings without track payloads or credentials.</param>
public sealed record HyperFidelLibraryDoctorCandidate(
    string FolderPath,
    int TrackCount,
    LibraryDoctorSeverity HighestSeverity,
    IReadOnlyList<HyperFidelLibraryDoctorFinding> Findings);

/// <summary>Request body used to store a client-refreshed artist profile on a remote HyperFidel Server.</summary>
/// <param name="Biography">Downloaded artist biography, or <see langword="null"/> when no biography was found.</param>
/// <param name="SourceUrl">Canonical source URL, or <see langword="null"/>.</param>
/// <param name="Language">Preferred profile language.</param>
/// <param name="ImageData">Optional client-downloaded artist image bytes.</param>
/// <param name="ImageMimeType">MIME type for <paramref name="ImageData"/>.</param>
public sealed record ArtistProfileUpdateRequest(
    string? Biography,
    string? SourceUrl,
    string Language,
    byte[]? ImageData,
    string? ImageMimeType);

/// <summary>Request body for renaming or merging an artist on a remote HyperFidel Server.</summary>
/// <param name="ArtistName">Requested artist display name.</param>
/// <param name="PreferredArtistId">Artist ID whose profile should survive a merge, or <see langword="null"/> to only rename or detect a collision.</param>
public sealed record HyperFidelArtistRenameRequest(string ArtistName, long? PreferredArtistId = null);

/// <summary>Response body for a remote artist rename or merge request.</summary>
/// <param name="Result">Committed rename result, or <see langword="null"/> when a matching artist must be confirmed first.</param>
/// <param name="MatchingArtist">Matching artist that would be merged, or <see langword="null"/> when the rename was committed.</param>
public sealed record HyperFidelArtistRenameResponse(ArtistRenameResult? Result, HyperFidelArtistInfo? MatchingArtist);

/// <summary>
/// HTTP client for browsing and streaming a remote HyperFidel Server library.
/// Each instance is stateless; methods accept <see cref="HyperFidelServerSettings"/> directly.
/// </summary>
