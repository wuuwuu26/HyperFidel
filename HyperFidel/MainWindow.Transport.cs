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
/// Transport controls, position slider, waveform, lyrics, and now-playing artwork.
/// </summary>
public partial class MainWindow : Window
{
    private void TransportControlsHeaderGrid_OnLayoutChanged(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateResponsivePlaybackControls);

    private void TransportControlsHeaderGrid_OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateResponsivePlaybackControls);

    private void UpdateResponsivePlaybackControls()
    {
        const double gap = 12;
        var centeredPlaybackLeft =
            (TransportControlsHeaderGrid.Bounds.Width - PlaybackControlsPanel.Bounds.Width) / 2;
        var requiredLeft = TransportActionPanel.Bounds.Width + gap;
        var shift = Math.Max(0, requiredLeft - centeredPlaybackLeft);
        var maximumShift = Math.Max(
            0,
            TransportControlsHeaderGrid.Bounds.Width -
            PlaybackControlsPanel.Bounds.Width -
            centeredPlaybackLeft);

        if (PlaybackControlsPanel.RenderTransform is Avalonia.Media.TranslateTransform translate)
            translate.X = Math.Min(shift, maximumShift);
    }

    private void SetPlayPauseIcon(bool isPlaying)
    {
        _isPlaying = isPlaying;
        PlayPauseIcon.Data = Geometry.Parse(isPlaying
            ? "M 6 4 H 9 V 16 H 6 Z M 11 4 H 14 V 16 H 11 Z"
            : "M 8 4 L 17 10 L 8 16 Z");
    }

    // ------------------------------------------------------------------
    // Pause / Seek / Volume
    // ------------------------------------------------------------------

    private async void PositionSlider_OnPreviewMouseLeftButtonUp(object? sender, PointerReleasedEventArgs e) =>
        await CommitPositionSliderSeekAsync(e.Pointer);

    private void PositionSlider_OnPreviewMouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (_player?.CanSeek != true || PositionSlider.Bounds.Width <= 0)
            return;

        _isSeekingWithSlider = true;
        _positionSliderSeekStartedAt = DateTimeOffset.UtcNow;
        e.Pointer.Capture(PositionSlider);
        PositionSlider.SetValueFromPoint(e.GetPosition(PositionSlider));
        e.Handled = true;
    }

    private async void PositionSlider_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSeekingWithSlider || _player?.CanSeek != true)
        {
            return;
        }
        if (!e.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed)
        {
            await CommitPositionSliderSeekAsync(e.Pointer);
            return;
        }

        PositionSlider.SetValueFromPoint(e.GetPosition(PositionSlider));
        e.Handled = true;
    }

    private void PositionSlider_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isSeekingWithSlider)
            return;

        _isSeekingWithSlider = false;
        RefreshTransport();
    }

    private void PositionSlider_OnValueChanged(object? sender, EventArgs e)
    {
        if (_isSeekingWithSlider)
            CurrentTimeTextBlock.Text = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
    }

    private void RefreshTransport()
    {
        if (_player is BrowserAudioPlayer browserOutput)
        {
            FileInfoTextBlock.Text = browserOutput.OutputFormat;
            PositionSlider.IsEnabled = browserOutput.CanSeek;
            _currentPlaybackDuration = browserOutput.Duration;
        }
        if (_player is null) return;
        if (_isSeekingWithSlider &&
            DateTimeOffset.UtcNow - _positionSliderSeekStartedAt > TimeSpan.FromSeconds(5))
        {
            _isSeekingWithSlider = false;
        }

        var visiblePosition = _pendingTransportSeekPosition ?? _player.Position;
        CurrentTimeTextBlock.Text = FormatTime(visiblePosition);
        DurationTextBlock.Text    = FormatTime(_player.Duration);
        PositionSlider.Maximum    = Math.Max(1, _player.Duration.TotalSeconds);
        if (!_isSeekingWithSlider)
            PositionSlider.Value = Math.Min(PositionSlider.Maximum, visiblePosition.TotalSeconds);
        _windowsMediaTransport?.UpdateTimeline(visiblePosition, _player.Duration);
        if (_currentPodcastPlayback is not null &&
            DateTimeOffset.UtcNow - _lastPodcastProgressSave >= TimeSpan.FromSeconds(5))
        {
            SavePodcastProgress(completed: false);
        }
        UpdateActiveLyric(_player.Position);
        PublishRemoteTrackPosition(_player.Position);
        EnsureInfiniteMixQueue();
        MaybeStartNonGaplessFadeTransition(visiblePosition);
    }

    /// <summary>Commits the pending waveform-progress seek and leaves preview mode.</summary>
    /// <param name="pointer">Pointer that owns capture, or <see langword="null"/>.</param>
    /// <returns>The tracked seek lifetime; obsolete completion cannot publish transport state.</returns>
    private async Task CommitPositionSliderSeekAsync(IPointer? pointer)
    {
        if (!_isSeekingWithSlider)
            return;
        var player = _player;
        var intent = _playbackStarts.Current;
        if (player is null || !_playbackSession.Owns(intent, player)) return;
        using var preparation = _playbackPreparations.Begin();

        var target = TimeSpan.FromSeconds(PositionSlider.Value);
        var seekVersion = Interlocked.Increment(ref _transportSeekVersion);
        var stopwatch = Stopwatch.StartNew();
        _pendingTransportSeekPosition = target;
        _isSeekingWithSlider = false;
        pointer?.Capture(null);
        CurrentTimeTextBlock.Text = FormatTime(target);
        PositionSlider.Value = Math.Min(PositionSlider.Maximum, target.TotalSeconds);
        var durationForLog = _player?.Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) ?? "none";
        SeekDiagnostics.Log(
            "transport-ui",
            $"seek-request version={seekVersion} target={target.TotalSeconds:F3}s duration={durationForLog}s canSeek={_player?.CanSeek} path={SeekDiagnostics.SanitizeUrl(_currentFilePath)}");
        try
        {
            if (seekVersion != Volatile.Read(ref _transportSeekVersion))
            {
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-skipped-stale version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
                return;
            }
            if (player.CanSeek)
            {
                await player.SeekAsync(target);
                if (!_playbackSession.Owns(intent, player)) return;
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-complete version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds} playerPosition={player.Position.TotalSeconds:F3}s");
            }
            else
            {
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-skipped-unavailable version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
            }
        }
        catch (OperationCanceledException)
        {
            // Track changes and stop requests can cancel an in-flight seek.
            SeekDiagnostics.Log(
                "transport-ui",
                $"seek-canceled version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception) when (!_playbackSession.Owns(intent, player) ||
                                seekVersion != Volatile.Read(ref _transportSeekVersion))
        {
            // An obsolete seek must not report an error over a newer source.
        }
        catch (Exception ex)
        {
            SeekDiagnostics.Log(
                "transport-ui",
                $"seek-failed version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}",
                ex);
            CrashLogger.Log(ex, "Transport seek");
            StatusTextBlock.Text = ex.Message;
        }
        finally
        {
            if (_playbackSession.Owns(intent, player) && seekVersion == Volatile.Read(ref _transportSeekVersion))
            {
                _pendingTransportSeekPosition = null;
                RefreshTransport();
                _windowsMediaTransport?.UpdateTimeline(
                    player.Position,
                    player.Duration,
                    force: true);
            }
        }
    }

    /// <summary>Loads compact waveform data for the current transport item.</summary>
    /// <param name="filePath">Playback path for the current item.</param>
    /// <param name="duration">Known playback duration.</param>
    private async Task LoadTransportWaveformAsync(string filePath, TimeSpan duration)
    {
        CancelAndDispose(ref _waveformCts);
        PositionSlider.SetWaveform(null);
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var source = new CancellationTokenSource();
        _waveformCts = source;
        try
        {
            var samples = await LoadWaveformPeaksAsync(filePath, duration, source.Token);
            if (!source.IsCancellationRequested &&
                string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase) &&
                samples.Count > 0)
            {
                PositionSlider.SetWaveform(samples);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Transport waveform analysis");
        }
    }

    /// <summary>Loads waveform peak data for a local or remote HyperFidel track.</summary>
    /// <param name="filePath">Playback path for the current item.</param>
    /// <param name="duration">Known playback duration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Normalized waveform peaks, or an empty list when unavailable.</returns>
    private async Task<IReadOnlyList<float>> LoadWaveformPeaksAsync(
        string filePath,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (_hyperfidelTracksByUrl.TryGetValue(filePath, out var remoteTrack) &&
            remoteTrack.HyperFidelServer is { } server &&
            remoteTrack.Id is long trackId)
        {
            var waveform = await _hyperfidelClient.GetTrackWaveformAsync(
                server,
                trackId,
                cancellationToken);
            if (waveform?.Peaks is { Length: > 0 } peaks)
                return peaks;

            var fallback = await WaveformCache.GetOrCreateStreamAsync(
                $"hyperfidel-server:{server.Id}:{trackId}",
                filePath,
                duration,
                900,
                cancellationToken);
            return fallback?.Peaks ?? [];
        }

        if (CueSheetParser.IsVirtualPath(filePath))
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(filePath);
                if (track is { Duration: > 0 })
                {
                    var cueData = await WaveformCache.GetOrCreateAsync(
                        track.Path,
                        track.SourcePath,
                        TimeSpan.FromSeconds(track.Duration.Value),
                        900,
                        track.SegmentStart is double start ? TimeSpan.FromSeconds(start) : null,
                        track.SegmentEnd is double end ? TimeSpan.FromSeconds(end) : null,
                        cancellationToken);
                    return cueData?.Peaks ?? [];
                }
            }
            catch
            {
                return [];
            }
        }

        if (!File.Exists(filePath))
        {
            return [];
        }

        var data = await WaveformCache.GetOrCreateAsync(
            filePath,
            null,
            duration,
            900,
            cancellationToken: cancellationToken);
        return data?.Peaks ?? [];
    }

    /// <summary>Clears the old waveform before cancellation callbacks can start replacement analysis.</summary>
    private void ClearTransportWaveform()
    {
        PositionSlider.SetWaveform(null);
        CancelAndDispose(ref _waveformCts);
    }

    private void LyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        LyricsView.IsVisible = !(LyricsView.IsVisible);
        if (!LyricsView.IsVisible)
        {
            SetLyricsImmersive(false);
            StopLyricsScrollAnimation();
        }
        UpdateBackButtonForDetailView();
        if (LyricsView.IsVisible &&
            _lyricLines.Count == 0 &&
            !string.IsNullOrWhiteSpace(_currentFilePath))
        {
            _ = LoadLyricsForTrackAsync(_currentFilePath, forceRefresh: false);
        }
    }

    private void CloseLyricsButton_OnClick(object? sender, RoutedEventArgs e)
        => CloseNowPlayingDetailViews();

    private async void RefreshLyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;
        await LoadLyricsForTrackAsync(_currentFilePath, forceRefresh: true);
    }

    /// <summary>Builds the now-playing track context for the active metadata provider.</summary>
    /// <param name="filePath">Local path or remote stream URL of the current track.</param>
    /// <returns>Context populated from the remote track row when remote, otherwise just the path.</returns>
    private NowPlayingTrackContext BuildNowPlayingTrackContext(string filePath)
    {
        if (_currentHyperFidelTrackRow is { } row &&
            string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return new NowPlayingTrackContext(
                row.Id,
                filePath,
                row.Title,
                row.Artist,
                row.Album,
                row.KnownDuration?.TotalSeconds);
        }

        return new NowPlayingTrackContext(null, filePath, null, null, null, null);
    }

    /// <summary>
    /// Loads the transport cover and lyrics background for the current track through the
    /// active now-playing metadata provider, so local and remote tracks behave the same.
    /// </summary>
    /// <param name="filePath">Local path or remote stream URL of the current track.</param>
    /// <param name="provider">Provider for the current track's source.</param>
    /// <param name="context">Now-playing track context.</param>
    private async Task LoadNowPlayingArtworkAsync(
        string filePath,
        INowPlayingMetadataProvider provider,
        NowPlayingTrackContext context)
    {
        var token = _playbackSession.Token;
        try
        {
            var artwork = await provider.GetArtworkAsync(context, token);
            if (token.IsCancellationRequested ||
                !string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var thumbnailPath = artwork?.ThumbnailPath ?? artwork?.LargePath;
            var largePath = artwork?.LargePath ?? artwork?.ThumbnailPath;

            // Clear the cover only when the provider authoritatively returned no artwork
            // (local). For remote, keep any list thumbnail already shown if the fetch failed.
            if (thumbnailPath is not null || provider is LocalNowPlayingMetadataProvider)
                NowPlayingArtworkImage.Source = CreateArtworkImage(thumbnailPath, 96);
            if (largePath is not null || provider is LocalNowPlayingMetadataProvider)
                LyricsBackgroundImage.Source = CreateArtworkImage(largePath, 900);
            RefreshWindowsMediaMetadata();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task LoadLyricsForTrackAsync(string filePath, bool forceRefresh)
    {
        CancelLyricsLoad();
        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        _activeLyricIndex = -1;
        RefreshLyricsButton.IsEnabled = false;
        ShowLyricsStatus(LocalizationManager.Current.LyricsLoading);

        try
        {
            var provider = _currentNowPlayingProvider ?? _localNowPlayingProvider;
            var context = BuildNowPlayingTrackContext(filePath);

            var cached = await provider.GetCachedLyricsAsync(context, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (cached is null && provider is LocalNowPlayingMetadataProvider)
            {
                // A local track with no database row cannot be looked up at all.
                ClearLyrics();
                ShowLyricsStatus(LocalizationManager.Current.LyricsUnavailable);
                return;
            }

            var hasLocalLyrics = cached is { HasLyrics: true }
                && ApplyLyricsContent(cached.Plain, cached.Synced);
            if (cached is null || !cached.HasLyrics)
                ClearLyrics();

            var fetchedAt = cached?.FetchedAt is long timestamp
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : (DateTimeOffset?)null;
            var lookupExpired = fetchedAt is null ||
                fetchedAt < DateTimeOffset.UtcNow.AddDays(-30);
            var shouldDownload = forceRefresh ||
                (string.IsNullOrWhiteSpace(cached?.Synced) && lookupExpired);
            if (!shouldDownload)
            {
                if (!hasLocalLyrics)
                    ShowLyricsStatus(LocalizationManager.Current.LyricsNotFound);
                return;
            }

            if (!hasLocalLyrics)
                ShowLyricsStatus(LocalizationManager.Current.LyricsDownloading);

            var result = await provider.DownloadLyricsAsync(context, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                return;

            if (result is not { HasLyrics: true } || !ApplyLyricsContent(result.Plain, result.Synced))
                ShowLyricsStatus(LocalizationManager.Current.LyricsNotFound);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_lyricLines.Count == 0)
                ShowLyricsStatus(LocalizationManager.Current.LyricsDownloadFailed);
        }
        finally
        {
            if (_lyricsCts == cts)
            {
                RefreshLyricsButton.IsEnabled = true;
                _lyricsCts = null;
            }
            cts.Dispose();
        }
    }

    private bool ApplyLyrics(TrackRecord track)
        => ApplyLyricsContent(track.DownloadedLyrics ?? track.Lyrics, track.SyncedLyrics);

    /// <summary>Renders the lyrics view from plain and synchronised lyrics text.</summary>
    /// <param name="plainLyrics">Unsynchronised plain-text lyrics, or <see langword="null"/>.</param>
    /// <param name="syncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when at least one lyric line was rendered.</returns>
    private bool ApplyLyricsContent(string? plainLyrics, string? syncedLyrics)
    {
        ClearLyrics();
        // Embedded LRC tags are sometimes stored in the plain lyrics field instead of
        // the synchronized field, so try both before falling back to raw text.
        var timedLines = LyricsService.ParseLrc(syncedLyrics);
        if (timedLines.Count == 0)
            timedLines = LyricsService.ParseLrc(plainLyrics);
        if (timedLines.Count > 0)
        {
            foreach (var line in timedLines)
                _lyricLines.Add(new LyricLineViewModel(line.Text, line.Time, line.Words));
        }
        else if (!string.IsNullOrWhiteSpace(plainLyrics))
        {
            foreach (var line in plainLyrics.Replace("\r\n", "\n").Split('\n'))
                _lyricLines.Add(new LyricLineViewModel(line.Trim(), null));
        }

        var hasLyrics = _lyricLines.Count > 0;
        LyricsStatusTextBlock.IsVisible = !(hasLyrics);
        if (hasLyrics && _player is not null)
            UpdateActiveLyric(_player.Position);
        return hasLyrics;
    }

    private void UpdateActiveLyric(TimeSpan position)
    {
        if (_lyricLines.Count == 0)
            return;

        var nextIndex = LyricLineSelector.FindActiveIndex(
            _lyricLines,
            line => line.Time,
            position);
        if (nextIndex != _activeLyricIndex)
        {
            if (_activeLyricIndex >= 0 && _activeLyricIndex < _lyricLines.Count)
                _lyricLines[_activeLyricIndex].IsActive = false;
            _activeLyricIndex = nextIndex;
            if (_activeLyricIndex >= 0)
            {
                var activeLine = _lyricLines[_activeLyricIndex];
                activeLine.IsActive = true;
                LyricsListBox.SelectedItem = activeLine;
                CenterLyricLine(activeLine);
                // A seek can jump backwards, so recompute every line's word progress
                // instead of only advancing the newly active one.
                foreach (var line in _lyricLines)
                    line.UpdateWordProgress(position);
            }
            else
            {
                LyricsListBox.SelectedItem = null;
            }
        }
        else if (_activeLyricIndex >= 0 && _activeLyricIndex < _lyricLines.Count)
        {
            _lyricLines[_activeLyricIndex].UpdateWordProgress(position);
        }
    }

    /// <summary>Keeps the active lyric line vertically centred in the lyrics view.</summary>
    /// <param name="line">Line that became active.</param>
    private void CenterLyricLine(LyricLineViewModel line)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                var scrollViewer = FindLyricsScrollViewer();
                if (scrollViewer is null ||
                    LyricsListBox.ContainerFromItem(line) is not Control container)
                    return;
                var top = container.TranslatePoint(new Point(0, 0), LyricsListBox)?.Y;
                if (top is null)
                    return;
                var target = scrollViewer.Offset.Y + top.Value -
                             (scrollViewer.Viewport.Height - container.Bounds.Height) / 2;
                StartLyricsScrollAnimation(scrollViewer, Math.Max(0, target));
            },
            DispatcherPriority.Background);
    }

    /// <summary>Finds the scroll viewer inside the lyrics list.</summary>
    /// <returns>The lyrics scroll viewer, or <see langword="null"/> before the list is realised.</returns>
    private ScrollViewer? FindLyricsScrollViewer() =>
        LyricsListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>Scrolls the lyrics to a new offset, easing over a short interval.</summary>
    /// <param name="scrollViewer">Lyrics scroll viewer.</param>
    /// <param name="target">Target vertical offset.</param>
    private void StartLyricsScrollAnimation(ScrollViewer scrollViewer, double target)
    {
        _lyricsScrollTimer?.Stop();
        if (!MotionPreferences.ShouldAnimate(_settings.ReduceMotion) ||
            Math.Abs(scrollViewer.Offset.Y - target) < 0.5)
        {
            scrollViewer.Offset = new Vector(0, target);
            return;
        }

        _lyricsScrollViewer = scrollViewer;
        _lyricsScrollStartOffset = scrollViewer.Offset.Y;
        _lyricsScrollTargetOffset = target;
        _lyricsScrollWatch.Restart();
        _lyricsScrollTimer ??= CreateLyricsScrollTimer();
        _lyricsScrollTimer.Start();
    }

    /// <summary>Creates the timer that advances the eased lyrics scroll.</summary>
    /// <returns>A running-capable timer.</returns>
    private DispatcherTimer CreateLyricsScrollTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => AdvanceLyricsScrollAnimation();
        return timer;
    }

    /// <summary>Advances the eased lyrics scroll by one frame.</summary>
    private void AdvanceLyricsScrollAnimation()
    {
        if (_lyricsScrollViewer is null)
        {
            _lyricsScrollTimer?.Stop();
            return;
        }

        const double durationMilliseconds = 260;
        var progress = Math.Min(1, _lyricsScrollWatch.Elapsed.TotalMilliseconds / durationMilliseconds);
        var eased = 1 - Math.Pow(1 - progress, 3);
        _lyricsScrollViewer.Offset = new Vector(
            0,
            _lyricsScrollStartOffset + (_lyricsScrollTargetOffset - _lyricsScrollStartOffset) * eased);
        if (progress >= 1)
        {
            _lyricsScrollTimer?.Stop();
            _lyricsScrollViewer = null;
        }
    }

    /// <summary>Stops a pending lyrics scroll animation.</summary>
    private void StopLyricsScrollAnimation()
    {
        _lyricsScrollTimer?.Stop();
        _lyricsScrollViewer = null;
    }

    /// <summary>Opens or closes immersive fullscreen playback of the current lyrics page.</summary>
    /// <param name="sender">The immersive action.</param>
    /// <param name="e">Click details.</param>
    private void ImmersiveLyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!LyricsView.IsVisible)
            LyricsButton_OnClick(sender, e);
        if (!LyricsView.IsVisible)
            return;
        SetLyricsImmersive(!_lyricsImmersive);
    }

    /// <summary>Enters or leaves the fullscreen lyrics presentation without hiding the lyrics.</summary>
    /// <param name="immersive">Whether immersive playback should be active.</param>
    private void SetLyricsImmersive(bool immersive)
    {
        if (_lyricsImmersive == immersive)
            return;
        _lyricsImmersive = immersive;
        if (immersive)
        {
            _windowStateBeforeImmersive = WindowState;
            SidebarPanel.IsVisible = false;
            TransportBar.IsVisible = false;
            Grid.SetColumn(ContentPanel, 0);
            Grid.SetColumnSpan(ContentPanel, 2);
            WindowState = WindowState.FullScreen;
            LyricsImmersiveButton.Content = LocalizationManager.Current.ExitImmersivePlayback;
            ToolTip.SetTip(LyricsImmersiveButton, LocalizationManager.Current.ExitImmersivePlayback);
        }
        else
        {
            SidebarPanel.IsVisible = true;
            TransportBar.IsVisible = true;
            Grid.SetColumn(ContentPanel, 1);
            Grid.SetColumnSpan(ContentPanel, 1);
            WindowState = _windowStateBeforeImmersive;
            LyricsImmersiveButton.Content = LocalizationManager.Current.ImmersivePlayback;
            ToolTip.SetTip(LyricsImmersiveButton, LocalizationManager.Current.ImmersivePlayback);
        }
    }

    private void ShowLyricsStatus(string text)
    {
        LyricsStatusTextBlock.Text = text;
        // Never cover rendered lyrics with a status message; a failed refresh keeps
        // the lines the user can already read.
        LyricsStatusTextBlock.IsVisible = _lyricLines.Count == 0;
    }

    private void ClearLyrics()
    {
        StopLyricsScrollAnimation();
        LyricsListBox.SelectedItem = null;
        _lyricLines.Clear();
        _activeLyricIndex = -1;
    }

    private void CancelLyricsLoad()
    {
        _lyricsCts?.Cancel();
    }
}
