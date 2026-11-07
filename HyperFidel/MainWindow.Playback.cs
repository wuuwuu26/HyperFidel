using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using HyperFidel.Audio;
using HyperFidel.Controls;
using HyperFidel.Library;
using HyperFidel.Localization;
using HyperFidel.Streaming;
using Windows.Media;

namespace HyperFidel;

/// <summary>
/// Playback engine (start/stop, gapless sessions, and shuffle), transport UI and
/// waveform, lyrics, now-playing metadata, and playback history for
/// <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    // Progress is publishable only after the owning podcast resume seek finishes.
    private bool _podcastResumePrepared;

    // ------------------------------------------------------------------
    // Wiedergabe
    // ------------------------------------------------------------------

    private void ConfigureWindowsMediaTransport()
    {
        _windowsMediaTransport = WindowsMediaTransportService.TryCreate();
        if (_windowsMediaTransport is null)
            return;

        _windowsMediaTransport.PlayRequested += () =>
            Dispatcher.UIThread.Post(async () => await ResumeOrStartPlaybackAsync());
        _windowsMediaTransport.PauseRequested += () =>
            Dispatcher.UIThread.Post(PausePlayback);
        _windowsMediaTransport.PreviousRequested += () =>
            Dispatcher.UIThread.Post(async () => await PlayPreviousAsync());
        _windowsMediaTransport.NextRequested += () =>
            Dispatcher.UIThread.Post(async () => await PlayNextAsync());
        _windowsMediaTransport.StopRequested += () =>
            Dispatcher.UIThread.Post(StopPlayback);
        _windowsMediaTransport.PositionChangeRequested += position =>
            Dispatcher.UIThread.Post(async () => await SeekFromSystemAsync(position));
        _windowsMediaTransport.VolumeChangeRequested += volume =>
            Dispatcher.UIThread.Post(() => ApplySystemVolume((float)volume));
        _windowsMediaTransport.SetVolume(VolumeSlider.Value);
        RefreshQueueNavigationButtons();
    }

    private async void PlayButton_OnClick(object? sender, RoutedEventArgs e) =>
        await TogglePlaybackAsync();

    /// <summary>Toggles the active player or prepares the selected source.</summary>
    /// <returns>The pause/resume action or initial preparation.</returns>
    private async Task TogglePlaybackAsync()
    {
        if (_player is { } player)
        {
            SetPlaybackPaused(!player.IsPaused);
            return;
        }

        await ResumeOrStartPlaybackAsync();
    }

    /// <summary>Resumes the active player or returns when source preparation finishes.</summary>
    /// <returns>The resume action or guarded preparation.</returns>
    private async Task ResumeOrStartPlaybackAsync()
    {
        if (_player is not null)
        {
            SetPlaybackPaused(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            StatusTextBlock.Text = LocalizationManager.Current.SelectTrackFirst;
            return;
        }
        var ready = StartReadyPlaybackAsync(_currentFilePath, _currentRadioStation, _currentPodcastPlayback);
        var intent = _playbackStarts.Current;
        try { await ready; }
        catch (Exception) when (_playbackShuttingDown || !_playbackStarts.IsCurrent(intent)) { }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    /// <summary>Pauses current output without publishing state from a reentrant obsolete player.</summary>
    private void PausePlayback() => SetPlaybackPaused(true);

    /// <summary>Adapts an owned backend pause/resume transition to the transport controls.</summary>
    /// <param name="paused">Requested pause state.</param>
    private void SetPlaybackPaused(bool paused)
    {
        var player = _player;
        var intent = _playbackStarts.Current;
        if (player is null || !_playbackSession.TrySetPaused(intent, player, paused)) return;
        SetPlayPauseIcon(isPlaying: !paused);
        _windowsMediaTransport?.SetPlaybackStatus(paused ? MediaPlaybackStatus.Paused : MediaPlaybackStatus.Playing);
    }

    /// <summary>Tracks a remote transport seek and cancels acknowledgement when output is superseded.</summary>
    /// <param name="seconds">Requested absolute playback position.</param>
    /// <returns>The owned seek completion; current failures propagate to the remote command.</returns>
    private async Task SeekFromRemoteAsync(double seconds)
    {
        var player = _player;
        var intent = _playbackStarts.Current;
        if (player?.CanSeek != true || !_playbackSession.Owns(intent, player)) return;
        using var preparation = _playbackPreparations.Begin();
        try { await player.SeekAsync(TimeSpan.FromSeconds(seconds)); }
        catch (Exception) when (!_playbackSession.Owns(intent, player))
        {
            throw new OperationCanceledException(LocalizationManager.Current.PlaybackStopped);
        }
        if (!_playbackSession.Owns(intent, player))
            throw new OperationCanceledException(LocalizationManager.Current.PlaybackStopped);
    }

    /// <summary>Tracks a system transport seek and rejects obsolete completion before updating playback UI.</summary>
    /// <param name="position">Requested seek position, including the start of the track.</param>
    /// <returns>The seek lifetime included in complete output release.</returns>
    private async Task SeekFromSystemAsync(TimeSpan position)
    {
        var player = _player;
        var intent = _playbackStarts.Current;
        if (player?.CanSeek != true || !_playbackSession.Owns(intent, player))
            return;
        using var preparation = _playbackPreparations.Begin();
        try
        {
            await player.SeekAsync(position);
            if (!_playbackSession.Owns(intent, player)) return;
            RefreshTransport();
            _windowsMediaTransport?.UpdateTimeline(
                player.Position,
                player.Duration,
                force: true);
        }
        catch
        {
            // A rejected system seek request must not interrupt playback.
        }
    }

    /// <summary>Creates a desktop window with an injected backend creation adapter.</summary>
    /// <param name="playbackPlayerFactory">The dispatcher-invoked factory for playback creation.</param>
    internal MainWindow(PlaybackPlayerFactory playbackPlayerFactory) : this()
    {
        ArgumentNullException.ThrowIfNull(playbackPlayerFactory);
        _playbackPlayerFactory = playbackPlayerFactory;
    }

    /// <summary>Returns when preparation finishes, while observing the remaining playback lifetime.</summary>
    /// <param name="filePath">The local path or remote playback reference.</param>
    /// <param name="radioStation">Optional radio metadata.</param>
    /// <param name="podcastPlayback">Optional podcast metadata.</param>
    /// <param name="initialPosition">Requested initial position; a nonzero value overrides saved podcast progress.</param>
    /// <param name="initiallyPaused">Whether to pause the prepared player before publishing readiness.</param>
    /// <param name="onPrepared">Optional synchronous action on the current prepared player.</param>
    /// <returns>The prepared player, or null for an abandoned or unavailable start.</returns>
    private Task<IAudioPlayer?> StartReadyPlaybackAsync(
        string filePath, RadioStationRecord? radioStation = null,
        PodcastPlayback? podcastPlayback = null, TimeSpan initialPosition = default,
        bool initiallyPaused = false, Action<IAudioPlayer>? onPrepared = null)
    {
        var readiness = new PlaybackReadiness();
        var lifetime = StartPlaybackAsync(filePath, radioStation, podcastPlayback, initialPosition,
            initiallyPaused, player =>
            {
                onPrepared?.Invoke(player);
                readiness.Publish(player);
            });
        return readiness.Observe(lifetime, (player, error) =>
        {
            if (_playbackShuttingDown || !ReferenceEquals(_player, player))
                return;
            StopPlayback();
            StatusTextBlock.Text = error is OperationCanceledException
                ? LocalizationManager.Current.PlaybackStopped : error.Message;
        });
    }

    /// <summary>Completes a remote command only after its own player is prepared.</summary>
    /// <param name="filePath">The source to play.</param>
    /// <returns>Preparation completion; abandoned or unavailable starts are cancelled.</returns>
    private async Task StartCommandPlaybackAsync(string filePath)
    {
        var player = await StartReadyPlaybackAsync(filePath);
        if (player is null || !ReferenceEquals(_player, player))
            throw new OperationCanceledException(LocalizationManager.Current.PlaybackStopped);
    }

    /// <summary>Starts the requested source and ignores results superseded by another start or stop.</summary>
    /// <param name="filePath">The local path or remote playback reference.</param>
    /// <param name="radioStation">Optional radio source metadata.</param>
    /// <param name="podcastPlayback">Optional podcast source metadata.</param>
    /// <param name="initialPosition">Requested initial position; a nonzero value overrides saved podcast progress.</param>
    /// <param name="initiallyPaused">Whether to pause before publishing readiness.</param>
    /// <param name="onPrepared">Optional synchronous callback after preparation and ownership checks.</param>
    /// <returns>The playback lifetime, including automatic queue advancement.</returns>
    private async Task StartPlaybackAsync(
        string filePath,
        RadioStationRecord? radioStation = null,
        PodcastPlayback? podcastPlayback = null,
        TimeSpan initialPosition = default,
        bool initiallyPaused = false,
        Action<IAudioPlayer>? onPrepared = null)
    {
        if (_playbackShuttingDown)
            return;
        await _playbackStarts.RunAsync(intent =>
        {
            _playbackPreparations.CancelAll();
            return StartPlaybackIntentAsync(intent, filePath, radioStation, podcastPlayback, initialPosition, initiallyPaused, onPrepared);
        });
    }

    /// <summary>Exposes pending preparation to queue edits without superseding its owning start.</summary>
    /// <param name="intent">The owning start identifier.</param>
    /// <param name="filePath">The local path or remote reference.</param>
    /// <param name="radioStation">Optional radio metadata.</param>
    /// <param name="podcastPlayback">Optional podcast metadata.</param>
    /// <param name="initialPosition">Requested initial position; a nonzero value overrides saved podcast progress.</param>
    /// <param name="initiallyPaused">Whether to pause the prepared player before readiness.</param>
    /// <param name="onPrepared">Optional synchronous current-player callback.</param>
    /// <returns>The owning playback lifetime.</returns>
    private async Task StartPlaybackIntentAsync(
        long intent, string filePath, RadioStationRecord? radioStation,
        PodcastPlayback? podcastPlayback, TimeSpan initialPosition,
        bool initiallyPaused, Action<IAudioPlayer>? onPrepared)
    {
        var publication = new TaskCompletionSource<IAudioPlayer?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _playbackPreparationReady = publication.Task;
        _playbackPreparationIntent = intent;
        try
        {
            await StartPlaybackPreparationAndLifetimeAsync(intent, filePath, radioStation, podcastPlayback,
                initialPosition, initiallyPaused, player =>
                {
                    onPrepared?.Invoke(player);
                    publication.TrySetResult(player);
                });
        }
        finally { publication.TrySetResult(null); }
    }

    /// <summary>Publishes playback state only while its captured start intent remains current.</summary>
    /// <param name="intent">The owning start identifier.</param>
    /// <param name="filePath">The local path or remote reference.</param>
    /// <param name="radioStation">Optional radio metadata.</param>
    /// <param name="podcastPlayback">Optional podcast metadata.</param>
    /// <param name="initialPosition">Requested initial position; a nonzero value overrides saved podcast progress.</param>
    /// <param name="initiallyPaused">Whether to pause the prepared player before readiness.</param>
    /// <param name="onPrepared">Optional synchronous current-player callback.</param>
    /// <returns>The owning playback lifetime.</returns>
    private async Task StartPlaybackPreparationAndLifetimeAsync(
        long intent, string filePath, RadioStationRecord? radioStation,
        PodcastPlayback? podcastPlayback, TimeSpan initialPosition,
        bool initiallyPaused, Action<IAudioPlayer>? onPrepared)
    {
        using var preparation = _playbackPreparations.Begin();
        if (PlaylistReferences.IsTrackReference(filePath))
        {
            var resolvedPath = await ResolveRemoteMcpTrackForPlaybackAsync(filePath, () => _playbackStarts.IsCurrent(intent), preparation.Token);
            if (!_playbackStarts.IsCurrent(intent))
                return;
            if (string.IsNullOrWhiteSpace(resolvedPath))
                throw new InvalidOperationException(LocalizationManager.Current.PlaybackStopped);
            filePath = resolvedPath;
        }
        var previousPlayer = StopPlaybackCore(invalidateStart: false);
        await _audioPlayerRetirement.RetireAsync(previousPlayer);
        if (!_playbackStarts.IsCurrent(intent))
            return;
        _currentFilePath = filePath;
        _browserOutputResume = null;
        _currentRadioStation = radioStation;
        _currentPodcastPlayback = podcastPlayback;
        _podcastResumePrepared = false;
        if (radioStation is null && podcastPlayback is null)
            _playedQueuePaths.Add(filePath);

        if (!_playbackSession.TryBegin(intent, preparation.Token, out var playbackToken))
            return;
        var created = await PrepareCurrentPlaybackPlayerAsync(intent, filePath, radioStation, podcastPlayback, initialPosition, playbackToken);
        if (created is not { } result) return;
        var (player, info) = result;
        _podcastResumePrepared = podcastPlayback is not null;
        _lastPodcastProgressSave = DateTimeOffset.UtcNow;
        PlayButton.IsEnabled   = false;
        PlayButton.IsEnabled   = true;
        SetPlayPauseIcon(isPlaying: true);
        RefreshQueueNavigationButtons();
        PositionSlider.IsEnabled = player.CanSeek;
        DurationTextBlock.Text = FormatTime(player.Duration);
        _ = LoadTransportWaveformAsync(filePath, player.Duration);
        _transportTimer.Start();
        StartMusicBrainzBackgroundEnrichment();

        // Now-playing anzeigen
        var queueMetadata = GetPlaylistMetadata(filePath);
        var filename = podcastPlayback?.Episode.Title ??
                       radioStation?.Name ??
                       queueMetadata?.DisplayTitle ??
                       Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text  = filename;
        NowPlayingArtistBlock.Text = podcastPlayback?.Podcast.Name ??
                                     (radioStation is null
                                         ? SelectedDriverTextBlock.Text
                                         : LocalizationManager.Current.InternetRadio);
        ClearNowPlayingAlbum();
        var usesNativeDsd = player is DsfAudioPlayer or DffAudioPlayer or RemoteDsfAudioPlayer or RemoteDffAudioPlayer
#if !WINDOWS
                            || player is DsfDopAudioPlayer { UsesNativeDsd: true }
                            || player is RemoteDsfDopAudioPlayer { UsesNativeDsd: true }
                            || player is DffDopAudioPlayer { UsesNativeDsd: true }
#endif
            ;
#if !WINDOWS
        var usesDop = player is DsfDopAudioPlayer { UsesNativeDsd: false }
                      or RemoteDsfDopAudioPlayer { UsesNativeDsd: false }
                      or DffDopAudioPlayer { UsesNativeDsd: false };
#else
        const bool usesDop = false;
#endif
        FileInfoTextBlock.Text = player is BrowserAudioPlayer browserOutput
            ? browserOutput.OutputFormat
            : FormatFileInfo(info, usesDop, usesNativeDsd);
        if (radioStation is null && podcastPlayback is null)
        {
            _currentNowPlayingProvider = _localNowPlayingProvider;
            _currentHyperFidelTrackRow = null;
            var isPlexTrack = _plexTracksByUrl.TryGetValue(filePath, out var plexTrack);
            var isHyperFidelTrack = _hyperfidelTracksByUrl.TryGetValue(filePath, out var hyperfidelTrack);
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(filePath);
                var artist = db.GetArtistByTrackPath(filePath);
                var trackInfo = db.GetTrackIdAndFavorite(filePath);
                var navigationIds = db.GetTrackNavigationIds(filePath);
                _currentTrackId = trackInfo?.Id;
                _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
                _currentArtistId = artist?.Id;
                _currentArtistName = artist?.Artist;
                _currentAlbumId = navigationIds.AlbumId;
                NowPlayingArtistButton.IsEnabled = artist is not null;
                LyricsButton.IsEnabled = track is not null;
                ArtistInfoButton.IsEnabled = artist is not null;
                ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
                if (track is not null)
                {
                    NowPlayingTitleBlock.Text = track.Title ?? filename;
                    NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
                    SetNowPlayingAlbum(track.Album, navigationIds.AlbumId, navigationIds.AlbumId is not null);
                }
            }
            catch
            {
                NowPlayingArtworkImage.Source = null;
                LyricsBackgroundImage.Source = null;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = null;
                ClearNowPlayingAlbum();
                NowPlayingArtistButton.IsEnabled = false;
                _currentTrackIsFavorite = false;
                LyricsButton.IsEnabled = false;
                ArtistInfoButton.IsEnabled = false;
            }
            if (isPlexTrack && plexTrack is not null)
            {
                NowPlayingTitleBlock.Text = plexTrack.Title ?? filename;
                NowPlayingArtistBlock.Text = plexTrack.Artist ?? string.Empty;
                NowPlayingArtworkImage.Source = null;
                LyricsBackgroundImage.Source = null;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = plexTrack.Artist;
                SetNowPlayingAlbum(plexTrack.Album, null, canNavigate: false);
                _currentTrackIsFavorite = false;
                NowPlayingArtistButton.IsEnabled = false;
                LyricsButton.IsEnabled = false;
                ArtistInfoButton.IsEnabled = false;
            }
            else if (isHyperFidelTrack && hyperfidelTrack is not null)
            {
                NowPlayingTitleBlock.Text = hyperfidelTrack.Title ?? filename;
                NowPlayingArtistBlock.Text = hyperfidelTrack.Artist ?? string.Empty;
                // Show the list thumbnail immediately if already hydrated; the provider
                // then loads full artwork below so it appears even when the row is not.
                NowPlayingArtworkImage.Source = hyperfidelTrack.Thumbnail ?? hyperfidelTrack.Artwork;
                LyricsBackgroundImage.Source = hyperfidelTrack.Artwork ?? hyperfidelTrack.Thumbnail;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = hyperfidelTrack.Artist;
                SetNowPlayingAlbum(
                    hyperfidelTrack.Album,
                    hyperfidelTrack.AlbumId,
                    hyperfidelTrack.HyperFidelServer is not null && hyperfidelTrack.AlbumId is not null);
                _currentTrackIsFavorite = false;
                _currentHyperFidelTrackRow = hyperfidelTrack;
                if (hyperfidelTrack.HyperFidelServer is { } trackServer)
                {
                    _currentNowPlayingProvider = CreateHyperFidelNowPlayingProvider(trackServer);
                    if (hyperfidelTrack.Id is long favoriteTrackId)
                        _currentTrackIsFavorite = IsHyperFidelFavorite(trackServer, "Track", favoriteTrackId);
                }
                // The now-playing artist button navigates within the remote library
                // using the row's server and artist ID (see NowPlayingArtistButton_OnClick).
                NowPlayingArtistButton.IsEnabled = hyperfidelTrack.ArtistId is not null
                    && !string.IsNullOrWhiteSpace(hyperfidelTrack.Artist);
                LyricsButton.IsEnabled = !string.IsNullOrWhiteSpace(hyperfidelTrack.Title)
                    && !string.IsNullOrWhiteSpace(hyperfidelTrack.Artist);
                ArtistInfoButton.IsEnabled = hyperfidelTrack.ArtistId is not null
                    && !string.IsNullOrWhiteSpace(hyperfidelTrack.Artist);
                ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
                if (LyricsButton.IsEnabled)
                    _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
            }
            else
            {
                _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
            }

            if (!isPlexTrack)
                _ = LoadNowPlayingArtworkAsync(
                    filePath,
                    _currentNowPlayingProvider ?? _localNowPlayingProvider,
                    BuildNowPlayingTrackContext(filePath));
        }
        else if (podcastPlayback is not null)
        {
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = true;
            ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowPodcastInfo);
            _ = LoadPodcastArtworkAsync(
                podcastPlayback.Podcast.ArtworkUrl,
                playbackToken);
        }
        else if (radioStation is not null)
        {
            ShowRadioNowPlaying(radioStation, info);
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            NowPlayingArtistButton.IsEnabled = false;
            _currentTrackIsFavorite = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
            ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
            StartRadioMetadataMonitor(radioStation);
        }
        UpdateNowPlayingFavoriteButton();
        UpdateReplayGainBadge(usesNativeDsd);
        if (podcastPlayback is null && _currentHyperFidelTrackRow is { } remoteResumeRow)
            _ = OfferRemoteResumeAsync(remoteResumeRow, initialPosition.TotalSeconds);
        RefreshWindowsMediaMetadata();
        _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
        _windowsMediaTransport?.UpdateTimeline(
            player.Position,
            player.Duration,
            force: true);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            if (radioStation is not null)
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    null,
                    null,
                    mediaType: "radio",
                    title: radioStation.Name,
                    subtitle: LocalizationManager.Current.InternetRadio,
                    externalId: radioStation.StationUuid);
            }
            else if (podcastPlayback is not null)
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    null,
                    player.Duration.TotalSeconds > 0
                        ? player.Duration.TotalSeconds
                        : podcastPlayback.Episode.FeedDuration?.TotalSeconds,
                    mediaType: "podcast",
                    title: podcastPlayback.Episode.Title,
                    subtitle: podcastPlayback.Podcast.Name,
                    album: podcastPlayback.Podcast.Name,
                    externalId: podcastPlayback.Episode.EpisodeKey);
            }
            else
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    db.GetTrackIdByPath(filePath),
                    player.Duration.TotalSeconds > 0 ? player.Duration.TotalSeconds : null,
                    title: NowPlayingTitleBlock.Text,
                    subtitle: NowPlayingArtistBlock.Text,
                    album: _currentAlbumTitle,
                    externalId: ResolveNowPlayingExternalId(filePath),
                    genre: ResolveNowPlayingGenre(filePath));
            }
        }
        catch
        {
            _currentPlayHistoryId = null;
        }

        if (!_playbackSession.PrepareReadiness(intent, player, initiallyPaused))
            return;
        if (initiallyPaused)
        {
            SetPlayPauseIcon(isPlaying: false);
            _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Paused);
        }
        onPrepared?.Invoke(player);
        preparation.Dispose();
        try
        {
            await player.WaitForCompletionAsync();
        }
        catch (OperationCanceledException) when (!ReferenceEquals(_player, player))
        {
            return;
        }

        if (_playbackStarts.IsCurrent(intent) && ReferenceEquals(_player, player))
        {
            RecordPlaybackEnd(completed: true);
            SavePodcastProgress(completed: true);
            if (radioStation is not null || podcastPlayback is not null || !await TryPlayNextAsync())
            {
                if (!ReferenceEquals(_player, player))
                    return;
                StopPlayback();
                StatusTextBlock.Text = LocalizationManager.Current.PlaybackFinished;
            }
        }
    }

    private Task<IAudioPlayer?>? _playbackPreparationReady;
    private long _playbackPreparationIntent;

    /// <summary>
    /// Optional starting volume for the next prepared player in this async flow.
    /// The non-gapless crossfade sets zero so a new player cannot emit its first
    /// buffers at full volume before the incoming fade begins.
    /// </summary>
    private static readonly AsyncLocal<float?> PendingPlayerInitialVolume = new();

    /// <summary>Prepares seek and queue snapshot before one readiness/history publication.</summary>
    /// <param name="intent">Owning playback intent.</param>
    /// <param name="filePath">Resolved current track.</param>
    /// <param name="radioStation">Optional radio source.</param>
    /// <param name="podcastPlayback">Optional podcast source.</param>
    /// <param name="initialPosition">Requested or already completed resume position.</param>
    /// <param name="playbackToken">Captured preparation cancellation.</param>
    /// <returns>The current fully sought player and format, or null for supersession.</returns>
    private async Task<(IAudioPlayer Player, AudioFileInfo Info)?> PrepareCurrentPlaybackPlayerAsync(
        long intent, string filePath, RadioStationRecord? radioStation,
        PodcastPlayback? podcastPlayback, TimeSpan initialPosition, CancellationToken playbackToken)
    {
        while (_playbackStarts.IsCurrent(intent))
        {
            var created = await CreateCurrentQueuePlaybackPlayerAsync(
                intent, filePath, radioStation is null && podcastPlayback is null, playbackToken);
            if (created is not { } result)
                return null;
            if (!_playbackStarts.IsCurrent(intent))
            {
                await _audioPlayerRetirement.RetireAsync(result.Player);
                return null;
            }
            var (player, info) = result;

            if (!_playbackSession.TryAttach(intent, player))
            {
                await _audioPlayerRetirement.RetireAsync(player);
                return null;
            }
            ApplyCrossfeedSettings(_player);
            ApplyLoudnessNormalizationSettings(
                _player,
                radioStation is not null || podcastPlayback is not null);
            _audioDeviceExplicitlyReleased = false;
            ClearReleasedOutputResumeState();
            UpdateOutputDeviceLockButton();
            if (player is IGaplessAudioPlayer gaplessPlayer)
            {
                gaplessPlayer.StopAfterCurrentTrack = _shuffleEnabled;
                gaplessPlayer.TrackChanged += GaplessPlayer_OnTrackChanged;
            }
            if (!await _playbackSession.SeekAsync(intent, player, initialPosition))
                return null;
            if (!_playbackStarts.IsCurrent(intent) || !ReferenceEquals(_player, player))
                return null;
            UpdateNowPlayingRowHighlights();
            var normalVolume = _settings.OutputBackend == OutputBackend.Wasapi &&
                               _endpointVolumeSynchronizer is { AppliesToOutputPath: true }
                ? 1.0f
                : (float)VolumeSlider.Value;
            _player.Volume = PendingPlayerInitialVolume.Value ?? normalVolume;
            _player.ReplayGainFactor = GetReplayGainFactor(filePath);
            _currentPlaybackDuration = player.Duration;
            if (initialPosition == TimeSpan.Zero &&
                podcastPlayback is not null &&
                podcastPlayback.Podcast.Id > 0 &&
                player.CanSeek)
            {
                try
                {
                    using var db = AudioDatabase.OpenDefault();
                    var progress = db.GetPodcastEpisodeProgress(
                        podcastPlayback.Podcast.Id,
                        podcastPlayback.Episode.EpisodeKey);
                    if (progress is { IsCompleted: false, PositionSeconds: > 5 } &&
                        progress.PositionSeconds < Math.Max(0, player.Duration.TotalSeconds - 10))
                    {
                        if (!await _playbackSession.SeekAsync(intent, player, TimeSpan.FromSeconds(progress.PositionSeconds)))
                            return null;
                    }
                }
                catch
                {
                    // A corrupt or unavailable resume point must not prevent playback.
                }
            }
            if (!_playbackStarts.IsCurrent(intent) || !ReferenceEquals(_player, player))
                return null;
            if (player is IGaplessAudioPlayer && radioStation is null && podcastPlayback is null &&
                !_preparedGaplessQueuePaths.SequenceEqual(BuildGaplessQueuePaths(filePath, true)))
            {
                initialPosition = player.Position;
                ((IGaplessAudioPlayer)player).TrackChanged -= GaplessPlayer_OnTrackChanged;
                var detached = _playbackSession.DetachForRetry(intent, player);
                UpdateOutputDeviceLockButton();
                await _audioPlayerRetirement.RetireAsync(detached);
                continue;
            }
            return result;
        }
        return null;
    }

    private string[] _preparedGaplessQueuePaths = [];

    /// <summary>Retries unpublished gapless creation when its ordered queue snapshot changes.</summary>
    /// <param name="intent">The owning playback start.</param>
    /// <param name="filePath">Current resolved track path.</param>
    /// <param name="allowQueuedTracks">Whether this source may include queued successors.</param>
    /// <param name="token">The captured playback cancellation token.</param>
    /// <returns>A current player with a current queue snapshot, or null for an obsolete or unavailable start.</returns>
    private async Task<(IAudioPlayer Player, AudioFileInfo Info)?> CreateCurrentQueuePlaybackPlayerAsync(
        long intent, string filePath, bool allowQueuedTracks, CancellationToken token)
    {
        while (_playbackStarts.IsCurrent(intent))
        {
            var paths = BuildGaplessQueuePaths(filePath, allowQueuedTracks);
            var created = await _playbackStarts.CreateAsync(intent,
                captured => _playbackPlayerFactory(filePath, allowQueuedTracks, captured), token, _audioPlayerRetirement);
            if (created is not { } result) return null;
            if (result.Player is not IGaplessAudioPlayer ||
                paths.SequenceEqual(BuildGaplessQueuePaths(filePath, allowQueuedTracks)))
            {
                _preparedGaplessQueuePaths = paths;
                return result;
            }
            await _audioPlayerRetirement.RetireAsync(result.Player);
        }
        return null;
    }

    /// <summary>Creates a player using the captured cancellation token and existing backend selection.</summary>
    /// <param name="filePath">The resolved playable path.</param>
    /// <param name="allowQueuedTracks">Whether gapless queued tracks are allowed.</param>
    /// <param name="cancellationToken">The owning playback cancellation token.</param>
    /// <returns>The created player and format, or null when output selection is incomplete.</returns>
    private async Task<(IAudioPlayer Player, AudioFileInfo Info)?> CreatePlaybackPlayerAsync(
        string filePath, bool allowQueuedTracks, CancellationToken cancellationToken)
    {
        var playbackTrack = ResolveGaplessPlaybackItem(filePath);
        if (_browserAudioClientId is { } browserClient)
        {
            if (_currentPodcastPlayback?.Episode.FeedDuration is { } feedDuration && playbackTrack.KnownDuration is null)
                playbackTrack = playbackTrack with { KnownDuration = feedDuration };
            return await CreateBrowserAudioPlayerAsync(playbackTrack, browserClient, _currentRadioStation, _currentPodcastPlayback, cancellationToken);
        }
        var ext = Path.GetExtension(playbackTrack.PlaybackPath);
        IAudioPlayer player;
        AudioFileInfo info;
        var gaplessItems = BuildGaplessPlaybackItems(
            filePath,
            allowQueuedTracks);

        if (_settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio)
        {
            if (!SteinbergAsioStream.IsBackendAvailable(_settings.OutputBackend))
            {
                StatusTextBlock.Text = LocalizationManager.Current.AsioBridgeMissing;
                return null;
            }
            if (string.IsNullOrWhiteSpace(_settings.SelectedDriverName))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectAsioDevice;
                return null;
            }
            if (!_settings.AlwaysConvertDsdToPcm &&
                ext.Equals(".dsf", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DsfAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    cancellationToken);
            else if (!_settings.AlwaysConvertDsdToPcm &&
                     IsRemoteHyperFidelDsdCandidate(filePath))
                (player, info) = await CreateRemoteHyperFidelDsdOrPcmPlayerAsync(filePath, playbackTrack, cancellationToken);
            else if (!_settings.AlwaysConvertDsdToPcm &&
                     ext.Equals(".dff", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DffAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    cancellationToken);
            else
                (player, info) = await FfmpegAudioPlayer.CreateAsync(
                    gaplessItems,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _settings.EqualizerEnabled,
                    _settings.EqualizerProfile,
                    cancellationToken,
                    _settings.MaxOutputSampleRateHz);
        }
        else if (_settings.OutputBackend == OutputBackend.AirPlay)
        {
            if (string.IsNullOrWhiteSpace(_settings.SelectedAirPlayDeviceId))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectAirPlayDevice;
                return null;
            }
            (player, info) = await CreateAirPlayPlayerAsync(playbackTrack, cancellationToken);
        }
        else if (_settings.OutputBackend == OutputBackend.Wasapi)
        {
            if (string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectWasapiDevice;
                return null;
            }
#if !WINDOWS
            if (OperatingSystem.IsLinux() &&
                _settings.DsdOverPcmEnabled &&
                (ext.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                 ext.Equals(".dff", StringComparison.OrdinalIgnoreCase) ||
                 IsRemoteHyperFidelDsdCandidate(filePath)))
            {
                if (Uri.TryCreate(filePath, UriKind.Absolute, out var dopUri) &&
                    dopUri.Scheme is "http" or "https")
                {
                    if (IsRemoteHyperFidelDffCandidate(filePath))
                        (player, info) = await DffDopAudioPlayer.CreateRemoteAsync(
                            filePath,
                            _settings.SelectedWasapiDeviceId,
                            cancellationToken);
                    else
                        (player, info) = await RemoteDsfDopAudioPlayer.CreateAsync(
                            filePath,
                            _settings.SelectedWasapiDeviceId,
                            cancellationToken);
                }
                else if (ext.Equals(".dff", StringComparison.OrdinalIgnoreCase))
                    (player, info) = await DffDopAudioPlayer.CreateLocalAsync(
                        playbackTrack.PlaybackPath,
                        _settings.SelectedWasapiDeviceId,
                        cancellationToken);
                else
                {
                    (player, info) = await DsfDopAudioPlayer.CreateAsync(
                        playbackTrack.PlaybackPath,
                        _settings.SelectedWasapiDeviceId,
                        cancellationToken);
                }
            }
            else
#endif
            (player, info) = await WasapiAudioPlayer.CreateAsync(
                gaplessItems,
                _settings.SelectedWasapiDeviceId,
                _settings.EqualizerEnabled,
                _settings.EqualizerProfile,
                cancellationToken,
                _settings.MaxOutputSampleRateHz);
        }
        else
        {
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.NotImplemented, _settings.OutputBackend);
            return null;
        }

        return (player, info);
    }

    private IReadOnlyList<GaplessPlaybackItem> BuildGaplessPlaybackItems(
        string currentFilePath,
        bool allowQueuedTracks)
    {
        var paths = BuildGaplessQueuePaths(currentFilePath, allowQueuedTracks);
        var items = new List<GaplessPlaybackItem>(paths.Length);
        foreach (var path in paths)
            items.Add(ResolveGaplessPlaybackItem(path));
        return items;
    }

    /// <summary>
    /// Resolves only the ordered paths a gapless session would prepare. This is
    /// the cheap half of <see cref="BuildGaplessPlaybackItems"/> used to compare
    /// snapshots without repeated ReplayGain lookups.
    /// </summary>
    /// <param name="currentFilePath">Current resolved track path.</param>
    /// <param name="allowQueuedTracks">Whether queued successors may be included.</param>
    /// <returns>The ordered gapless paths, always starting with the current track.</returns>
    private string[] BuildGaplessQueuePaths(string currentFilePath, bool allowQueuedTracks)
    {
        if (!allowQueuedTracks ||
            _shuffleEnabled ||
            _queueIndex < 0 ||
            _queueIndex >= _queue.Count ||
            !string.Equals(
                _queue[_queueIndex].FilePath,
                currentFilePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return [currentFilePath];
        }

        var paths = new List<string>();
        for (var index = _queueIndex; index < _queue.Count; index++)
        {
            var path = _queue[index].FilePath;
            if (!_settings.AlwaysConvertDsdToPcm &&
                _settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio &&
                Path.GetExtension(path) is string extension &&
                (extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".dff", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            paths.Add(path);
        }

        return paths.Count == 0 ? [currentFilePath] : [.. paths];
    }

    /// <summary>
    /// Builds pre-known stream characteristics for a remote server track so the
    /// player can skip the FFmpeg probe. Returns <see langword="null"/> for DSD
    /// sources (which take the native remote players) and when the server did not
    /// report a usable sample rate, so those tracks probe as before.
    /// </summary>
    /// <param name="row">Cached remote track row carrying server metadata.</param>
    /// <returns>The pre-known audio info, or <see langword="null"/> to probe.</returns>
    private static KnownAudioInfo? BuildRemotePcmKnownInfo(ContentRow row)
    {
        if (row.SampleRateHz is not > 0)
            return null;

        var format = row.Format?.Trim();
        var sourceExtension = Path.GetExtension(row.SourcePath);
        var fileNameExtension = Path.GetExtension(row.FileName);
        var isDsd =
            string.Equals(format, "DSF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DSDIFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dff", StringComparison.OrdinalIgnoreCase);
        if (isDsd)
            return null;

        return new KnownAudioInfo(
            row.SampleRateHz.Value,
            row.ChannelCount ?? 2,
            string.IsNullOrWhiteSpace(format) ? "pcm" : format.ToLowerInvariant(),
            IsDsd: false,
            format?.ToLowerInvariant());
    }

    private GaplessPlaybackItem ResolveGaplessPlaybackItem(string path)
    {
        if (_plexTracksByUrl.TryGetValue(path, out var plexTrack))
        {
            return new GaplessPlaybackItem(
                path,
                GetPcmOutputGainFactor(),
                SourcePaths: plexTrack.PlexPartUrls,
                KnownDuration: plexTrack.KnownDuration);
        }

        if (_hyperfidelTracksByUrl.TryGetValue(path, out var hyperfidelTrack))
            return new GaplessPlaybackItem(
                path,
                GetPcmOutputGainFactor(),
                KnownDuration: hyperfidelTrack.KnownDuration,
                KnownInfo: BuildRemotePcmKnownInfo(hyperfidelTrack));

        if (!CueSheetParser.IsVirtualPath(path))
            return new GaplessPlaybackItem(path, GetReplayGainFactor(path));

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(path);
            if (track is not null)
            {
                return new GaplessPlaybackItem(
                    path,
                    GetReplayGainFactor(path),
                    track.SourcePath,
                    track.SegmentStart is double start ? TimeSpan.FromSeconds(start) : null,
                    track.SegmentEnd is double end ? TimeSpan.FromSeconds(end) : null);
            }
        }
        catch
        {
        }

        return new GaplessPlaybackItem(path, GetReplayGainFactor(path));
    }

    /// <summary>Probes native remote DSD formats and falls back to PCM using one owning token.</summary>
    /// <param name="filePath">The remote playable path.</param>
    /// <param name="playbackTrack">The resolved PCM fallback item.</param>
    /// <param name="cancellationToken">The owning playback cancellation token.</param>
    /// <returns>The created native or PCM player and format.</returns>
    private async Task<(IAudioPlayer Player, AudioFileInfo Info)> CreateRemoteHyperFidelDsdOrPcmPlayerAsync(
        string filePath,
        GaplessPlaybackItem playbackTrack,
        CancellationToken cancellationToken)
    {
        var driverName = _settings.SelectedDriverName ?? string.Empty;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var (dsfPlayer, dsfInfo) = await RemoteDsfAudioPlayer.CreateAsync(
                filePath,
                _settings.OutputBackend,
                driverName,
                cancellationToken);
            return (dsfPlayer, dsfInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            SeekDiagnostics.Log(
                "remote-dsf-player",
                $"native-skip reason={ex.GetType().Name} message={ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var (dffPlayer, dffInfo) = await RemoteDffAudioPlayer.CreateAsync(
                filePath,
                _settings.OutputBackend,
                driverName,
                cancellationToken);
            return (dffPlayer, dffInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            SeekDiagnostics.Log(
                "remote-dff-player",
                $"native-fallback reason={ex.GetType().Name} message={ex.Message}");
            cancellationToken.ThrowIfCancellationRequested();
            return await FfmpegAudioPlayer.CreateAsync(
                [playbackTrack],
                _settings.OutputBackend,
                driverName,
                _settings.EqualizerEnabled,
                _settings.EqualizerProfile,
                cancellationToken,
                _settings.MaxOutputSampleRateHz);
        }
    }

    /// <summary>
    /// Determines whether a remote HyperFidel Server stream should be routed through
    /// the native DSD players. When the cached server metadata identifies a
    /// concrete non-DSD format it is treated as authoritative so PCM tracks skip
    /// the native DSF/DFF probes and go straight to the gapless FFmpeg PCM path;
    /// only tracks with no usable format metadata fall back to the conservative
    /// stream-URL check.
    /// </summary>
    /// <param name="path">Remote stream URL of the track.</param>
    /// <returns>
    /// <see langword="true"/> when the track may be native DSD; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private bool IsRemoteHyperFidelDsdCandidate(string path)
    {
        if (!_hyperfidelTracksByUrl.TryGetValue(path, out var row))
            return IsConfiguredHyperFidelStreamUrl(path);

        var format = row.Format?.Trim();
        var sourceExtension = Path.GetExtension(row.SourcePath);
        var fileNameExtension = Path.GetExtension(row.FileName);

        if (string.Equals(format, "DSF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DSDIFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dff", StringComparison.OrdinalIgnoreCase))
            return true;

        // Any concrete server-supplied format/extension identifies the track as
        // PCM here, so it must not probe the native DSD players. Each of those
        // probes reads remote header byte ranges, adding two wasted HTTP
        // round-trips before the FFmpeg fallback — the main reason remote
        // playback took several seconds to start on ASIO/cwASIO. Routing PCM
        // tracks through the normal FFmpeg branch also restores gapless playback
        // for them, because that branch uses the full gapless item list instead
        // of a single item. Only genuinely unknown tracks stay conservative.
        var hasKnownFormat =
            !string.IsNullOrWhiteSpace(format) ||
            !string.IsNullOrWhiteSpace(sourceExtension) ||
            !string.IsNullOrWhiteSpace(fileNameExtension);
        return !hasKnownFormat && IsConfiguredHyperFidelStreamUrl(path);
    }

    /// <summary>
    /// Determines whether an HyperFidel Server stream is specifically a DSF
    /// candidate for Linux DoP playback.
    /// </summary>
    /// <param name="path">Remote stream URL of the track.</param>
    /// <returns><see langword="true"/> for known DSF or metadata-less HyperFidel streams.</returns>
    private bool IsRemoteHyperFidelDsfCandidate(string path)
    {
        if (!_hyperfidelTracksByUrl.TryGetValue(path, out var row))
            return IsConfiguredHyperFidelStreamUrl(path);

        var format = row.Format?.Trim();
        return string.Equals(format, "DSF", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(row.SourcePath), ".dsf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(row.FileName), ".dsf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Determines whether a remote HyperFidel Server stream is a known DFF/DSDIFF track.</summary>
    /// <param name="path">Remote stream URL of the track.</param>
    /// <returns><see langword="true"/> when cached metadata identifies DFF/DSDIFF.</returns>
    private bool IsRemoteHyperFidelDffCandidate(string path)
    {
        if (!_hyperfidelTracksByUrl.TryGetValue(path, out var row))
            return false;

        var format = row.Format?.Trim();
        return string.Equals(format, "DFF", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(format, "DSDIFF", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(row.SourcePath), ".dff", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(row.FileName), ".dff", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsConfiguredHyperFidelStreamUrl(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return false;

        foreach (var server in _settings.HyperFidelServers ?? [])
        {
            if (!Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out var baseUri))
                continue;

            if (!string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.Port != baseUri.Port)
            {
                continue;
            }

            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var expectedPrefix = string.IsNullOrEmpty(basePath)
                ? "/api/stream/"
                : $"{basePath}/api/stream/";
            if (uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Resolves a history entry to its remote HyperFidel Server track target.</summary>
    /// <param name="entry">The playback-history entry.</param>
    /// <param name="server">Resolved server settings.</param>
    /// <param name="trackId">Resolved server-side track identifier.</param>
    /// <returns><see langword="true"/> when the entry identifies a configured remote track.</returns>
    private bool TryGetHyperFidelHistoryTarget(
        DailyHistoryEntry entry,
        out HyperFidelServerSettings server,
        out long trackId)
    {
        if (TryParseHyperFidelHistoryExternalId(entry.ExternalId, out var serverId, out trackId))
        {
            var matchingServer = (_settings.HyperFidelServers ?? [])
                .FirstOrDefault(item => string.Equals(item.Id, serverId, StringComparison.Ordinal));
            if (matchingServer is not null)
            {
                server = matchingServer;
                return true;
            }
        }

        return TryGetHyperFidelStreamUrlTarget(entry.Path, out server, out trackId);
    }

    /// <summary>Parses an HyperFidel Server track identifier stored in playback history.</summary>
    /// <param name="externalId">Stored history external identifier.</param>
    /// <param name="serverId">Parsed server identifier.</param>
    /// <param name="trackId">Parsed server-side track identifier.</param>
    /// <returns><see langword="true"/> when the identifier is valid.</returns>
    private static bool TryParseHyperFidelHistoryExternalId(
        string? externalId,
        out string serverId,
        out long trackId)
    {
        serverId = string.Empty;
        trackId = 0;
        if (string.IsNullOrWhiteSpace(externalId))
            return false;

        var parts = externalId.Split(':');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "hyperfidel", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[2], "track", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
        {
            return false;
        }

        serverId = parts[1];
        return !string.IsNullOrWhiteSpace(serverId);
    }

    /// <summary>Resolves a configured HyperFidel Server and track ID from a stream URL.</summary>
    /// <param name="path">Potential remote stream URL.</param>
    /// <param name="server">Resolved server settings.</param>
    /// <param name="trackId">Resolved server-side track identifier.</param>
    /// <returns><see langword="true"/> when the URL points at a configured server stream.</returns>
    private bool TryGetHyperFidelStreamUrlTarget(
        string path,
        out HyperFidelServerSettings server,
        out long trackId)
    {
        server = null!;
        trackId = 0;
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return false;

        foreach (var candidate in _settings.HyperFidelServers ?? [])
        {
            if (!Uri.TryCreate(candidate.BaseUrl, UriKind.Absolute, out var baseUri))
                continue;

            if (!string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.Port != baseUri.Port)
            {
                continue;
            }

            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var expectedPrefix = string.IsNullOrEmpty(basePath)
                ? "/api/stream/"
                : $"{basePath}/api/stream/";
            if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var idText = uri.AbsolutePath[expectedPrefix.Length..].Trim('/');
            var slashIndex = idText.IndexOf('/');
            if (slashIndex >= 0)
                idText = idText[..slashIndex];
            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
                continue;

            server = candidate;
            return true;
        }

        return false;
    }

    /// <summary>Advances queue/history and clears obsolete resume prompts for the active player.</summary>
    /// <param name="sender">Owning gapless player.</param>
    /// <param name="e">New audible track.</param>
    private void GaplessPlayer_OnTrackChanged(object? sender, GaplessTrackChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(sender, _player))
                return;

            ClearRemoteResumePrompt();
            RecordPlaybackEnd(completed: true, _currentPlaybackDuration.TotalSeconds);
            _currentFilePath = e.FilePath;
            _currentPlaybackDuration = e.Info.Duration;
            _playedQueuePaths.Add(e.FilePath);
            UpdateNowPlayingRowHighlights();

            if (_queueIndex + 1 < _queue.Count &&
                string.Equals(
                    _queue[_queueIndex + 1].FilePath,
                    e.FilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _queueIndex++;
            }
            else
            {
                var matchingIndex = Enumerable.Range(0, _queue.Count)
                    .Where(index => string.Equals(
                        _queue[index].FilePath,
                        e.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                    .DefaultIfEmpty(-1)
                    .First();
                if (matchingIndex >= 0)
                    _queueIndex = matchingIndex;
            }

            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            PositionSlider.IsEnabled = _player?.CanSeek == true;
            DurationTextBlock.Text = FormatTime(e.Info.Duration);
            UpdateGaplessNowPlaying(e.FilePath, e.Info);
            StartLocalPlaybackHistory(e.FilePath);
        });
    }

    /// <summary>Formats the transport's codec, rate, and channel summary with localized units.</summary>
    /// <param name="info">Decoded track information.</param>
    /// <param name="usesDop">Whether DoP transport is active.</param>
    /// <param name="usesNativeDsd">Whether native DSD output is active.</param>
    /// <returns>The display text.</returns>
    private static string FormatFileInfo(AudioFileInfo info, bool usesDop, bool usesNativeDsd)
    {
        var channels = string.Format(LocalizationManager.Current.ChannelCountFormat, info.Channels);
        var sourceRate = FormatKhz(info.SourceSampleRate);
        var outputRate = FormatKhz(info.OutputSampleRate);
        if (usesDop)
        {
            return $"{info.ContainerName.ToUpperInvariant()}  ·  {sourceRate}  ·  " +
                   $"{LocalizationManager.Current.DopOutput} ({outputRate})  ·  {channels}";
        }
        if (usesNativeDsd)
        {
            return $"{info.ContainerName.ToUpperInvariant()}  ·  {sourceRate}  ·  " +
                   $"{LocalizationManager.Current.NativeDsdOutput}  ·  {channels}";
        }
        if (info.IsDsd)
        {
            return $"{info.ContainerName.ToUpperInvariant()}  ·  " +
                   $"{LocalizationManager.Current.DsdToPcmOutput}  ·  {outputRate}  ·  {channels}";
        }
        var codec = FormatCodec(info.CodecName);
        return info.SourceSampleRate != info.OutputSampleRate
            ? $"{codec}  ·  {sourceRate} → {outputRate}  ·  {channels}"
            : $"{codec}  ·  {sourceRate}  ·  {channels}";
    }

    private static string FormatCodec(string codecName)
    {
        var codec = codecName.ToUpperInvariant();
        if (!codec.StartsWith("PCM_", StringComparison.Ordinal))
            return codec;
        var suffix = codec["PCM_".Length..];
        var digits = new string(suffix
            .SkipWhile(character => !char.IsAsciiDigit(character))
            .TakeWhile(char.IsAsciiDigit)
            .ToArray());
        return int.TryParse(digits, out var bits)
            ? $"PCM  ·  {string.Format(LocalizationManager.Current.BitDepthFormat, bits)}"
            : "PCM";
    }

    private static string FormatKhz(int rate) =>
        $"{rate / 1000.0:0.###} kHz";

    private void UpdateGaplessNowPlaying(string filePath, AudioFileInfo info)
    {
        var metadata = GetPlaylistMetadata(filePath);
        var filename = metadata?.DisplayTitle ?? Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text = filename;
        NowPlayingArtistBlock.Text = metadata?.Artist ?? SelectedDriverTextBlock.Text;
        ClearNowPlayingAlbum();
        FileInfoTextBlock.Text = FormatFileInfo(info, usesDop: false, usesNativeDsd: false);

        _currentNowPlayingProvider = _localNowPlayingProvider;
        _currentHyperFidelTrackRow = null;
        var isPlexTrack = _plexTracksByUrl.TryGetValue(filePath, out var plexTrack);
        var isHyperFidelTrack = _hyperfidelTracksByUrl.TryGetValue(filePath, out var hyperfidelTrack);
        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(filePath);
            var artist = db.GetArtistByTrackPath(filePath);
            var trackInfo = db.GetTrackIdAndFavorite(filePath);
            var navigationIds = db.GetTrackNavigationIds(filePath);
            _currentTrackId = trackInfo?.Id;
            _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
            _currentArtistId = artist?.Id;
            _currentArtistName = artist?.Artist;
            _currentAlbumId = navigationIds.AlbumId;
            NowPlayingArtistButton.IsEnabled = artist is not null;
            LyricsButton.IsEnabled = track is not null;
            ArtistInfoButton.IsEnabled = artist is not null;
            if (track is not null)
            {
                NowPlayingTitleBlock.Text = track.Title ?? filename;
                NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
                SetNowPlayingAlbum(track.Album, navigationIds.AlbumId, navigationIds.AlbumId is not null);
            }
        }
        catch
        {
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }

        if (isPlexTrack && plexTrack is not null)
        {
            NowPlayingTitleBlock.Text = plexTrack.Title ?? filename;
            NowPlayingArtistBlock.Text = plexTrack.Artist ?? string.Empty;
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = plexTrack.Artist;
            SetNowPlayingAlbum(plexTrack.Album, null, canNavigate: false);
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }
        else if (isHyperFidelTrack && hyperfidelTrack is not null)
        {
            NowPlayingTitleBlock.Text = hyperfidelTrack.Title ?? filename;
            NowPlayingArtistBlock.Text = hyperfidelTrack.Artist ?? string.Empty;
            NowPlayingArtworkImage.Source = hyperfidelTrack.Thumbnail ?? hyperfidelTrack.Artwork;
            LyricsBackgroundImage.Source = hyperfidelTrack.Artwork ?? hyperfidelTrack.Thumbnail;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = hyperfidelTrack.Artist;
            SetNowPlayingAlbum(
                hyperfidelTrack.Album,
                hyperfidelTrack.AlbumId,
                hyperfidelTrack.HyperFidelServer is not null && hyperfidelTrack.AlbumId is not null);
            _currentTrackIsFavorite = false;
            _currentHyperFidelTrackRow = hyperfidelTrack;
            if (hyperfidelTrack.HyperFidelServer is { } trackServer)
            {
                _currentNowPlayingProvider = CreateHyperFidelNowPlayingProvider(trackServer);
                if (hyperfidelTrack.Id is long favoriteTrackId)
                    _currentTrackIsFavorite = IsHyperFidelFavorite(trackServer, "Track", favoriteTrackId);
            }
            // The now-playing artist button navigates within the remote library
            // using the row's server and artist ID (see NowPlayingArtistButton_OnClick).
            NowPlayingArtistButton.IsEnabled = hyperfidelTrack.ArtistId is not null
                && !string.IsNullOrWhiteSpace(hyperfidelTrack.Artist);
            LyricsButton.IsEnabled = !string.IsNullOrWhiteSpace(hyperfidelTrack.Title)
                && !string.IsNullOrWhiteSpace(hyperfidelTrack.Artist);
            ArtistInfoButton.IsEnabled = hyperfidelTrack.ArtistId is not null
                && !string.IsNullOrWhiteSpace(hyperfidelTrack.Artist);
            if (LyricsButton.IsEnabled)
                _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
        }
        else
        {
            _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
        }

        if (!isPlexTrack)
            _ = LoadNowPlayingArtworkAsync(
                filePath,
                _currentNowPlayingProvider ?? _localNowPlayingProvider,
                BuildNowPlayingTrackContext(filePath));

        UpdateNowPlayingFavoriteButton();
        UpdateReplayGainBadge(nativeDsdOutput: false);
        RefreshWindowsMediaMetadata();
        _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
        if (_player is not null)
        {
            _windowsMediaTransport?.UpdateTimeline(
                _player.Position,
                _player.Duration,
                force: true);
        }
    }

    private void RefreshWindowsMediaMetadata()
    {
        if (_windowsMediaTransport is null)
            return;

        var title = NowPlayingTitleBlock.Text ?? string.Empty;
        var artist = NowPlayingArtistBlock.Text ?? string.Empty;
        var album = string.Empty;
        string? artworkPath = null;
        Uri? artworkUri = null;

        if (_currentPodcastPlayback is { } podcastPlayback)
        {
            album = podcastPlayback.Podcast.Name;
            if (Uri.TryCreate(
                    podcastPlayback.Podcast.ArtworkUrl,
                    UriKind.Absolute,
                    out var podcastArtworkUri))
            {
                artworkUri = podcastArtworkUri;
            }
        }
        else if (_currentRadioStation is { } radioStation)
        {
            album = radioStation.Name;
            artworkPath = _currentRadioArtworkPath;
            if (string.IsNullOrWhiteSpace(artworkPath) &&
                Uri.TryCreate(radioStation.Favicon, UriKind.Absolute, out var radioArtworkUri))
            {
                artworkUri = radioArtworkUri;
            }
        }
        else if (_plexTracksByUrl.TryGetValue(_currentFilePath, out var plexTrack))
        {
            album = plexTrack.Album ?? string.Empty;
        }
        else if (_hyperfidelTracksByUrl.TryGetValue(_currentFilePath, out var hyperfidelTrack))
        {
            album = hyperfidelTrack.Album ?? string.Empty;
            if (hyperfidelTrack.HyperFidelServer is { } server &&
                hyperfidelTrack.Id is long trackId)
            {
                var cachedArtwork = AppPaths.GetDataPath(
                    "remote-artworks",
                    $"track-art-{server.Id}-{trackId}.img");
                if (File.Exists(cachedArtwork))
                {
                    artworkPath = cachedArtwork;
                }
                else if (Uri.TryCreate(
                    HyperFidelServerClient.GetTrackArtworkUrl(server, trackId, 320),
                    UriKind.Absolute,
                    out var hyperfidelArtworkUri))
                {
                    artworkUri = hyperfidelArtworkUri;
                }
            }
        }
        else
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(_currentFilePath);
                var artwork = db.GetArtworkPathsByTrackPath(_currentFilePath);
                album = track?.Album ?? string.Empty;
                artworkPath = artwork?.OriginalPath ??
                              artwork?.Thumb320Path ??
                              artwork?.Thumb96Path;
            }
            catch
            {
                // Metadata is optional and must never affect audio playback.
            }
        }

        _ = _windowsMediaTransport.UpdateMetadataAsync(new WindowsMediaMetadata(
            title,
            artist,
            album,
            artworkPath,
            artworkUri));
    }
}
