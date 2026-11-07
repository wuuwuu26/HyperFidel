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
/// Top-level view switching and the shared content loading skeleton for
/// <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly NavigationLoadOwnership _topLevelViewLoads = new();
    private CancellationTokenSource? _topLevelViewCts;

    /// <summary>Detaches outer, cloud, Plex and artist-profile ownership before cancellation callbacks.</summary>
    private void CancelTopLevelViewLoad()
    {
        _topLevelViewLoads.Invalidate();
        var previousCancellation = _topLevelViewCts;
        _topLevelViewCts = null;
        var previousCloudCancellation = _genreCloudCts;
        _genreCloudCts = null;
        HideGenreCloudLoading();
        _genreCloudLoads.Invalidate();
        var previousPlexCancellation = _plexViewCts;
        _plexViewCts = null;
        _plexLoads.Invalidate();
        var previousProfileCancellation = _artistProfileCts;
        _artistProfileCts = null;
        _artistProfileLoads.Invalidate();
        var previousBackgroundCancellation = _backgroundArtistLoadCts;
        _backgroundArtistLoadCts = new CancellationTokenSource();
        previousProfileCancellation?.Cancel();
        previousBackgroundCancellation.Cancel();
        CancelAndDispose(ref previousCloudCancellation);
        CancelAndDispose(ref previousPlexCancellation);
        CancelAndDispose(ref previousCancellation);
    }

    /// <summary>Loads a top-level view while retaining ownership of its loading and fade publication.</summary>
    /// <param name="tag">Existing sidebar view identity.</param>
    /// <returns>A task completing after loading or abandonment by newer navigation.</returns>
    private Task ShowTopLevelViewAsync(string tag) => LoadTopLevelViewAsync(tag);

    /// <summary>Admits a top-level request and exposes its token before cancellation callbacks run.</summary>
    /// <param name="tag">Existing sidebar view identity.</param>
    /// <param name="onAdmitted">Optional adapter capturing this request's exact cancellation token.</param>
    /// <returns>A task completing after loading or abandonment.</returns>
    private async Task LoadTopLevelViewAsync(string tag, Action<CancellationToken>? onAdmitted = null)
    {
        if (!_navigationRestores.TryAdmitNavigation()) return;
        var restoreRequest = _navigationRestores.Context;
        var previousCancellation = _topLevelViewCts;
        _topLevelViewCts = new CancellationTokenSource();
        var token = _topLevelViewCts.Token;
        var request = _topLevelViewLoads.Begin(token);
        onAdmitted?.Invoke(token);
        CancelProviderAlbumLoad();
        CancelAndDispose(ref previousCancellation);
        CancelRemoteViewLoad();
        bool CanPublish() => _topLevelViewLoads.IsCurrent(request) &&
            (restoreRequest is null || _navigationRestores.IsCurrent(restoreRequest));
        if (!CanPublish()) return;
        var diagnosticStopwatch = Stopwatch.StartNew();
        LogUiDiagnostics($"ShowTopLevelViewAsync start tag={tag}");
        var showLoadingSkeleton = tag is not ("Dashboard" or "GenreCloud") &&
            !tag.StartsWith("Radio:", StringComparison.Ordinal);
        if (showLoadingSkeleton)
            ShowContentLoadingSkeleton();
        using var loading = showLoadingSkeleton ? new ContentLoadingLease(HideContentLoadingSkeleton) : null;
        using var loadingCancellation = token.Register(() =>
        {
            if (Dispatcher.UIThread.CheckAccess()) loading?.Dispose();
            else Dispatcher.UIThread.Post(() => loading?.Dispose());
        });
        CancelLibrarySearch();
        if (!CanPublish()) return;
        _currentTopLevelTag = tag;
        CancelAndDispose(ref _unifiedLibraryAppendCts);
        if (!CanPublish()) return;
        ClearUnifiedLibraryLoadNotice();
        _hyperfidelTrackFacets = null;
        // A fresh load reflects current library data, so any pending refresh prompt is stale.
        SetLibraryRefreshAvailable(false);
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        _activePlaylistId = tag.StartsWith("Playlist:") &&
                            long.TryParse(tag.AsSpan("Playlist:".Length), out long parsedPid)
            ? parsedPid : null;
        _activeHyperFidelPlaylistServer = null;
        _activeHyperFidelPlaylistId = null;
        if (TryParseHyperFidelPlaylistTag(tag, out var playlistServer, out var playlistId))
        {
            _activeHyperFidelPlaylistServer = playlistServer;
            _activeHyperFidelPlaylistId = playlistId;
        }

        ContentTitleTextBlock.Text = tag switch
        {
            "Artists" => LocalizationManager.Current.Artists,
            "Albums"  => LocalizationManager.Current.Albums,
            "Tracks"  => LocalizationManager.Current.Tracks,
            "GenreCloud" => LocalizationManager.Current.GenreExplorer,
            "Folders" => LocalizationManager.Current.FolderStructure,
            "Queue" => LocalizationManager.Current.UpNext,
            "AiChat" => LocalizationManager.Current.AiChat,
            "InternetRadio" => LocalizationManager.Current.InternetRadio,
            "Podcasts" => LocalizationManager.Current.Podcasts,
            "RecentAlbumsAll" => LocalizationManager.Current.RecentAlbums,
            "RecentlyPlayedAll" => LocalizationManager.Current.RecentlyPlayed,
            _ when tag.StartsWith("PlexLibrary:", StringComparison.Ordinal) =>
                _activePlexSectionTitle ?? LocalizationManager.Current.PlexServers,
            _ when tag.StartsWith("HyperFidelServer:", StringComparison.Ordinal) =>
                _activeHyperFidelServer?.Name ?? LocalizationManager.Current.HyperFidelServers,
            _ when tag.StartsWith("HyperFidelServerPlaylist:", StringComparison.Ordinal) => GetHyperFidelPlaylistName(tag),
            _ when tag.StartsWith("Radio:", StringComparison.Ordinal) => GetRadioName(tag),
            _ when tag.StartsWith("Podcast:", StringComparison.Ordinal) => GetPodcastName(tag),
            _         => tag.StartsWith("Playlist:") ? GetPlaylistName(tag) : tag
        };
        AnimateContentHeader();

        // Stop any in-flight unified folder-tree load so a late completion cannot mutate
        // the tree or the count of the view the user is switching to.
        CancelAndDispose(ref _folderViewCts);
        CancelGenreCloudViewLoad();
        if (!CanPublish()) return;
        CancelPlexViewLoad();
        if (!CanPublish()) return;
        _plexDetailVisible = false;
        ContentDataGrid.ItemsSource = null;
        AlbumArtworkListBox.ItemsSource = null;
        ArtistArtworkListBox.ItemsSource = null;
        _albumArtworkRows = [];
        _artistArtworkRows = [];
        _visibleAlbumArtworkRows.Clear();
        _visibleArtistArtworkRows.Clear();
        UpdateAlphabetIndex(null, false);
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        GenreCloudSurface.IsVisible = false;
        ContentDataGrid.Margin = new Thickness(0);
        AlbumArtworkListBox.Margin = new Thickness(0);
        AiChatViewControl.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        PodcastEpisodesView.IsVisible = false;
        HideAlbumDetailHeader();
        ContentCountTextBlock.Text  = "";
        var isHyperFidelServerTag = tag.StartsWith("HyperFidelServer:", StringComparison.Ordinal);
        var isHyperFidelTracksTag = TryParseHyperFidelServerTag(tag, out _, out var hyperfidelViewForHeader) &&
                                 hyperfidelViewForHeader == "Tracks";
        SearchTextBox.IsVisible = isHyperFidelTracksTag ||
                                   !(tag is "InternetRadio" or "Podcasts" or "Queue" or "AiChat" or "GenreCloud"
                                        or "RecentAlbumsAll" or "RecentlyPlayedAll" ||
                                    tag.StartsWith("Radio:", StringComparison.Ordinal) ||
                                    tag.StartsWith("Podcast:", StringComparison.Ordinal) ||
                                    tag.StartsWith("PlexLibrary:", StringComparison.Ordinal) ||
                                    tag.StartsWith("HyperFidelServerPlaylist:", StringComparison.Ordinal) ||
                                    isHyperFidelServerTag);
        UpdateEntityFavoritesFilterToggle(tag);
        AlbumViewModeBorder.IsVisible = tag is "Albums" or "Artists";
        PlexViewModeBorder.IsVisible = tag.StartsWith("PlexLibrary:", StringComparison.Ordinal);
        HyperFidelServerViewModeBorder.IsVisible = false;
        if (tag is "Albums" or "Artists")
            SetViewModeButtons(tag == "Albums" ? _showAlbumArtworkView : _showArtistArtworkView);
        TrackFilterButton.IsVisible = tag == "Tracks" ? true : false;
        SaveSmartPlaylistButton.IsVisible = tag == "Tracks" ? true : false;
        ClearQueueButton.IsVisible = tag == "Queue";
        ClearQueueButton.IsEnabled = _queue.Count > 0;
        InfiniteMixButton.IsVisible = tag == "Queue";
        InfiniteMixActionsPanel.IsVisible = tag == "Queue" && _infiniteMixEnabled;
        InfiniteMixStatusTextBlock.IsVisible = tag == "Queue" && _infiniteMixEnabled;
        SaveQueueAsPlaylistButton.IsVisible = tag == "Queue";
        SaveQueueAsPlaylistButton.IsEnabled =
            _queue.Any(item => CanPersistQueuePath(item.FilePath));
        UpdateRestoreQueueButtonState(tag);
        if (tag == "Queue" && _infiniteMixEnabled)
            RestoreQueueButton.IsVisible = false;
        if (tag == "Tracks") UpdateSaveSmartPlaylistButtonState();
        TrackFilterPopup.IsOpen = false;
        try
        {
            if (tag == "Dashboard")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await ShowDashboardAsync();
            }
            else if (tag == "RecentAlbumsAll")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await BuildAllRecentAlbumsViewAsync();
            }
            else if (tag == "RecentlyPlayedAll")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await BuildAllRecentlyPlayedViewAsync();
            }
            else if (tag == "AiChat")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                AiChatViewControl.IsVisible = true;
            }
            else if (tag == "InternetRadio")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                InternetRadioView.IsVisible = true;
                await EnsureRadioFilterCatalogAsync();
                if (RadioStationsDataGrid.ItemsSource is null)
                {
                    RadioStatusTextBlock.Text = LocalizationManager.Current.RadioEmptyState;
                    RadioStatusTextBlock.IsVisible = true;
                }
            }
            else if (tag == "Podcasts")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                PodcastView.IsVisible = true;
                PodcastEpisodesView.IsVisible = false;
                await EnsurePodcastFilterCatalogAsync();
                if (PodcastsDataGrid.ItemsSource is null)
                {
                    PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastEmptyState;
                    PodcastStatusTextBlock.IsVisible = true;
                }
            }
            else if (tag == "Queue")
            {
                ContentDataGrid.IsVisible = true;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                ApplyColumns("Queue");
                RefreshQueueRows();
            }
            else if (tag == "GenreCloud")
            {
                ContentDataGrid.IsVisible = true;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                GenreCloudSurface.IsVisible = true;
                ApplyColumns("Tracks");
                if (!_navigationRestores.ShouldSuppressHistory)
                    _genreCloudSelectedKey = null;
                await ShowGenreCloudAsync(_genreCloudSelectedKey, CanPublish);
            }
            else if (tag.StartsWith("Podcast:", StringComparison.Ordinal) &&
                     long.TryParse(tag.AsSpan("Podcast:".Length), out var podcastId))
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                PodcastView.IsVisible = true;
                await ShowSavedPodcastAsync(podcastId);
            }
            else if (tag.StartsWith("Radio:", StringComparison.Ordinal) &&
                     long.TryParse(tag.AsSpan("Radio:".Length), out var radioId))
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                InternetRadioView.IsVisible = true;
                _ = PlaySavedRadioAsync(radioId);
            }
            else if (tag.StartsWith("PlexLibrary:", StringComparison.Ordinal))
            {
                await ShowPlexLibraryAsync(tag, CanPublish);
            }
            else if (isHyperFidelServerTag)
            {
                await ShowHyperFidelServerAsync(tag);
            }
            else if (tag.StartsWith("HyperFidelServerPlaylist:", StringComparison.Ordinal))
            {
                await ShowHyperFidelPlaylistAsync(tag);
            }
            else if (tag == "Folders")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = true;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                await ShowUnifiedFolderTreeAsync();
            }
            else
            {
                var showArtwork = tag == "Albums"
                    ? _showAlbumArtworkView
                    : tag == "Artists" && _showArtistArtworkView;
                ContentDataGrid.IsVisible = !(showArtwork);
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = tag == "Albums" && _showAlbumArtworkView
                    ? true : false;
                ArtistArtworkListBox.IsVisible = tag == "Artists" && _showArtistArtworkView
                    ? true : false;

                if (tag is "Artists" or "Albums" or "Tracks")
                {
                    await BindLocalRowsAndStartRemoteAppendAsync(tag);
                }
                else
                {
                    if (_activePlaylistId is long storedPlaylistId)
                        await ShowStoredPlaylistAsync(storedPlaylistId);
                    else
                    {
                        var rows = await Task.Run(() => QueryRows(tag));
                        if (!CanPublish()) return;
                        ApplyColumns(tag);
                        ContentDataGrid.ItemsSource = rows;
                        UpdateAlphabetIndex(rows, false);
                        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    }
                }
            }
        }
        finally
        {
            loading?.Dispose();
            if (CanPublish() && tag is not ("Dashboard" or "GenreCloud"))
                FadeInVisibleContentSurface(CanPublish);
            LogUiDiagnostics(
                $"ShowTopLevelViewAsync finish tag={tag} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms gridVisible={ContentDataGrid.IsVisible} gridItems={GetDiagnosticItemCount(ContentDataGrid.ItemsSource)}");
        }
    }

    private void ShowContentLoadingSkeleton()
    {
        _contentLoadingDepth++;
        if (_contentLoadingDepth > 1)
            return;
        var generation = ++_contentLoadingGeneration;

        ContentLoadingOverlay.Opacity = 0;
        ContentLoadingOverlay.IsVisible = true;
        ContentLoadingOverlay.IsHitTestVisible = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_contentLoadingGeneration == generation && _contentLoadingDepth > 0)
                    ContentLoadingOverlay.Opacity = 1;
            },
            DispatcherPriority.Render);
    }

    private void HideContentLoadingSkeleton()
    {
        if (_contentLoadingDepth > 0)
            _contentLoadingDepth--;
        if (_contentLoadingDepth > 0)
            return;
        var generation = ++_contentLoadingGeneration;

        ContentLoadingOverlay.Opacity = 0;
        ContentLoadingOverlay.IsHitTestVisible = false;
        _ = HideContentLoadingSkeletonAsync(generation);
    }

    private async Task HideContentLoadingSkeletonAsync(int generation)
    {
        await Task.Delay(170);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_contentLoadingGeneration == generation &&
                _contentLoadingDepth == 0)
            {
                ContentLoadingOverlay.Opacity = 0;
                ContentLoadingOverlay.IsHitTestVisible = false;
                ContentLoadingOverlay.IsVisible = false;
            }
        });
    }

    /// <summary>Lifts currently visible surfaces only while the optional view owner remains current.</summary>
    /// <param name="canFade">Optional guard also checked by the deferred render callback.</param>
    private void FadeInVisibleContentSurface(Func<bool>? canFade = null)
    {
        if (canFade is not null && !canFade()) return;
        var animate = MotionPreferences.ShouldAnimate(_settings.ReduceMotion);
        foreach (var surface in _animatedViewSurfaces)
        {
            if (!surface.IsVisible)
            {
                surface.Opacity = 1;
                surface.RenderTransform = null;
                continue;
            }
            // The loading overlay already fades the old view out. Resetting the
            // surface opacity here would fade the new view twice and produce a
            // visible blink, so only the short lift animates.
            surface.Opacity = 1;
            surface.RenderTransform = animate ? CreateSurfaceSlideTransform() : null;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (canFade is not null && !canFade()) return;
            foreach (var surface in _animatedViewSurfaces)
            {
                if (!surface.IsVisible)
                    continue;
                surface.Opacity = 1;
                if (surface.RenderTransform is TranslateTransform transform)
                    transform.Y = 0;
            }
        }, DispatcherPriority.Render);
    }

    private void AnimateContentHeader()
    {
        if (!MotionPreferences.ShouldAnimate(_settings.ReduceMotion))
        {
            ContentTitleTextBlock.Opacity = 1;
            ContentTitleTextBlock.RenderTransform = null;
            return;
        }
        // Keep the title opaque and slide it; fading an already-visible title
        // from zero would blink for one frame.
        ContentTitleTextBlock.Opacity = 1;
        var transform = new TranslateTransform(0, -4);
        transform.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(160),
                Easing = new CubicEaseOut()
            }
        };
        ContentTitleTextBlock.RenderTransform = transform;
        Dispatcher.UIThread.Post(() =>
        {
            ContentTitleTextBlock.Opacity = 1;
            transform.Y = 0;
        }, DispatcherPriority.Render);
    }

    private static TranslateTransform CreateSurfaceSlideTransform()
    {
        var transform = new TranslateTransform(0, 8);
        transform.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new CubicEaseOut()
            }
        };
        return transform;
    }

}
