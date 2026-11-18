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
using Avalonia.Animation;
using Avalonia.Animation.Easings;
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
/// Embedded settings host, output-profile and equalizer pickers, output-device
/// lock, and PCM volume/ReplayGain handling for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private void VolumeSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (VolumeValueTextBlock is null) return;
        VolumeValueTextBlock.Text = $"{Math.Round(VolumeSlider.Value * 100):N0} %";

        // The transport slider is applied only as in-app software gain. HyperFidel
        // never writes the system sink or the ALSA hardware mixer for PCM output.
        if (_player is not null)
            _player.Volume = (float)VolumeSlider.Value;

        _settings.Volume = VolumeSlider.Value;
        if (!_updatingVolumeFromSystem)
        {
            _windowsMediaTransport?.SetVolume(VolumeSlider.Value);
            ScheduleVolumeSave();
        }
    }

    /// <summary>
    /// Persists the volume shortly after the last change. Volume used to be written
    /// only on a clean window close, so a crash, force-quit or logout lost the user's
    /// choice and the next launch fell back to the default full volume.
    /// </summary>
    private void ScheduleVolumeSave()
    {
        if (_volumeSaveTimer is null)
        {
            _volumeSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _volumeSaveTimer.Tick += (_, _) =>
            {
                _volumeSaveTimer!.Stop();
                var snapshot = _settings;
                _ = Task.Run(() => _settingsStore.Save(snapshot));
            };
        }

        _volumeSaveTimer.Stop();
        _volumeSaveTimer.Start();
    }

    /// <summary>
    /// Endpoint-volume synchronization is intentionally disabled. HyperFidel
    /// applies the transport slider only as in-app software gain and never
    /// writes the system sink or the ALSA hardware mixer for PCM output. The
    /// method is kept so callers in every build remain valid.
    /// </summary>
    private async Task ConfigureEndpointVolumeSynchronizationAsync()
    {
        DetachEndpointVolumeSynchronization();
        await Task.CompletedTask;
    }

    private static async Task UpdateSearchIndexAfterArtistRenameAsync(long artistId)
    {
        try
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                TrackSearchIndex.UpdateMany(db.GetTracksForArtistSearchIndex(artistId));
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Artist rename search-index update");
        }
    }

    private WindowsEndpointVolumeSynchronizer? DetachEndpointVolumeSynchronization()
    {
        var synchronizer = _endpointVolumeSynchronizer;
        _endpointVolumeSynchronizer = null;
        if (synchronizer is not null)
            synchronizer.VolumeChanged -= EndpointVolumeSynchronizer_OnVolumeChanged;
        return synchronizer;
    }

    private void DisposeEndpointVolumeSynchronizationInBackground()
    {
        Interlocked.Increment(ref _endpointVolumeSynchronizationVersion);
        var synchronizer = DetachEndpointVolumeSynchronization();
        if (synchronizer is not null)
            _ = Task.Run(() => DisposeEndpointSynchronizer(synchronizer));
    }

    private static void DisposeEndpointSynchronizer(
        WindowsEndpointVolumeSynchronizer synchronizer)
    {
        try
        {
            synchronizer.Dispose();
        }
        catch
        {
        }
    }

    private void EndpointVolumeSynchronizer_OnVolumeChanged(object? sender, float volume)
    {
        // Only mirror the endpoint when it actually carries the gain. For a bypassing
        // output path (direct ALSA) the application volume is authoritative, otherwise
        // any system volume change would silently overwrite the user's choice.
        if (_endpointVolumeSynchronizer is { AppliesToOutputPath: false })
            return;
        Dispatcher.UIThread.Post(() => ApplySystemVolume(volume, pushToEndpoint: false));
    }

    /// <summary>Applies a volume reported by MPRIS or the system media controls.</summary>
    /// <param name="volume">Linear volume from 0.0 to 1.0.</param>
    /// <param name="pushToEndpoint">
    /// Retained for source compatibility; the system endpoint is never written.
    /// </param>
    private void ApplySystemVolume(float volume, bool pushToEndpoint = true)
    {
        LogUiDiagnostics($"ApplySystemVolume {volume:0.###}");
        _updatingVolumeFromSystem = true;
        try
        {
            VolumeSlider.Value = Math.Clamp(volume, 0.0f, 1.0f);
            VolumeValueTextBlock.Text = $"{Math.Round(VolumeSlider.Value * 100):N0} %";
            _settings.Volume = VolumeSlider.Value;
        }
        finally
        {
            _updatingVolumeFromSystem = false;
        }

        // Publish the value back to MPRIS media controls only. This never writes
        // the system sink or the ALSA hardware mixer; the slider drives software
        // gain exclusively.
        _windowsMediaTransport?.SetVolume(VolumeSlider.Value);
        ScheduleVolumeSave();
    }

    private float GetReplayGainFactor(string filePath)
    {
        var pcmGain = GetPcmOutputGainFactor();
        if (_settings.ReplayGainMode == ReplayGainMode.Off)
            return pcmGain;

        if (TryGetCurrentReplayGainValues(filePath, out var trackGain, out var albumGain))
        {
            return pcmGain * ReplayGain.GetLinearFactor(
                _settings.ReplayGainMode,
                trackGain,
                albumGain);
        }

        return pcmGain;
    }

    private void UpdateReplayGainBadge(bool nativeDsdOutput)
    {
        ReplayGainBadgeBorder.IsVisible =
            !nativeDsdOutput &&
            _settings.ReplayGainMode != ReplayGainMode.Off &&
            TryGetCurrentReplayGainValues(_currentFilePath, out var trackGain, out var albumGain) &&
            HasPreferredReplayGainValue(trackGain, albumGain);
    }

    private bool HasPreferredReplayGainValue(string? trackGain, string? albumGain) =>
        _settings.ReplayGainMode switch
        {
            ReplayGainMode.Track => !string.IsNullOrWhiteSpace(trackGain) ||
                                    !string.IsNullOrWhiteSpace(albumGain),
            ReplayGainMode.Album => !string.IsNullOrWhiteSpace(albumGain) ||
                                    !string.IsNullOrWhiteSpace(trackGain),
            _ => false
        };

    private bool TryGetCurrentReplayGainValues(
        string? filePath,
        out string? trackGain,
        out string? albumGain)
    {
        trackGain = null;
        albumGain = null;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (_hyperfidelTracksByUrl.TryGetValue(filePath, out var hyperfidelRow))
        {
            trackGain = hyperfidelRow.ReplayGainTrack;
            albumGain = hyperfidelRow.ReplayGainAlbum;
            return !string.IsNullOrWhiteSpace(trackGain) ||
                   !string.IsNullOrWhiteSpace(albumGain);
        }

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(filePath);
            if (track is null)
                return false;
            trackGain = track.ReplayGainTrack;
            albumGain = track.ReplayGainAlbum;
            return !string.IsNullOrWhiteSpace(trackGain) ||
                   !string.IsNullOrWhiteSpace(albumGain);
        }
        catch
        {
            return false;
        }
    }

    private float GetPcmOutputGainFactor() =>
        _settings.PcmOutputBoostEnabled ? PcmOutputBoostFactor : 1.0f;

    // ------------------------------------------------------------------
    // Einstellungen
    // ------------------------------------------------------------------

    /// <summary>Removes indexed rows outside copied roots off the dispatcher and invalidates only after a successful mutation.</summary>
    /// <param name="paths">Current configured roots, copied before worker admission.</param>
    /// <returns>The number of removed rows, or zero when cleanup fails or changes nothing.</returns>
    private Task<int> CleanupLibraryRootsAsync(IReadOnlyCollection<string> paths)
    {
        var capturedPaths = paths.ToArray();
        return Task.Run(() =>
        {
            try
            {
                var removed = LibraryScanner.RemoveTracksOutsideRoots(capturedPaths);
                if (removed > 0)
                    OnWatchedLibraryChanged();
                return removed;
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Library root cleanup");
                return 0;
            }
        });
    }

    /// <summary>Copies configured roots and reveals the library when its first local source is added.</summary>
    /// <param name="paths">New local roots; later caller edits must not change configured state.</param>
    /// <returns>The copied, non-null configured roots.</returns>
    private List<string> SetConfiguredLibraryPaths(IReadOnlyCollection<string> paths)
    {
        if ((_settings.LibraryPaths?.Count ?? 0) == 0 && paths.Count > 0)
            _settings.IsLocalLibrarySectionExpanded = true;
        _settings.LibraryPaths = paths.ToList();
        return _settings.LibraryPaths;
    }

    /// <summary>Opens embedded Settings with shared artist write ownership after abandoning pending navigation work.</summary>
    /// <param name="sender">Settings action source.</param>
    /// <param name="e">Button activation event.</param>
    private void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SettingsViewHost.IsVisible)
            return;

        _navigationRestores.Invalidate();
        CancelProviderAlbumLoad();
        CancelTopLevelViewLoad();
        CancelRemoteViewLoad();
        CancelLibrarySearch();
        var view = new SettingsView(_settings, paths =>
        {
            var configuredPaths = SetConfiguredLibraryPaths(paths);
            _settingsStore.Save(_settings);
            _libraryWatcher?.UpdatePaths(configuredPaths);
            _ = CleanupLibraryRootsAsync(configuredPaths);
            // Show/hide the Local section and the empty-library hint immediately
            // when directories are added or removed while Settings is open.
            ApplySidebarNavigationSettings();
        }, (enabled, profile) =>
        {
            if (_player is IEqualizerAudioPlayer equalizerPlayer)
                equalizerPlayer.UpdateEqualizer(enabled, profile);
        });
        view.ArtistImageWriter = new MissingArtistImageWriter(_artistImageWriteQueue, AdmitArtistImageWrite);
        view.ArtistImagesChanged += InvalidateUnifiedLibraryViewCache;
        var completionHandled = false;
        view.RunScheduledBackup = async force =>
        {
            var completed = await RunScheduledBackupIfDueAsync(force);
            if (completed)
                view.SetScheduledBackupLastRun(_settings.ScheduledBackup.LastRunAtUnix);
            return completed;
        };
        view.LocalLibraryChanged += OnWatchedLibraryChanged;
        view.ProfileChanged += profileId => _ = OnUserProfileChangedAsync(profileId);
        view.DuplicateResolutionRequested += () => _ = OpenDuplicateResolutionAsync();
        view.CompletionRequested += async (_, accepted) =>
        {
            if (completionHandled)
                return;
            completionHandled = true;
            SettingsViewHost.IsEnabled = false;
            try
            {
                if (accepted)
                    await ApplySettingsAsync(view);
            }
            finally
            {
                CloseEmbeddedSettings();
                SettingsViewHost.IsEnabled = true;
            }
        };
        SettingsViewHost.Content = view;
        SettingsViewHost.IsVisible = true;
        AnimateSettingsHostIn();
    }

    /// <summary>Lifts the embedded settings host into place.</summary>
    private void AnimateSettingsHostIn()
    {
        if (!MotionPreferences.ShouldAnimate(_settings.ReduceMotion))
        {
            SettingsViewHost.Transitions = null;
            SettingsViewHost.Opacity = 1;
            SettingsViewHost.RenderTransform = null;
            return;
        }
        // Start the host slightly dimmed rather than fully transparent so it
        // never leaves a blank frame while it lifts into place.
        SettingsViewHost.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(140),
                Easing = new CubicEaseOut()
            }
        };
        SettingsViewHost.Opacity = 0.9;
        var transform = new TranslateTransform(0, 10);
        transform.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new CubicEaseOut()
            }
        };
        SettingsViewHost.RenderTransform = transform;
        Dispatcher.UIThread.Post(() =>
        {
            SettingsViewHost.Opacity = 1;
            transform.Y = 0;
        }, DispatcherPriority.Render);
    }

    /// <summary>Applies the persisted headphone-crossfeed settings to a PCM player.</summary>
    /// <param name="player">The active player, or <see langword="null"/> when none is open.</param>
    private void ApplyCrossfeedSettings(IAudioPlayer? player)
    {
        if (player is ICrossfeedAudioPlayer crossfeedPlayer)
            crossfeedPlayer.UpdateCrossfeed(_settings.CrossfeedEnabled, _settings.CrossfeedStrength);
    }

    /// <summary>
    /// Applies the persisted streaming-loudness settings. Normalization runs only
    /// for library tracks; native DSD remains unaffected.
    /// </summary>
    /// <param name="player">The active player, or <see langword="null"/> when none is open.</param>
    private void ApplyLoudnessNormalizationSettings(IAudioPlayer? player)
    {
        if (player is not ILoudnessNormalizerAudioPlayer normalizerPlayer)
            return;

        normalizerPlayer.UpdateLoudnessNormalization(
            enabled: false,
            StreamingLoudnessNormalizer.DefaultTargetDbfs,
            StreamingLoudnessNormalizer.DefaultMaximumGainDb);
    }

    /// <summary>Applies a newly selected profile and refreshes profile-sensitive views.</summary>
    /// <param name="profileId">Stable identifier of the selected profile.</param>
    private async Task OnUserProfileChangedAsync(string profileId)
    {
        AudioDatabase.SetActiveProfile(profileId);
        ApplyServerProfileContext();
        _settingsStore.Save(_settings);
        StopInfiniteMix();
        InvalidateCatalogConfiguration();
        if (!string.IsNullOrWhiteSpace(_currentTopLevelTag))
            await ShowTopLevelViewAsync(_currentTopLevelTag);
    }

    /// <summary>Applies the active local profile's server-profile mappings to remote clients.</summary>
    private void ApplyServerProfileContext()
    {
        if (_profileManager is null)
            return;
        var profile = _profileManager.ActiveProfile;
        foreach (var server in _settings.HyperFidelServers)
        {
            server.ProfileId = profile.ServerProfileIds.TryGetValue(server.Id, out var mapped)
                && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : "standard";
        }
    }

    private async Task RefreshServerProfileMappingsAsync()
    {
        if (_profileManager is null || _settings.HyperFidelServers.Count == 0)
            return;
        using var client = new HyperFidelServerClient();
        var profile = _profileManager.ActiveProfile;
        foreach (var server in _settings.HyperFidelServers)
        {
            var profiles = await client.GetProfilesAsync(server);
            if (profiles.Count == 0)
                continue;
            if (!profile.ServerProfileIds.TryGetValue(server.Id, out var mapped) ||
                profiles.All(p => !string.Equals(p.Id, mapped, StringComparison.OrdinalIgnoreCase)))
            {
                var sameName = profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                profile.ServerProfileIds[server.Id] = sameName?.Id
                    ?? profiles.FirstOrDefault(p => string.Equals(p.Id, "standard", StringComparison.OrdinalIgnoreCase))?.Id
                    ?? profiles[0].Id;
            }
        }
        ApplyServerProfileContext();
        _settingsStore.Save(_settings);
        _ = SyncProfileHistoryInBackgroundAsync();
    }

    /// <summary>Replicates the active profile's recent history without blocking the UI.</summary>
    private async Task SyncProfileHistoryInBackgroundAsync()
    {
        try
        {
            List<SyncedPlaybackHistoryEntry> local;
            using (var db = AudioDatabase.OpenDefault())
                local = db.GetHistoryForSync(1000);
            var payload = local.Select(entry => new HyperFidelHistorySyncEntry(
                entry.SyncId, entry.Path, entry.StartedAtUnix, entry.PositionSeconds,
                entry.DurationSeconds, entry.MediaType, entry.Title, entry.Subtitle,
                entry.Album, entry.ExternalId, entry.Genre)).ToList();
            foreach (var server in _settings.HyperFidelServers ?? [])
            {
                var remote = await _hyperfidelClient.GetHistorySyncAsync(server, 1000).ConfigureAwait(false);
                using (var db = AudioDatabase.OpenDefault())
                {
                    foreach (var entry in remote)
                        db.ImportSyncedHistory(new SyncedPlaybackHistoryEntry(
                            entry.SyncId, entry.Path, entry.StartedAtUnix, entry.PositionSeconds,
                            entry.DurationSeconds, entry.MediaType, entry.Title, entry.Subtitle,
                            entry.Album, entry.ExternalId, entry.Genre));
                }
                await _hyperfidelClient.PushHistorySyncAsync(server, payload).ConfigureAwait(false);
            }
            InvalidateDashboardCatalogCache();
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Background profile history synchronization");
        }
    }

    private void CloseEmbeddedSettings()
    {
        if (SettingsViewHost.Content is SettingsView settingsView)
            settingsView.Deactivate();
        SettingsViewHost.Content = null;
        SettingsViewHost.IsVisible = false;
    }

    /// <summary>Opens library setup through the shared embedded settings path.</summary>
    /// <param name="sender">The empty-library setup button.</param>
    /// <param name="e">The button activation event.</param>
    private void LibrarySetupButton_OnClick(object? sender, RoutedEventArgs e) =>
        OpenSettingsAt("Library");

    /// <summary>Opens server setup through the shared embedded settings path.</summary>
    /// <param name="sender">The empty-library server setup button.</param>
    /// <param name="e">The button activation event.</param>
    private void LibraryServerSetupButton_OnClick(object? sender, RoutedEventArgs e) =>
        OpenSettingsAt("HyperFidelServers");

    private void OpenSettingsAt(string sectionTag, bool scrollToEqualizer = false)
    {
        if (!SettingsViewHost.IsVisible)
            SettingsButton_OnClick(null!, null!);
        if (SettingsViewHost.Content is SettingsView sv)
        {
            sv.NavigateToSection(sectionTag);
            if (scrollToEqualizer)
                sv.ScrollToEqualizerSection();
        }
    }

    // ------------------------------------------------------------------
    // EQ + Output quick-pick popups
    // ------------------------------------------------------------------

    private void EqPickerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OutputPickerPopup.IsOpen = false;
        _eqPickerUpdating = true;
        try
        {
            EqPickerComboBox.ItemsSource = _settings.EqualizerProfiles;
            EqPickerComboBox.SelectedItem = _settings.EqualizerProfiles
                .FirstOrDefault(p => string.Equals(
                    p.Name, _settings.SelectedEqualizerProfileName,
                    StringComparison.OrdinalIgnoreCase));
            EqPickerEnabledCheckBox.IsChecked = _settings.EqualizerEnabled;
        }
        finally
        {
            _eqPickerUpdating = false;
        }
        EqPickerPopup.IsOpen = !EqPickerPopup.IsOpen;
    }

    private void EqPickerComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_eqPickerUpdating) return;
        if (EqPickerComboBox.SelectedItem is not EqualizerProfile profile) return;
        _settings.SelectedEqualizerProfileName = profile.Name;
        _settings.EqualizerProfile = profile.Clone();
        if (_player is IEqualizerAudioPlayer eqPlayer)
            eqPlayer.UpdateEqualizer(_settings.EqualizerEnabled, _settings.EqualizerProfile);
        _ = Task.Run(() => _settingsStore.Save(_settings));
    }

    private void EqPickerEnabledCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_eqPickerUpdating) return;
        _settings.EqualizerEnabled = EqPickerEnabledCheckBox.IsChecked == true;
        if (_player is IEqualizerAudioPlayer eqPlayer)
            eqPlayer.UpdateEqualizer(_settings.EqualizerEnabled, _settings.EqualizerProfile);
        _ = Task.Run(() => _settingsStore.Save(_settings));
    }

    private void EqPickerSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EqPickerPopup.IsOpen = false;
        OpenSettingsAt("AudioDevice", scrollToEqualizer: true);
    }

    /// <summary>Populates the transport output picker from the saved output profiles.</summary>
    private void RefreshOutputPickerSelection()
    {
        _outputPickerUpdating = true;
        try
        {
            var profiles = (_settings.OutputProfiles ?? []).ToList();
            OutputPickerComboBox.ItemsSource = profiles;
            OutputPickerComboBox.SelectedItem = profiles.FirstOrDefault(p =>
                string.Equals(p.Name, _settings.SelectedOutputProfileName, StringComparison.OrdinalIgnoreCase));
        }
        finally { _outputPickerUpdating = false; }
    }

    private void OutputPickerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EqPickerPopup.IsOpen = false;
        RefreshOutputPickerSelection();
        OutputPickerPopup.IsOpen = !OutputPickerPopup.IsOpen;
    }

    private async void OutputPickerComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_outputPickerUpdating) return;
        if (OutputPickerComboBox.SelectedItem is not OutputProfile profile) return;
        await SelectOutputProfileAsync(profile);
    }

    /// <summary>Selects a configured output profile by display name.</summary>
    /// <param name="profileName">Exact profile name.</param>
    /// <returns><see langword="true"/> when the profile exists and was selected.</returns>
    private async Task<bool> SelectOutputProfileByNameAsync(string profileName)
    {
        var requestedName = profileName?.Trim() ?? string.Empty;
        const string selectedSuffix = " (selected)";
        if (requestedName.EndsWith(selectedSuffix, StringComparison.OrdinalIgnoreCase))
            requestedName = requestedName[..^selectedSuffix.Length].TrimEnd();

        var profile = (_settings.OutputProfiles ?? []).FirstOrDefault(candidate =>
            string.Equals(candidate.Name, requestedName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
            return false;
        await SelectOutputProfileAsync(profile);
        return true;
    }

    /// <summary>Selects an output profile and preserves active playback across device changes.</summary>
    /// <param name="profile">Profile to select.</param>
    /// <returns>A task representing the asynchronous device switch.</returns>
    private async Task SelectOutputProfileAsync(OutputProfile profile)
    {
        if (_playbackShuttingDown)
            return;
        var selectionVersion = ++_outputProfileSelectionVersion;
        if (string.Equals(profile.Name, _settings.SelectedOutputProfileName, StringComparison.Ordinal)) return;

        var outputChanged =
            _settings.OutputBackend != profile.Backend ||
            !string.Equals(_settings.SelectedDriverName, profile.SelectedDriverName, StringComparison.Ordinal) ||
            !string.Equals(_settings.SelectedWasapiDeviceId, profile.SelectedWasapiDeviceId, StringComparison.Ordinal) ||
            !string.Equals(_settings.SelectedAirPlayDeviceId, profile.SelectedAirPlayDeviceId, StringComparison.Ordinal);

        // Snapshot playback state before StopPlaybackCore clears current state.
        var resumePath     = _currentFilePath;
        var resumePosition = _player?.Position ?? TimeSpan.Zero;
        var wasPaused      = _player?.IsPaused == true;
        var shouldResume   = outputChanged && _player is not null && !string.IsNullOrEmpty(resumePath);

        if (outputChanged)
        {
            var stopped = StopPlaybackAsync(waitForCompleteDisposal: false);
            var stoppedIntent = _playbackStarts.Current;
            await stopped;
            if (_playbackShuttingDown || selectionVersion != _outputProfileSelectionVersion ||
                !_playbackStarts.IsCurrent(stoppedIntent))
                return;
        }

        _settings.SelectedOutputProfileName  = profile.Name;
        _settings.OutputBackend              = profile.Backend;
        _settings.SelectedDriverName         = profile.SelectedDriverName;
        _settings.SelectedWasapiDeviceId     = profile.SelectedWasapiDeviceId;
        _settings.SelectedWasapiDeviceName   = profile.SelectedWasapiDeviceName;
        _settings.SelectedAirPlayDeviceId    = profile.SelectedAirPlayDeviceId;
        _settings.SelectedAirPlayDeviceName  = profile.SelectedAirPlayDeviceName;
        _settings.SelectedAirPlayHost        = profile.SelectedAirPlayHost;
        _settings.SelectedAirPlayPort        = profile.SelectedAirPlayPort;
        _ = Task.Run(() => _settingsStore.Save(_settings));

        RefreshOutputPickerSelection();

        if (!shouldResume)
            return;
        var ready = StartReadyPlaybackAsync(resumePath, resumePosition, wasPaused);
        var resumeIntent = _playbackStarts.Current;
        try { await ready; }
        catch (Exception) when (_playbackShuttingDown || !_playbackStarts.IsCurrent(resumeIntent)) { }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped;
        }
        catch (Exception ex)
        {
            StopPlayback();
            StatusTextBlock.Text = ex.Message;
        }
    }

    /// <summary>Opens existing output Settings from the transport or empty-library setup.</summary>
    /// <param name="sender">The output Settings action.</param>
    /// <param name="e">The button activation event.</param>
    private void OutputPickerSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OutputPickerPopup.IsOpen = false;
        OpenSettingsAt("AudioDevice");
    }

    /// <summary>Releases or reacquires the exclusive output device while preserving playback context.</summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="e">Click event arguments.</param>
    private async void OutputDeviceLockButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EqPickerPopup.IsOpen = false;
        OutputPickerPopup.IsOpen = false;

        if (_player is { } player)
        {
            _releasedOutputPath = _currentFilePath;
            _releasedOutputPosition = player.Position;
            _releasedOutputWasPaused = player.IsPaused;
            var release = StopPlaybackAsync(waitForCompleteDisposal: true);
            var releaseIntent = _playbackStarts.Current;
            await release;
            if (_playbackShuttingDown || !_playbackStarts.IsCurrent(releaseIntent))
                return;
            _audioDeviceExplicitlyReleased = true;
            UpdateOutputDeviceLockButton();
            StatusTextBlock.Text = LocalizationManager.Current.OutputDeviceReleased;
            return;
        }

        if (!_audioDeviceExplicitlyReleased || string.IsNullOrWhiteSpace(_releasedOutputPath))
            return;

        var path = _releasedOutputPath;
        var position = _releasedOutputPosition;
        var wasPaused = _releasedOutputWasPaused;
        var ready = StartReadyPlaybackAsync(path, position, wasPaused);
        var resumeIntent = _playbackStarts.Current;
        try { await ready; }
        catch (Exception) when (_playbackShuttingDown || !_playbackStarts.IsCurrent(resumeIntent)) { }
        catch (Exception ex)
        {
            var stopped = StopPlaybackAsync(waitForCompleteDisposal: true);
            var stoppedIntent = _playbackStarts.Current;
            await stopped;
            if (_playbackShuttingDown || !_playbackStarts.IsCurrent(stoppedIntent))
                return;
            _releasedOutputPath = path;
            _releasedOutputPosition = position;
            _releasedOutputWasPaused = wasPaused;
            _audioDeviceExplicitlyReleased = true;
            UpdateOutputDeviceLockButton();
            StatusTextBlock.Text = ex is OperationCanceledException
                ? LocalizationManager.Current.PlaybackStopped : ex.Message;
        }
    }

    /// <summary>Selects an equalizer profile and applies the requested enabled state.</summary>
    /// <param name="profileName">Profile name, or <see langword="null"/> to retain the selection.</param>
    /// <param name="enabled">Requested enabled state.</param>
    /// <returns><see langword="true"/> when the requested profile exists.</returns>
    private Task<bool> ConfigureEqualizerAsync(string? profileName, bool enabled)
    {
        EqualizerProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            profile = (_settings.EqualizerProfiles ?? []).FirstOrDefault(candidate =>
                string.Equals(candidate.Name, profileName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (profile is null)
                return Task.FromResult(false);
            _settings.SelectedEqualizerProfileName = profile.Name;
            _settings.EqualizerProfile = profile.Clone();
        }

        _settings.EqualizerEnabled = enabled;
        if (_player is IEqualizerAudioPlayer eqPlayer)
            eqPlayer.UpdateEqualizer(enabled, _settings.EqualizerProfile);
        _ = Task.Run(() => _settingsStore.Save(_settings));
        return Task.FromResult(true);
    }

    /// <summary>Refreshes the lock icon, tooltip, and availability from the actual player state.</summary>
    private void UpdateOutputDeviceLockButton()
    {
        if (OutputDeviceLockButton is null || OutputDeviceLockIconPath is null)
            return;
        var isHeld = _player is not null;
        OutputDeviceLockIconPath.Data = FindResource<StreamGeometry>(
            isHeld ? "IconLockClosed" : "IconLockOpen");
        OutputDeviceLockButton.IsEnabled = isHeld ||
            (_audioDeviceExplicitlyReleased && !string.IsNullOrWhiteSpace(_releasedOutputPath));
        ToolTip.SetTip(
            OutputDeviceLockButton,
            isHeld
                ? LocalizationManager.Current.ReleaseOutputDevice
                : _audioDeviceExplicitlyReleased
                    ? LocalizationManager.Current.ReacquireOutputDevice
                    : LocalizationManager.Current.OutputDeviceReleased);
    }

    /// <summary>Clears the one-shot playback snapshot after the output device was reacquired normally.</summary>
    private void ClearReleasedOutputResumeState()
    {
        _releasedOutputPath = null;
        _releasedOutputPosition = TimeSpan.Zero;
        _releasedOutputWasPaused = false;
    }

    private async Task ApplySettingsAsync(SettingsView window)
    {
            var themeChanged = _settings.Theme != window.SelectedTheme;
            var languageChanged = _settings.Language != window.SelectedLanguage;
            var genreCloudBackgroundChanged =
                _settings.GenreCloudBackground != window.SelectedGenreCloudBackground ||
                Math.Abs(
                    _settings.GenreCloudBackgroundOpacity -
                    window.SelectedGenreCloudBackgroundOpacity) > 0.0001;
            var replayGainChanged =
                _settings.ReplayGainMode != window.SelectedReplayGainMode ||
                _settings.PcmOutputBoostEnabled != window.PcmOutputBoostEnabled;
            var artistInfoChanged =
                _settings.ArtistInfoSource != window.SelectedArtistInfoSource ||
                !string.Equals(
                    _settings.LastFmApiKey,
                    window.SelectedLastFmApiKey,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _settings.FanartTvApiKey,
                    window.SelectedFanartTvApiKey,
                    StringComparison.Ordinal);
            var sidebarChanged =
                _settings.ShowQueueItem != window.ShowQueueItem ||
                _settings.ShowLocalLibrarySection != window.ShowLocalLibrarySection ||
                _settings.ShowPlexSection != window.ShowPlexSection;
            var motionChanged = _settings.ReduceMotion != window.ReduceMotionValue;
            var plexServersChanged = !PlexServerSettingsEqual(
                _settings.PlexServers,
                window.SelectedPlexServers);
            var hyperfidelServersChanged = !LibraryCatalogConfiguration.MatchesServers(
                _settings.HyperFidelServers,
                window.SelectedHyperFidelServers);
            var libraryPathsChanged = !(_settings.LibraryPaths ?? []).SequenceEqual(window.SelectedLibraryPaths);
            var hadHyperFidelServers = _settings.HyperFidelServers.Count > 0;
            var outputChanged =
                _settings.OutputBackend != window.SelectedOutputBackend ||
                _settings.DsdOverPcmEnabled != window.DsdOverPcmEnabled ||
                !string.Equals(
                    _settings.SelectedDriverName,
                    window.SelectedDriverName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _settings.SelectedWasapiDeviceId,
                    window.SelectedWasapiDeviceId,
                    StringComparison.Ordinal);
            outputChanged = outputChanged || !string.Equals(
                _settings.SelectedAirPlayDeviceId,
                window.SelectedAirPlayDeviceId,
                StringComparison.Ordinal);
            if (outputChanged && _player is not null)
                await StopPlaybackAsync();

            _settings.OutputProfiles             = window.SelectedOutputProfiles.ToList();
            _settings.SelectedOutputProfileName  = window.SelectedOutputProfileName;
            _settings.OutputBackend              = window.SelectedOutputBackend;
            _settings.SelectedDriverName         = window.SelectedDriverName;
            _settings.SelectedWasapiDeviceId     = window.SelectedWasapiDeviceId;
            _settings.SelectedWasapiDeviceName   = window.SelectedWasapiDeviceName;
            _settings.SelectedAirPlayDeviceId    = window.SelectedAirPlayDeviceId;
            _settings.SelectedAirPlayDeviceName  = window.SelectedAirPlayDeviceName;
            _settings.SelectedAirPlayHost        = window.SelectedAirPlayHost;
            _settings.SelectedAirPlayPort        = window.SelectedAirPlayPort;
            _settings.ReplayGainMode        = window.SelectedReplayGainMode;
            _settings.CalculateMissingReplayGainDuringScan = window.CalculateMissingReplayGainDuringScan;
            _settings.AlwaysConvertDsdToPcm = window.AlwaysConvertDsdToPcm;
            _settings.DsdOverPcmEnabled = window.DsdOverPcmEnabled;
            _settings.PcmOutputBoostEnabled = window.PcmOutputBoostEnabled;
            _settings.MaxOutputSampleRateHz  = window.MaxOutputSampleRateHz;
            _settings.DsdPcmSampleRateHz     = window.DsdPcmSampleRateHz;
            DsdPlaybackOptions.PcmSampleRateHz = _settings.DsdPcmSampleRateHz;
            _settings.NonGaplessCrossfadeSeconds = window.NonGaplessCrossfadeSeconds;
            _settings.EqualizerEnabled      = window.EqualizerEnabled;
            _settings.EqualizerProfile      = window.SelectedEqualizerProfile;
            _settings.EqualizerProfiles     = window.SelectedEqualizerProfiles.ToList();
            _settings.SelectedEqualizerProfileName = window.SelectedEqualizerProfileName;
            var configuredLibraryPaths = SetConfiguredLibraryPaths(window.SelectedLibraryPaths);
            _libraryWatcher?.UpdateReplayGainAnalysis(_settings.CalculateMissingReplayGainDuringScan);
            _libraryWatcher?.UpdatePaths(configuredLibraryPaths);
            if (libraryPathsChanged)
                _ = CleanupLibraryRootsAsync(configuredLibraryPaths);
            _settings.Theme                  = window.SelectedTheme;
            _settings.Language               = window.SelectedLanguage;
            _settings.GenreCloudBackground   = window.SelectedGenreCloudBackground;
            _settings.GenreCloudBackgroundOpacity = window.SelectedGenreCloudBackgroundOpacity;
            _settings.ArtistInfoSource       = window.SelectedArtistInfoSource;
            _settings.LastFmApiKey           = window.SelectedLastFmApiKey;
            _settings.LastFmApiSecret        = window.SelectedLastFmApiSecret;
            _settings.LastFmScrobblingEnabled = window.SelectedLastFmScrobblingEnabled;
            _settings.CrossfeedEnabled       = window.SelectedCrossfeedEnabled;
            _settings.CrossfeedStrength      = window.SelectedCrossfeedStrength;
            _settings.FanartTvApiKey         = window.SelectedFanartTvApiKey;
            _settings.QobuzApplicationId      = window.SelectedQobuzApplicationId;
            _settings.PlexServers             = window.SelectedPlexServers.ToList();
            _settings.HyperFidelServers          = window.SelectedHyperFidelServers.ToList();
            if (hyperfidelServersChanged)
                InvalidateCatalogConfiguration();
            if (!hadHyperFidelServers && _settings.HyperFidelServers.Count > 0)
                _settings.IsLocalLibrarySectionExpanded = true;
            _settings.ScheduledBackup.Enabled        = window.ScheduledBackupEnabledValue;
            _settings.ScheduledBackup.IntervalDays   = window.ScheduledBackupIntervalValue;
            _settings.ScheduledBackup.RetentionCount = window.ScheduledBackupRetentionValue;
            _settings.ScheduledBackup.Directory      = window.ScheduledBackupDirectoryValue;
            _settings.ShowQueueItem           = window.ShowQueueItem;
            _settings.ReduceMotion             = window.ReduceMotionValue;
            StartAppLogoGlow();
            _settings.StartMaximized           = window.StartMaximized;
            _settings.ShowLocalLibrarySection = window.ShowLocalLibrarySection;
            _settings.ShowPlexSection              = window.ShowPlexSection;
            if (window.PlexCredentialsChanged)
            {
                try
                {
                    var plexTokens = window.SelectedPlexTokens.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value);
                    await Task.Run(() => new WindowsPlexCredentialStore().SaveAll(plexTokens));
                }
                catch
                {
                }
            }
            await Task.Run(() => _settingsStore.Save(_settings));
            ApplyCrossfeedSettings(_player);
            ApplyLoudnessNormalizationSettings(_player);
            if (themeChanged)
            {
                ThemeManager.Apply(_settings.Theme);
                UpdateNowPlayingRowHighlights();
            }
            if (languageChanged)
            {
                LocalizationManager.Apply(_settings.Language);
                UpdateUnifiedLibraryLoadNotice();
                RefreshLibrarySearchPresentation();
                RefreshPlaylistPlaceholderText();
                // Recreate dynamic navigation entries so headers such as the
                // local Playlists group use the newly selected language.
                LoadNavPlaylists();
                UpdateOutputDeviceLockButton();
                if (string.Equals(_currentTopLevelTag, "Dashboard", StringComparison.Ordinal))
                    await BuildDashboardAsync();
            }
            if (genreCloudBackgroundChanged)
            {
                GenreCloudBackgroundImage.Source = null;
                GenreCloudBackgroundImage.Opacity = 0;
                GenreCloudBackgroundShade.Opacity = 0;
            }
            if (motionChanged &&
                string.Equals(_currentTopLevelTag, "Dashboard", StringComparison.Ordinal))
            {
                await BuildDashboardAsync();
            }
            if (artistInfoChanged)
                ApplyArtistInfoSettings();
            if (outputChanged)
                RefreshSelectedDriverText();
            if (plexServersChanged || window.PlexCredentialsChanged)
                LoadNavPlaylists();
            else if (sidebarChanged || libraryPathsChanged)
                ApplySidebarNavigationSettings();
            if (hyperfidelServersChanged)
                LoadHyperFidelServerNavigation();
            if (replayGainChanged && _player is not null)
            {
                var currentFilePath = _currentFilePath;
                var replayGainFactor = await Task.Run(() =>
                    GetReplayGainFactorFromDatabase(currentFilePath));
                if (_player is not null &&
                    string.Equals(
                        _currentFilePath,
                        currentFilePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _player.ReplayGainFactor = replayGainFactor;
                    UpdateReplayGainBadge(
                        _player is DsfAudioPlayer or DffAudioPlayer or RemoteDsfAudioPlayer or RemoteDffAudioPlayer
#if !WINDOWS
                        or DsfDopAudioPlayer or RemoteDsfDopAudioPlayer or DffDopAudioPlayer
#endif
                    );
                }
            }
            if (_player is IEqualizerAudioPlayer equalizerPlayer)
                equalizerPlayer.UpdateEqualizer(
                    _settings.EqualizerEnabled,
                    _settings.EqualizerProfile);
            if (outputChanged)
                _ = ConfigureEndpointVolumeSynchronizationAsync();

            StatusTextBlock.Text = _settings.OutputBackend switch
            {
                OutputBackend.Asio or OutputBackend.CwAsio when string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                    LocalizationManager.Current.SelectAsioDevice,
                OutputBackend.Wasapi when string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId) =>
                    LocalizationManager.Current.SelectWasapiDevice,
                OutputBackend.AirPlay when string.IsNullOrWhiteSpace(_settings.SelectedAirPlayDeviceId) =>
                    LocalizationManager.Current.SelectAirPlayDevice,
                OutputBackend.KernelStreaming =>
                    string.Format(LocalizationManager.Current.NotImplemented, "KernelStreaming"),
                _ => LocalizationManager.Current.SettingsSaved
            };
    }

    private float GetReplayGainFactorFromDatabase(string filePath)
        => GetReplayGainFactor(filePath);
}
