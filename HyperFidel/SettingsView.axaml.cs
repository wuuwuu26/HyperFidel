using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HyperFidel.Audio;
using HyperFidel.Controls;
using HyperFidel.Library;
using HyperFidel.Localization;
using AvaloniaApp = Avalonia.Application;
using UiLanguage = HyperFidel.Localization.Language;

namespace HyperFidel;

/// <summary>Provides the complete settings experience embedded in the main window.</summary>
internal partial class SettingsView : UserControl
{
    private const int MaximumEqualizerFilters = 512;

    private sealed record SettingChoice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }



    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore = new();
    private readonly List<string> _libraryPaths = [];
    private string _scheduledBackupDirectory = string.Empty;
    private long _scheduledBackupLastRunUnix;
    /// <summary>Owns active directory scans through completion, including cancellation after removal.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _activeScans = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<List<string>>? _onLibraryPathsChanged;
    private readonly Action<bool, EqualizerProfile?>? _onEqualizerPreviewChanged;
    private readonly bool _originalEqualizerEnabled;
    private readonly EqualizerProfile? _originalEqualizerProfile;
    private readonly List<EqualizerProfile> _equalizerProfiles = [];
    private readonly List<OutputProfile> _outputProfiles = [];
    private EqualizerProfile? _equalizerProfile;
    private OutputProfile? _outputProfile;
    private int _equalizerPreviewVersion;
    private bool _initializing = true;
    private bool _rebuildingEqualizerEditor;
    private bool _settingsAccepted;
    private UserProfileManager? _profileManager;
    private bool _profileSelectionUpdating;

    /// <summary>
    /// Occurs after a local-library scan has added, updated, or removed
    /// indexed tracks so catalog consumers can invalidate their session caches.
    /// </summary>
    internal event Action? LocalLibraryChanged;

    /// <summary>Runs ordinary directory scans; isolated workflow checks may replace the transport.</summary>
    internal Func<string, IProgress<ScanProgress>?, bool, CancellationToken, Task<ScanResult>> DirectoryScanner { get; set; }
        = LibraryScanner.ScanAsync;

    /// <summary>Raised when the user asks to review and resolve duplicate files.</summary>
    internal event Action? DuplicateResolutionRequested;
    /// <summary>Raised after the active user profile changes.</summary>
    internal event Action<string>? ProfileChanged;
    private bool _metadataAnalysisLoaded;
    private IReadOnlyList<MetadataProblemRow> _metadataDoctorRows = [];
    private CancellationTokenSource? _metadataAnalysisCts;
    private bool _metadataAnalysisRunning;
    private bool _metadataRepairRunning;
    private bool _metadataDeactivated;
    private CancellationTokenSource? _replayGainCalculationCts;
    private CancellationTokenSource? _audioFeatureAnalysisCts;

    /// <summary>
    /// Initializes a runtime-loader instance with default settings.
    /// </summary>
    public SettingsView()
        : this(new AppSettings())
    {
    }

    /// <summary>Initializes the settings view from persisted application settings.</summary>
    /// <param name="settings">Settings displayed and edited by the window.</param>
    /// <param name="onLibraryPathsChanged">Optional callback for immediate library-path updates.</param>
    /// <param name="onEqualizerPreviewChanged">Optional callback for live equalizer preview changes.</param>
    public SettingsView(
        AppSettings settings,
        Action<List<string>>? onLibraryPathsChanged = null,
        Action<bool, EqualizerProfile?>? onEqualizerPreviewChanged = null)
    {
        InitializeComponent();
        _settings = settings;
        _profileManager = new UserProfileManager(settings);
        _onLibraryPathsChanged = onLibraryPathsChanged;
        _onEqualizerPreviewChanged = onEqualizerPreviewChanged;
        _originalEqualizerEnabled = settings.EqualizerEnabled;
        _originalEqualizerProfile = settings.EqualizerProfile?.Clone();
        _outputProfiles.AddRange((settings.OutputProfiles ?? []).Select(CloneOutputProfile));
        OutputProfileComboBox.ItemsSource = _outputProfiles;
        OutputProfileComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(OutputProfile.Name));
        _outputProfile = _outputProfiles.FirstOrDefault(profile =>
            string.Equals(
                profile.Name,
                settings.SelectedOutputProfileName,
                StringComparison.OrdinalIgnoreCase));
        OutputProfileComboBox.SelectedItem = _outputProfile;
        var themeChoices = new[]
        {
            new SettingChoice<AppTheme>(AppTheme.Light, LocalizationManager.Current.ThemeLight),
            new SettingChoice<AppTheme>(AppTheme.Dark, LocalizationManager.Current.ThemeDark)
        };
        ThemeComboBox.ItemsSource = themeChoices;
        ThemeComboBox.SelectedItem = themeChoices.First(choice => choice.Value == settings.Theme);
        var languageChoices = new[]
        {
            new SettingChoice<UiLanguage>(UiLanguage.German, LocalizationManager.Current.LanguageGerman),
            new SettingChoice<UiLanguage>(UiLanguage.English, LocalizationManager.Current.LanguageEnglish),
            new SettingChoice<UiLanguage>(UiLanguage.French, LocalizationManager.Current.LanguageFrench),
            new SettingChoice<UiLanguage>(UiLanguage.Spanish, LocalizationManager.Current.LanguageSpanish),
            new SettingChoice<UiLanguage>(UiLanguage.Russian, LocalizationManager.Current.LanguageRussian),
            new SettingChoice<UiLanguage>(UiLanguage.ChineseSimplified, LocalizationManager.Current.LanguageChineseSimplified),
            new SettingChoice<UiLanguage>(UiLanguage.Hindi, LocalizationManager.Current.LanguageHindi)
        };
        LanguageComboBox.ItemsSource = languageChoices;
        LanguageComboBox.SelectedItem = languageChoices.First(choice => choice.Value == settings.Language);
        var genreCloudBackgroundChoices = new[]
        {
            new SettingChoice<GenreCloudBackgroundMode>(GenreCloudBackgroundMode.None, LocalizationManager.Current.GenreCloudBackgroundNone),
            new SettingChoice<GenreCloudBackgroundMode>(GenreCloudBackgroundMode.Albums, LocalizationManager.Current.GenreCloudBackgroundAlbums),
            new SettingChoice<GenreCloudBackgroundMode>(GenreCloudBackgroundMode.Artists, LocalizationManager.Current.GenreCloudBackgroundArtists)
        };
        GenreCloudBackgroundComboBox.ItemsSource = genreCloudBackgroundChoices;
        GenreCloudBackgroundComboBox.SelectedItem = genreCloudBackgroundChoices.FirstOrDefault(
            choice => choice.Value == settings.GenreCloudBackground) ?? genreCloudBackgroundChoices[2];
        GenreCloudVisibilitySlider.Value = Math.Round(
            Math.Clamp(settings.GenreCloudBackgroundOpacity, 0, 1) * 100);
        UpdateGenreCloudVisibilityText();
        UpdateGenreCloudVisibilityEnabled();
        // A cap keeps a driver that advertises an unusable maximum rate out of reach; zero
        // leaves the choice entirely to the device.
        var maxOutputSampleRateChoices = new[]
        {
            new SettingChoice<int>(0, LocalizationManager.Current.MaxOutputSampleRateAutomatic),
            new SettingChoice<int>(384_000, "384 kHz"),
            new SettingChoice<int>(352_800, "352.8 kHz"),
            new SettingChoice<int>(192_000, "192 kHz"),
            new SettingChoice<int>(176_400, "176.4 kHz"),
            new SettingChoice<int>(96_000, "96 kHz"),
            new SettingChoice<int>(88_200, "88.2 kHz"),
            new SettingChoice<int>(48_000, "48 kHz"),
            new SettingChoice<int>(44_100, "44.1 kHz")
        };
        var replayGainChoices = new[]
        {
            new SettingChoice<ReplayGainMode>(ReplayGainMode.Off, LocalizationManager.Current.ReplayGainOff),
            new SettingChoice<ReplayGainMode>(ReplayGainMode.Track, LocalizationManager.Current.ReplayGainTrack),
            new SettingChoice<ReplayGainMode>(ReplayGainMode.Album, LocalizationManager.Current.ReplayGainAlbum)
        };
        ReplayGainModeComboBox.ItemsSource = replayGainChoices;
        ReplayGainModeComboBox.SelectedItem =
            replayGainChoices.FirstOrDefault(choice => choice.Value == settings.ReplayGainMode)
            ?? replayGainChoices[0];
        CalculateReplayGainDuringScanCheckBox.IsChecked = settings.CalculateMissingReplayGainDuringScan;
        AlwaysConvertDsdToPcmCheckBox.IsChecked = settings.AlwaysConvertDsdToPcm;
        DsdOverPcmCheckBox.IsChecked = settings.DsdOverPcmEnabled;
        AlwaysConvertDsdToPcmCheckBox.IsCheckedChanged += AlwaysConvertDsdToPcmCheckBox_OnIsCheckedChanged;
        PcmOutputBoostCheckBox.IsChecked = settings.PcmOutputBoostEnabled;
        MaxOutputSampleRateComboBox.ItemsSource = maxOutputSampleRateChoices;
        MaxOutputSampleRateComboBox.SelectedItem = maxOutputSampleRateChoices
            .FirstOrDefault(choice => choice.Value == Math.Clamp(settings.MaxOutputSampleRateHz, 0, 768_000))
            ?? maxOutputSampleRateChoices[0];
        DsdPcmSampleRateComboBox.ItemsSource = maxOutputSampleRateChoices;
        DsdPcmSampleRateComboBox.SelectedItem = maxOutputSampleRateChoices
            .FirstOrDefault(choice => choice.Value == Math.Clamp(settings.DsdPcmSampleRateHz, 0, 768_000))
            ?? maxOutputSampleRateChoices[0];
        NonGaplessCrossfadeNumericUpDown.Value = (decimal)Math.Clamp(
            settings.NonGaplessCrossfadeSeconds,
            0,
            10);
        _equalizerProfiles.AddRange((settings.EqualizerProfiles ?? [])
            .Select(static profile => profile.Clone()));
        if (_equalizerProfiles.Count == 0 && settings.EqualizerProfile is not null)
            _equalizerProfiles.Add(settings.EqualizerProfile.Clone());
        EqualizerProfileComboBox.ItemsSource = _equalizerProfiles;
        EqualizerProfileComboBox.DisplayMemberBinding =
            new Avalonia.Data.Binding(nameof(EqualizerProfile.Name));
        _equalizerProfile = _equalizerProfiles.FirstOrDefault(profile =>
            string.Equals(
                profile.Name,
                settings.SelectedEqualizerProfileName ?? settings.EqualizerProfile?.Name,
                StringComparison.OrdinalIgnoreCase));
        EqualizerProfileComboBox.SelectedItem = _equalizerProfile;
        EqualizerEnabledCheckBox.IsChecked = settings.EqualizerEnabled;
        CrossfeedEnabledCheckBox.IsChecked = settings.CrossfeedEnabled;
        CrossfeedStrengthComboBox.ItemsSource = new[]
        {
            LocalizationManager.Current.CrossfeedLight,
            LocalizationManager.Current.CrossfeedMedium,
            LocalizationManager.Current.CrossfeedStrong
        };
        CrossfeedStrengthComboBox.SelectedIndex = Math.Clamp((int)settings.CrossfeedStrength, 0, 2);
        RefreshEqualizerProfileText();
        RebuildEqualizerEditor();
        UpdateSubsystemStatusBadges();
        ShowQueueItemCheckBox.IsChecked         = settings.ShowQueueItem;
        ReduceMotionCheckBox.IsChecked          = settings.ReduceMotion;
        StartMaximizedCheckBox.IsChecked             = settings.StartMaximized;
        ShowLocalLibrarySectionCheckBox.IsChecked = settings.ShowLocalLibrarySection;
        _libraryPaths.AddRange(settings.LibraryPaths);
        _scheduledBackupDirectory = settings.ScheduledBackup.ResolveDirectory();
        _scheduledBackupLastRunUnix = settings.ScheduledBackup.LastRunAtUnix;
        ScheduledBackupEnabledCheckBox.IsChecked = settings.ScheduledBackup.Enabled;
        ScheduledBackupIntervalInput.Value = Math.Clamp(settings.ScheduledBackup.IntervalDays, 1, 365);
        ScheduledBackupRetentionInput.Value = Math.Clamp(settings.ScheduledBackup.RetentionCount, 1, 50);
        UpdateScheduledBackupStatus();
        UserProfileComboBox.ItemsSource = _profileManager.Profiles;
        UserProfileComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(UserProfile.DisplayName));
        UserProfileComboBox.SelectedItem = _profileManager.ActiveProfile;
        RebuildDirectoryList();
        _initializing = false;
        RefreshOutputProfileButtons();
        NavListBox.SelectedIndex = 1;
    }

    /// <summary>Updates the platform-appropriate audio and MCP subsystem status badges.</summary>
    private void UpdateSubsystemStatusBadges()
    {
        var loc = LocalizationManager.Current;

        bool ffmpeg = FfmpegLocator.IsAvailable();
        FfmpegStatusBadge.State = ffmpeg ? StatusBadgeState.Ok : StatusBadgeState.Warning;
        FfmpegStatusBadge.Text = ffmpeg ? loc.StatusReady : loc.StatusUnavailable;

        var showAsio = OperatingSystem.IsWindows();
        AsioStatusPanel.IsVisible = showAsio;
        CwAsioStatusPanel.IsVisible = showAsio;
        if (!showAsio)
            return;

        bool asio = SteinbergAsioStream.IsAvailable;
        AsioStatusBadge.State = asio ? StatusBadgeState.Ok : StatusBadgeState.Off;
        AsioStatusBadge.Text = asio ? loc.StatusAvailable : loc.StatusUnavailable;

        bool cwAsio = SteinbergAsioStream.IsCwAsioAvailable;
        CwAsioStatusBadge.State = cwAsio ? StatusBadgeState.Ok : StatusBadgeState.Off;
        CwAsioStatusBadge.Text = cwAsio ? loc.StatusAvailable : loc.StatusUnavailable;
    }

    /// <summary>Raised when the embedded settings view requests save or cancel.</summary>
    internal event EventHandler<bool>? CompletionRequested;

    /// <summary>Gets the driver name of the selected output profile.</summary>
    public string? SelectedDriverName => _outputProfile?.SelectedDriverName;
    /// <summary>Gets the WASAPI device ID of the selected output profile.</summary>
    public string? SelectedWasapiDeviceId => _outputProfile?.SelectedWasapiDeviceId;
    /// <summary>Gets the WASAPI device display name of the selected output profile.</summary>
    public string? SelectedWasapiDeviceName => _outputProfile?.SelectedWasapiDeviceName;
    /// <summary>Gets the output backend of the selected output profile.</summary>
    public OutputBackend SelectedOutputBackend => _outputProfile?.Backend ?? OutputBackend.Wasapi;
    /// <summary>Gets independent copies of all configured output profiles.</summary>
    public IReadOnlyList<OutputProfile> SelectedOutputProfiles =>
        _outputProfiles.Select(CloneOutputProfile).ToList().AsReadOnly();
    /// <summary>Gets the name of the currently selected output profile.</summary>
    public string? SelectedOutputProfileName => _outputProfile?.Name;
    /// <summary>Gets the ReplayGain mode selected in the settings view.</summary>
    public ReplayGainMode SelectedReplayGainMode =>
        ReplayGainModeComboBox.SelectedItem is SettingChoice<ReplayGainMode> choice
            ? choice.Value
            : ReplayGainMode.Off;
    /// <summary>Gets a value indicating whether scans should calculate missing ReplayGain values.</summary>
    public bool CalculateMissingReplayGainDuringScan => CalculateReplayGainDuringScanCheckBox.IsChecked == true;

    /// <summary>Gets whether automatic library backups are enabled.</summary>
    public bool ScheduledBackupEnabledValue => ScheduledBackupEnabledCheckBox.IsChecked == true;

    /// <summary>Gets the configured minimum number of days between automatic backups.</summary>
    public int ScheduledBackupIntervalValue =>
        (int)Math.Clamp(ScheduledBackupIntervalInput.Value ?? 7m, 1m, 365m);

    /// <summary>Gets the configured number of backups kept before older ones are removed.</summary>
    public int ScheduledBackupRetentionValue =>
        (int)Math.Clamp(ScheduledBackupRetentionInput.Value ?? 3m, 1m, 50m);

    /// <summary>Gets the configured backup folder.</summary>
    public string ScheduledBackupDirectoryValue => _scheduledBackupDirectory;

    /// <summary>
    /// Gets or sets the callback that runs a library backup. The argument forces a
    /// run regardless of the configured interval.
    /// </summary>
    public Func<bool, Task<bool>>? RunScheduledBackup { get; set; }

    /// <summary>Updates the displayed last-run timestamp after an automatic or manual backup.</summary>
    /// <param name="unixSeconds">Unix timestamp of the completed backup.</param>
    public void SetScheduledBackupLastRun(long unixSeconds)
    {
        _scheduledBackupLastRunUnix = unixSeconds;
        UpdateScheduledBackupStatus();
    }

    private void UpdateScheduledBackupStatus()
    {
        ScheduledBackupFolderTextBlock.Text = _scheduledBackupDirectory;
        ScheduledBackupStatusTextBlock.Text = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Current.ScheduledBackupLastRun,
            _scheduledBackupLastRunUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(_scheduledBackupLastRunUnix)
                    .ToLocalTime()
                    .ToString("g", CultureInfo.CurrentCulture)
                : LocalizationManager.Current.ScheduledBackupNever);
    }

    private async void ScheduledBackupFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Current.ScheduledBackupFolder,
            AllowMultiple = false
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { Length: > 0 } path)
            return;
        _scheduledBackupDirectory = path;
        UpdateScheduledBackupStatus();
    }

    private async void RunScheduledBackupNowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (RunScheduledBackup is null)
            return;
        RunScheduledBackupNowButton.IsEnabled = false;
        ScheduledBackupStatusTextBlock.Text = LocalizationManager.Current.ScheduledBackupRunning;
        try
        {
            var completed = await RunScheduledBackup(true);
            ScheduledBackupStatusTextBlock.Text = completed
                ? LocalizationManager.Current.ScheduledBackupFinished
                : LocalizationManager.Current.ScheduledBackupFailed;
        }
        finally
        {
            RunScheduledBackupNowButton.IsEnabled = true;
        }
    }
    /// <summary>Gets a value indicating whether DSF and DFF sources should always be converted to PCM.</summary>
    public bool AlwaysConvertDsdToPcm => AlwaysConvertDsdToPcmCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether DSD should be transported through DoP.</summary>
    public bool DsdOverPcmEnabled => DsdOverPcmCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether PCM playback should receive the additional output boost.</summary>
    public bool PcmOutputBoostEnabled => PcmOutputBoostCheckBox.IsChecked == true;

    /// <summary>Gets the configured maximum PCM output sample rate in hertz, or zero for automatic.</summary>
    public int MaxOutputSampleRateHz =>
        MaxOutputSampleRateComboBox.SelectedItem is SettingChoice<int> choice ? choice.Value : 0;

    /// <summary>Gets the configured PCM output rate for DSD conversion, or zero for the conventional rate.</summary>
    public int DsdPcmSampleRateHz =>
        DsdPcmSampleRateComboBox.SelectedItem is SettingChoice<int> choice ? choice.Value : 0;

    /// <summary>Disables forced PCM conversion when DoP is selected.</summary>
    /// <param name="sender">DoP checkbox.</param>
    /// <param name="e">Check-state event arguments.</param>
    private void DsdOverPcmCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (DsdOverPcmCheckBox.IsChecked == true)
            AlwaysConvertDsdToPcmCheckBox.IsChecked = false;
    }

    /// <summary>Disables DoP when lossy DSD-to-PCM conversion is selected.</summary>
    /// <param name="sender">PCM-conversion checkbox.</param>
    /// <param name="e">Check-state event arguments.</param>
    private void AlwaysConvertDsdToPcmCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (AlwaysConvertDsdToPcmCheckBox.IsChecked == true)
            DsdOverPcmCheckBox.IsChecked = false;
    }
    /// <summary>Gets the configured non-gapless queue fade duration in seconds.</summary>
    public double NonGaplessCrossfadeSeconds => (double)(NonGaplessCrossfadeNumericUpDown.Value ?? 0);
    /// <summary>Gets a value indicating whether the imported equalizer profile is enabled.</summary>
    public bool EqualizerEnabled =>
        _equalizerProfile is not null && EqualizerEnabledCheckBox.IsChecked == true;
    /// <summary>Gets an independent copy of the imported equalizer profile.</summary>
    public EqualizerProfile? SelectedEqualizerProfile => _equalizerProfile?.Clone();
    /// <summary>Gets independent copies of all configured equalizer profiles.</summary>
    public IReadOnlyList<EqualizerProfile> SelectedEqualizerProfiles =>
        _equalizerProfiles.Select(static profile => profile.Clone()).ToList().AsReadOnly();
    /// <summary>Gets the selected equalizer profile name.</summary>
    public string? SelectedEqualizerProfileName => _equalizerProfile?.Name;
    /// <summary>Gets a value indicating whether headphone crossfeed is enabled.</summary>
    public bool SelectedCrossfeedEnabled => CrossfeedEnabledCheckBox.IsChecked == true;
    /// <summary>Gets the selected headphone crossfeed strength.</summary>
    public CrossfeedStrength SelectedCrossfeedStrength => CrossfeedStrengthComboBox.SelectedIndex switch
    {
        0 => CrossfeedStrength.Light,
        2 => CrossfeedStrength.Strong,
        _ => CrossfeedStrength.Medium
    };
    public IReadOnlyList<string> SelectedLibraryPaths => _libraryPaths.AsReadOnly();
    public AppTheme SelectedTheme =>
        ThemeComboBox.SelectedItem is SettingChoice<AppTheme> theme ? theme.Value : AppTheme.Dark;
    public UiLanguage SelectedLanguage =>
        LanguageComboBox.SelectedItem is SettingChoice<UiLanguage> language ? language.Value : UiLanguage.German;
    /// <summary>Gets the selected artwork source for Genre Cloud backgrounds.</summary>
    public GenreCloudBackgroundMode SelectedGenreCloudBackground =>
        GenreCloudBackgroundComboBox.SelectedItem is SettingChoice<GenreCloudBackgroundMode> choice
            ? choice.Value
            : GenreCloudBackgroundMode.Artists;
    /// <summary>Gets the selected Genre Cloud tile opacity from zero to one.</summary>
    public double SelectedGenreCloudBackgroundOpacity =>
        Math.Clamp(GenreCloudVisibilitySlider.Value / 100d, 0, 1);
    /// <summary>Gets a value indicating whether the Up Next sidebar item should be visible.</summary>
    public bool ShowQueueItem => ShowQueueItemCheckBox.IsChecked == true;

    /// <summary>Gets a value indicating whether optional UI motion is disabled.</summary>
    public bool ReduceMotionValue => ReduceMotionCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the main window should start maximized.</summary>
    public bool StartMaximized => StartMaximizedCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the Library sidebar section should be visible.</summary>
    public bool ShowLocalLibrarySection => ShowLocalLibrarySectionCheckBox.IsChecked == true;


    private static OutputProfile CloneOutputProfile(OutputProfile profile) => new()
    {
        Name = profile.Name,
        Backend = profile.Backend,
        SelectedDriverName = profile.SelectedDriverName,
        SelectedWasapiDeviceId = profile.SelectedWasapiDeviceId,
        SelectedWasapiDeviceName = profile.SelectedWasapiDeviceName,
    };

    private Button CreateStyledButton(string content, double width, double height, Thickness margin = default)
    {
        var btn = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            Margin = margin
        };
        if (TryGetResource("SettingsButtonTheme", ThemeVariant.Default, out var res) && res is ControlTheme theme)
            btn.Theme = theme;
        return btn;
    }


    private CancellationTokenSource? _hyperfidelStatusCts;


















    /// <summary>Stops background work and restores the original live equalizer preview when needed.</summary>
    internal void Deactivate()
    {
        _metadataDeactivated = true;
        foreach (var cts in _activeScans.Values)
            cts.Cancel();
        _hyperfidelStatusCts?.Cancel();
        _metadataAnalysisCts?.Cancel();
        _replayGainCalculationCts?.Cancel();
        _audioFeatureAnalysisCts?.Cancel();
        Interlocked.Increment(ref _equalizerPreviewVersion);
        if (!_settingsAccepted)
        {
            var originalProfile = _originalEqualizerProfile?.Clone();
            _ = Task.Run(() => _onEqualizerPreviewChanged?.Invoke(
                _originalEqualizerEnabled,
                originalProfile));
        }
    }

    /// <summary>Returns the main window hosting this settings view.</summary>
    /// <returns>The hosting window.</returns>
    private Window GetHostWindow() =>
        TopLevel.GetTopLevel(this) as Window
        ?? throw new InvalidOperationException("The settings view is not attached to a window.");

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void NavListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;
        AudioDevicePanel.IsVisible             = tag == "AudioDevice";
        LibraryPanel.IsVisible                 = tag == "Library";
        MetadataPanel.IsVisible                = tag == "Metadata";
        AppearancePanel.IsVisible              = tag == "Appearance";
        UserProfilesPanel.IsVisible            = tag == "UserProfiles";
        SettingsContentScrollViewer.Offset = new Vector(0, 0);
        AnimateSettingsSection(tag switch
        {
            "AudioDevice" => AudioDevicePanel,
            "Library" => LibraryPanel,
            "Metadata" => MetadataPanel,
            "Appearance" => AppearancePanel,
            "UserProfiles" => UserProfilesPanel,
            _ => null
        });
        if (tag == "Metadata" && !_metadataAnalysisLoaded)
            _ = LoadMetadataProblemsAsync();
    }

    /// <summary>Lifts the newly visible settings section into place.</summary>
    /// <param name="section">Section panel selected by the settings navigation.</param>
    private void AnimateSettingsSection(Control? section)
    {
        if (section is null || !section.IsVisible)
            return;
        if (!MotionPreferences.ShouldAnimate(ReduceMotionValue))
        {
            section.Transitions = null;
            section.Opacity = 1;
            section.RenderTransform = null;
            return;
        }
        // The old section is hidden in the same dispatcher turn. Start the new
        // section slightly dimmed rather than fully transparent, so it never
        // leaves a blank frame while it slides into place.
        section.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(120),
                Easing = new CubicEaseOut()
            }
        };
        section.Opacity = 0.85;
        var transform = new TranslateTransform(0, 6);
        transform.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(160),
                Easing = new CubicEaseOut()
            }
        };
        section.RenderTransform = transform;
        Dispatcher.UIThread.Post(() =>
        {
            section.Opacity = 1;
            transform.Y = 0;
        }, DispatcherPriority.Render);
    }

    private void UserProfileComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_profileSelectionUpdating || UserProfileComboBox.SelectedItem is not UserProfile profile ||
            _profileManager is null || !_profileManager.Activate(profile.Id))
            return;
        AudioDatabase.SetActiveProfile(profile.Id);
        ProfileChanged?.Invoke(profile.Id);
    }

    private async void CreateUserProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profileManager is null)
            return;
        var dialog = new UserProfileDialog();
        if (await dialog.ShowDialog<bool>(GetHostWindow()) != true)
            return;
        try
        {
            var profile = _profileManager.Create(dialog.ProfileName, dialog.MigrateFavorites);
            _profileSelectionUpdating = true;
            UserProfileComboBox.SelectedItem = profile;
            _profileSelectionUpdating = false;
            AudioDatabase.SetActiveProfile(profile.Id);
            _settingsStore.Save(_settings);
            ProfileChanged?.Invoke(profile.Id);
        }
        catch (Exception ex)
        {
            await AppMessageBox.ShowAsync(ex.Message, LocalizationManager.Current.UserProfiles, GetHostWindow());
        }
    }

    private async void RenameUserProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profileManager is null || UserProfileComboBox.SelectedItem is not UserProfile profile)
            return;
        var dialog = new UserProfileDialog(showMigration: false);
        if (await dialog.ShowDialog<bool>(GetHostWindow()) != true)
            return;
        try
        {
            _profileManager.Rename(profile.Id, dialog.ProfileName);
            UserProfileComboBox.ItemsSource = null;
            UserProfileComboBox.ItemsSource = _profileManager.Profiles;
            UserProfileComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(UserProfile.DisplayName));
            UserProfileComboBox.SelectedItem = profile;
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            await AppMessageBox.ShowAsync(ex.Message, LocalizationManager.Current.UserProfiles, GetHostWindow());
        }
    }

    private async void DeleteUserProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profileManager is null || UserProfileComboBox.SelectedItem is not UserProfile profile)
            return;
        if (!await AppMessageBox.ConfirmAsync(
                string.Format(LocalizationManager.Current.UserProfileDeleteConfirm, profile.DisplayName),
                LocalizationManager.Current.UserProfileDelete,
                GetHostWindow()))
            return;
        try
        {
            _profileManager.Delete(profile.Id);
            UserProfileComboBox.ItemsSource = null;
            UserProfileComboBox.ItemsSource = _profileManager.Profiles;
            UserProfileComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(UserProfile.DisplayName));
            UserProfileComboBox.SelectedItem = _profileManager.ActiveProfile;
            AudioDatabase.SetActiveProfile(_profileManager.ActiveProfile.Id);
            _settingsStore.Save(_settings);
            ProfileChanged?.Invoke(_profileManager.ActiveProfile.Id);
        }
        catch (Exception ex)
        {
            await AppMessageBox.ShowAsync(ex.Message, LocalizationManager.Current.UserProfiles, GetHostWindow());
        }
    }

    private async Task LoadMetadataProblemsAsync()
    {
        if (_metadataDeactivated || _metadataAnalysisRunning || _metadataRepairRunning)
            return;
        _metadataAnalysisCts?.Cancel();
        _metadataAnalysisCts?.Dispose();
        _metadataAnalysisCts = new CancellationTokenSource();
        var cancellationToken = _metadataAnalysisCts.Token;
        _metadataAnalysisLoaded = true;
        InitializeMetadataFilters();
        _metadataAnalysisRunning = true;
        var inspectFiles = MetadataInspectFilesCheckBox.IsChecked == true;
        var activity = new MetadataReviewActivity();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void UpdateProgress()
        {
            var snapshot = activity.Snapshot();
            MetadataActivityTextBlock.Text = snapshot.Text;
            MetadataAnalysisProgressBar.IsIndeterminate = !snapshot.Percent.HasValue;
            MetadataAnalysisProgressBar.Value = snapshot.Percent ?? 0;
        }
        timer.Tick += (_, _) => UpdateProgress();
        MetadataProgressPanel.IsVisible = true;
        UpdateProgress();
        timer.Start();
        RefreshMetadataAnalysisButton.IsEnabled = false;
        CancelMetadataAnalysisButton.IsEnabled = true;
        MetadataInspectFilesCheckBox.IsEnabled = false;
        _metadataDoctorRows = [];
        ApplyMetadataDoctorFilter();
        try
        {
            var localTask = Task.Run(() =>
            {
                using var database = AudioDatabase.OpenDefault();
                return LibraryMetadataRepairService.Analyze(
                    database.GetMetadataRepairTracks(), cancellationToken: cancellationToken,
                    inspectFiles: inspectFiles, progress: activity);
            }, cancellationToken);
            var localCandidates = await localTask;
            cancellationToken.ThrowIfCancellationRequested();
            var rows = localCandidates.Select(candidate => CreateMetadataProblemRow(
                    LocalizationManager.Current.LocalSource,
                    candidate.FolderPath,
                    candidate.Tracks.Count,
                    candidate.HighestSeverity,
                    candidate.Findings.Select<LibraryDoctorFinding, (string Code, int Count)>(
                        finding => (finding.Code, finding.AffectedTrackCount)),
                    candidate))
                .ToList();
            _metadataDoctorRows = rows;
            ApplyMetadataDoctorFilter();
            _metadataDoctorRows = rows;
            ApplyMetadataDoctorFilter();
        }
        catch (OperationCanceledException)
        {
            _metadataAnalysisLoaded = false;
            MetadataStatusTextBlock.Text = LocalizationManager.Current.MetadataAnalysisCancelled;
        }
        catch (Exception ex)
        {
            _metadataAnalysisLoaded = false;
            CrashLogger.Log(ex, "Library Doctor analysis");
            _metadataDoctorRows = [];
            MetadataProblemsDataGrid.ItemsSource = null;
            MetadataStatusTextBlock.Text = LocalizationManager.Current.MetadataAnalysisFailed;
        }
        finally
        {
            timer.Stop();
            _metadataAnalysisRunning = false;
            MetadataProgressPanel.IsVisible = false;
            MetadataInspectFilesCheckBox.IsEnabled = true;
            if (_metadataAnalysisCts?.Token == cancellationToken)
            {
                RefreshMetadataAnalysisButton.IsEnabled = true;
                CancelMetadataAnalysisButton.IsEnabled = false;
            }
        }
    }

    private void InitializeMetadataFilters()
    {
        if (MetadataSeverityFilterComboBox.ItemsSource is null)
        {
            var localized = LocalizationManager.Current;
            MetadataSeverityFilterComboBox.ItemsSource = new[]
            {
                new SettingChoice<LibraryDoctorSeverity?>(null, localized.MetadataSeverityAll),
                new SettingChoice<LibraryDoctorSeverity?>(LibraryDoctorSeverity.Error, localized.MetadataSeverityError),
                new SettingChoice<LibraryDoctorSeverity?>(LibraryDoctorSeverity.Warning, localized.MetadataSeverityWarning),
                new SettingChoice<LibraryDoctorSeverity?>(LibraryDoctorSeverity.Information, localized.MetadataSeverityInformation)
            };
            MetadataSeverityFilterComboBox.SelectedIndex = 0;
        }
        if (MetadataIssueFilterComboBox.ItemsSource is null)
        {
            var localized = LocalizationManager.Current;
            MetadataIssueFilterComboBox.ItemsSource = new[]
            {
                new SettingChoice<string?>(null, localized.MetadataIssueAll),
                new SettingChoice<string?>("album", localized.MetadataIssueAlbums),
                new SettingChoice<string?>("album-artist", localized.MetadataIssueArtists),
                new SettingChoice<string?>("title", localized.MetadataIssueMissingTitles),
                new SettingChoice<string?>("track-number", localized.MetadataIssueMissingNumbers),
                new SettingChoice<string?>("duplicate-track-number", localized.MetadataIssueDuplicateNumbers),
                new SettingChoice<string?>("replaygain", localized.MetadataIssueReplayGain),
                new SettingChoice<string?>("musicbrainz-id", localized.MetadataIssueMusicBrainzIds),
                new SettingChoice<string?>("incomplete-album", localized.MetadataIssueIncompleteAlbum),
                new SettingChoice<string?>("album-artwork", localized.MetadataIssueAlbumArtwork),
                new SettingChoice<string?>("artist-image", localized.MetadataIssueArtistImage),
                new SettingChoice<string?>("missing-file", localized.MetadataIssueMissingFiles),
                new SettingChoice<string?>("unreadable-file", localized.MetadataIssueUnreadableFiles),
                new SettingChoice<string?>("likely-duplicate", localized.MetadataIssueLikelyDuplicates),
                new SettingChoice<string?>("exact-duplicate", localized.MetadataIssueExactDuplicates),
                new SettingChoice<string?>("alternate-recording", localized.MetadataIssueAlternateRecordings),
                new SettingChoice<string?>("artist-name-variant", localized.MetadataIssueArtistNameVariants)
            };
            MetadataIssueFilterComboBox.SelectedIndex = 0;
        }
    }

    private void ApplyMetadataDoctorFilter()
    {
        var selected = (MetadataSeverityFilterComboBox.SelectedItem as SettingChoice<LibraryDoctorSeverity?>)?.Value;
        var issueCode = (MetadataIssueFilterComboBox.SelectedItem as SettingChoice<string?>)?.Value;
        IEnumerable<MetadataProblemRow> filtered = selected.HasValue
            ? _metadataDoctorRows.Where(row => row.SeverityValue == selected.Value)
            : _metadataDoctorRows;
        if (!string.IsNullOrWhiteSpace(issueCode))
            filtered = filtered.Where(row => row.FindingCodes.Contains(issueCode));
        var candidates = filtered.ToList();
        var previousSelection = MetadataProblemsDataGrid.SelectedItem as MetadataProblemRow;
        MetadataProblemsDataGrid.ItemsSource = candidates;
        if (previousSelection is not null && candidates.Contains(previousSelection))
            MetadataProblemsDataGrid.SelectedItem = previousSelection;
        var strings = LocalizationManager.Current;
        var summary = candidates.Count == 0
            ? strings.MetadataNoFindings
            : string.Format(CultureInfo.CurrentCulture, strings.MetadataDoctorSummary,
                candidates.Count(candidate => candidate.SeverityValue == LibraryDoctorSeverity.Error),
                candidates.Count(candidate => candidate.SeverityValue == LibraryDoctorSeverity.Warning),
                candidates.Count(candidate => candidate.SeverityValue == LibraryDoctorSeverity.Information));
        MetadataStatusTextBlock.Text = summary;
    }

    private void MetadataSeverityFilter_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_metadataAnalysisLoaded)
            ApplyMetadataDoctorFilter();
    }

    private static string FormatDoctorSeverity(LibraryDoctorSeverity severity) => severity switch
    {
        LibraryDoctorSeverity.Error => LocalizationManager.Current.MetadataSeverityError,
        LibraryDoctorSeverity.Warning => LocalizationManager.Current.MetadataSeverityWarning,
        _ => LocalizationManager.Current.MetadataSeverityInformation
    };

    private static string BuildMetadataIssueSummary(MetadataFolderCandidate candidate)
        => BuildMetadataIssueSummary(candidate.Findings
            .Select<LibraryDoctorFinding, (string Code, int Count)>(
                finding => (finding.Code, finding.AffectedTrackCount)));

    private static string BuildMetadataIssueSummary(IEnumerable<(string Code, int Count)> findings)
    {
        var strings = LocalizationManager.Current;
        var issues = new List<string>();
        foreach (var (code, count) in findings)
        {
            issues.Add(code switch
            {
                "album" => strings.MetadataIssueAlbums,
                "album-artist" => strings.MetadataIssueArtists,
                "title" => strings.MetadataIssueMissingTitles,
                "track-number" => strings.MetadataIssueMissingNumbers,
                "duplicate-track-number" => strings.MetadataIssueDuplicateNumbers,
                "replaygain" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueMissingReplayGain, count),
                "musicbrainz-id" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueMissingMusicBrainzIds, count),
                "incomplete-album" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueIncompleteAlbum, count),
                "album-artwork" => strings.MetadataIssueAlbumArtwork,
                "artist-image" => strings.MetadataIssueArtistImage,
                "missing-file" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueMissingFiles, count),
                "unreadable-file" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueUnreadableFiles, count),
                "likely-duplicate" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueLikelyDuplicates, count),
                "exact-duplicate" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueExactDuplicates, count),
                "alternate-recording" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueAlternateRecordings, count),
                "artist-name-variant" => string.Format(CultureInfo.CurrentCulture, strings.MetadataIssueArtistNameVariants, count),
                _ => code
            });
        }
        return string.Join(" · ", issues);
    }

    private static MetadataProblemRow CreateMetadataProblemRow(
        string source,
        string folder,
        int trackCount,
        LibraryDoctorSeverity severity,
        IEnumerable<(string Code, int Count)> findings,
        MetadataFolderCandidate? candidate)
    {
        var materialized = findings.ToList();
        return new MetadataProblemRow(
            source,
            folder,
            severity,
            FormatDoctorSeverity(severity),
            BuildMetadataIssueSummary(materialized),
            trackCount,
            materialized.Select(finding => finding.Code).ToHashSet(StringComparer.Ordinal),
            candidate);
    }

    private async Task OpenSelectedMetadataRepairAsync()
    {
        if (_metadataRepairRunning)
            return;
        if (MetadataProblemsDataGrid.SelectedItem is not MetadataProblemRow row)
            return;
        if (row.Candidate is null)
            return;
        _metadataRepairRunning = true;
        OpenMetadataRepairButton.IsEnabled = false;
        RefreshMetadataAnalysisButton.IsEnabled = false;
        try
        {
            var dialog = new MetadataRepairDialog(row.Candidate);
            if (await dialog.ShowDialog<bool>(GetHostWindow()) != true || dialog.SelectedMatch is null)
                return;
            // A pending server result must not republish the old local finding after a repair.
            _metadataAnalysisCts?.Cancel();
            while (_metadataAnalysisRunning)
                await Task.Delay(50);
            var overrides = LibraryMetadataRepairService.CreateOverrides(row.Candidate, dialog.SelectedMatch);
            MetadataStatusTextBlock.Text = LocalizationManager.Current.MetadataPhaseSaving;
            await Task.Run(() =>
            {
                using var database = AudioDatabase.OpenDefault();
                database.ApplyTrackMetadataOverrides(overrides);
                TrackSearchIndex.Rebuild(database.GetAll());
            });
            MetadataStatusTextBlock.Text = LocalizationManager.Current.MetadataRepairSuccess;
            LocalLibraryChanged?.Invoke();
            _metadataAnalysisLoaded = false;
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Metadata review correction");
            MetadataStatusTextBlock.Text = LocalizationManager.Current.MetadataAnalysisFailed;
        }
        finally
        {
            _metadataRepairRunning = false;
            RefreshMetadataAnalysisButton.IsEnabled = !_metadataAnalysisRunning;
            OpenMetadataRepairButton.IsEnabled = MetadataProblemsDataGrid.SelectedItem is MetadataProblemRow { Candidate: not null };
        }
        if (!_metadataAnalysisLoaded && !_metadataDeactivated)
            await LoadMetadataProblemsAsync();
    }

    private async void MetadataProblemsDataGrid_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) =>
        await OpenSelectedMetadataRepairAsync();

    private void MetadataProblemsDataGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var row = MetadataProblemsDataGrid.SelectedItem as MetadataProblemRow;
        OpenMetadataRepairButton.IsEnabled = !_metadataRepairRunning && row?.Candidate is not null;
        MetadataSelectionTextBlock.Text = row is null ? LocalizationManager.Current.MetadataSelectHint
            : row.Candidate is null ? LocalizationManager.Current.MetadataRemoteReadOnly
            : LocalizationManager.Current.MetadataActionGuide;
    }

    private async void OpenMetadataRepairButton_OnClick(object? sender, RoutedEventArgs e) =>
        await OpenSelectedMetadataRepairAsync();

    private async void RefreshMetadataAnalysisButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _metadataAnalysisLoaded = false;
        await LoadMetadataProblemsAsync();
    }

    private void CancelMetadataAnalysisButton_OnClick(object? sender, RoutedEventArgs e) =>
        _metadataAnalysisCts?.Cancel();

    /// <summary>Requests a duplicate-resolution review from the host window.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click event data.</param>
    private void ResolveDuplicatesButton_OnClick(object? sender, RoutedEventArgs e)
        => DuplicateResolutionRequested?.Invoke();



    // ------------------------------------------------------------------
    // Ausgabeprofile
    // ------------------------------------------------------------------

    private void OutputProfileComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _outputProfile = OutputProfileComboBox.SelectedItem as OutputProfile;
        RefreshOutputProfileButtons();
    }

    /// <summary>Creates a new output profile via dialog and selects it immediately.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void CreateOutputProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new OutputProfileDialog(
            _outputProfiles.Select(static p => p.Name));
        if (await dialog.ShowDialog<bool>(GetHostWindow()) != true || dialog.Result is null)
            return;
        _outputProfiles.Add(dialog.Result);
        RefreshOutputProfileChoices(dialog.Result);
    }

    /// <summary>Opens the selected output profile for editing.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void ConfigureOutputProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_outputProfile is null)
            return;
        var dialog = new OutputProfileDialog(
            _outputProfiles
                .Where(p => p != _outputProfile)
                .Select(static p => p.Name),
            _outputProfile);
        if (await dialog.ShowDialog<bool>(GetHostWindow()) != true || dialog.Result is null)
            return;
        var index = _outputProfiles.IndexOf(_outputProfile);
        if (index >= 0)
            _outputProfiles[index] = dialog.Result;
        RefreshOutputProfileChoices(dialog.Result);
    }

    /// <summary>Deletes the selected output profile after confirmation.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void DeleteOutputProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_outputProfile is null)
            return;
        var confirmed = await AppMessageBox.ConfirmAsync(
            string.Format(
                LocalizationManager.Current.OutputProfileDeleteConfirm,
                _outputProfile.Name),
            LocalizationManager.Current.OutputProfileDeleteTitle,
            GetHostWindow());
        if (!confirmed)
            return;
        _outputProfiles.Remove(_outputProfile);
        _outputProfile = null;
        RefreshOutputProfileChoices(null);
    }

    /// <summary>Refreshes the profile dropdown and applies the requested selection.</summary>
    /// <param name="selectedProfile">Profile to select, or <see langword="null"/> for no selection.</param>
    private void RefreshOutputProfileChoices(OutputProfile? selectedProfile)
    {
        OutputProfileComboBox.ItemsSource = null;
        OutputProfileComboBox.ItemsSource = _outputProfiles;
        OutputProfileComboBox.SelectedItem = selectedProfile;
        _outputProfile = selectedProfile;
        RefreshOutputProfileButtons();
    }

    /// <summary>Updates the enabled state of the configure and delete buttons and the summary label.</summary>
    private void RefreshOutputProfileButtons()
    {
        var hasSelection = _outputProfile is not null;
        ConfigureOutputProfileButton.IsEnabled = hasSelection;
        DeleteOutputProfileButton.IsEnabled    = hasSelection;
        OutputProfileSummaryTextBlock.Text     = _outputProfile is null
            ? string.Empty
            : BuildOutputSummary(_outputProfile);
    }

    private static string BuildOutputSummary(OutputProfile profile)
    {
        var backend = profile.Backend switch
        {
            OutputBackend.Asio   => LocalizationManager.Current.SteinbergAsio,
            OutputBackend.CwAsio => LocalizationManager.Current.CwAsio,
            _                    => !OperatingSystem.IsWindows()
                ? profile.SelectedWasapiDeviceId?.StartsWith("alsa:", StringComparison.Ordinal) == true
                    ? LocalizationManager.Current.DirectAlsa
                    : LocalizationManager.Current.OpenAl
                : "WASAPI"
        };
        var device = profile.Backend switch
        {
            OutputBackend.Asio or OutputBackend.CwAsio => profile.SelectedDriverName,
            _ => profile.SelectedWasapiDeviceName
        };
        return string.IsNullOrEmpty(device) ? backend : $"{backend}  ·  {device}";
    }

    /// <summary>Programmatically navigates to the settings section identified by <paramref name="tag"/>.</summary>
    internal void NavigateToSection(string tag)
    {
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));
        if (item is not null)
            NavListBox.SelectedItem = item;
    }

    /// <summary>Scrolls the content area to make the equalizer profile selector visible.</summary>
    internal void ScrollToEqualizerSection()
    {
        Dispatcher.UIThread.Post(
            () => EqualizerProfileComboBox.BringIntoView(),
            DispatcherPriority.Loaded);
    }

    /// <summary>Imports an Equalizer APO or AutoEQ text profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void ImportEqualizerProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await GetHostWindow().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Current.EqualizerImportTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationManager.Current.EqualizerProfileFileType)
                {
                    Patterns = ["*.txt", "*.cfg"]
                }
            ]
        });
        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;
        ImportEqualizerProfileButton.IsEnabled = false;
        EqualizerProfileTextBlock.Text = LocalizationManager.Current.EqualizerImporting;
        try
        {
            if (_equalizerProfile is null)
                return;
            var importedProfile = await Task.Run(() => EqualizerApoParser.ParseFile(path));
            _equalizerProfile.PreampDb = importedProfile.PreampDb;
            _equalizerProfile.Filters = importedProfile.Filters
                .Select(static filter => filter.Clone())
                .ToList();
            EqualizerEnabledCheckBox.IsChecked = true;
            RefreshEqualizerProfileText();
            RebuildEqualizerEditor();
            QueueEqualizerPreview();
        }
        catch
        {
            EqualizerProfileTextBlock.Text = LocalizationManager.Current.EqualizerImportFailed;
        }
        finally
        {
            ImportEqualizerProfileButton.IsEnabled = true;
        }
    }

    /// <summary>Refreshes the imported equalizer profile summary.</summary>
    private void RefreshEqualizerProfileText()
    {
        var hasProfile = _equalizerProfile is not null;
        EqualizerEditorPanel.IsVisible = hasProfile;
        DeleteEqualizerProfileButton.IsVisible = hasProfile;
        EqualizerEnabledCheckBox.IsEnabled = hasProfile;
        EqualizerProfileTextBlock.Text = _equalizerProfile is null
            ? string.Empty
            : string.Format(
                LocalizationManager.Current.EqualizerProfileSummary,
                _equalizerProfile.Name,
                _equalizerProfile.PreampDb,
                _equalizerProfile.Filters.Count);
    }

    /// <summary>Switches the editable and active equalizer profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Selection-change event data.</param>
    private void EqualizerProfileComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        _equalizerProfile = EqualizerProfileComboBox.SelectedItem as EqualizerProfile;
        if (_initializing)
            return;
        if (_equalizerProfile is null)
            EqualizerEnabledCheckBox.IsChecked = false;
        RefreshEqualizerProfileText();
        RebuildEqualizerEditor();
        QueueEqualizerPreview();
    }

    /// <summary>Creates and selects a new empty equalizer profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void CreateEqualizerProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new EqualizerProfileNameDialog(
            _equalizerProfiles.Select(static profile => profile.Name));
        if (await dialog.ShowDialog<bool>(GetHostWindow()) != true
            || string.IsNullOrWhiteSpace(dialog.ProfileName))
        {
            return;
        }

        var profile = new EqualizerProfile { Name = dialog.ProfileName };
        _equalizerProfiles.Add(profile);
        RefreshEqualizerProfileChoices(profile);
        EqualizerEnabledCheckBox.IsChecked = true;
        QueueEqualizerPreview();
    }

    /// <summary>Deletes the selected equalizer profile after confirmation.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void DeleteEqualizerProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_equalizerProfile is null)
            return;
        var confirmed = await AppMessageBox.ConfirmAsync(
            string.Format(
                LocalizationManager.Current.EqualizerDeleteConfirm,
                _equalizerProfile.Name),
            LocalizationManager.Current.EqualizerDeleteTitle,
            GetHostWindow());
        if (!confirmed)
            return;

        _equalizerProfiles.Remove(_equalizerProfile);
        _equalizerProfile = null;
        EqualizerEnabledCheckBox.IsChecked = false;
        RefreshEqualizerProfileChoices(null);
        QueueEqualizerPreview();
    }

    /// <summary>Refreshes the profile dropdown and applies the requested selection.</summary>
    /// <param name="selectedProfile">Profile to select, or <see langword="null"/> for no selection.</param>
    private void RefreshEqualizerProfileChoices(EqualizerProfile? selectedProfile)
    {
        EqualizerProfileComboBox.ItemsSource = null;
        EqualizerProfileComboBox.ItemsSource = _equalizerProfiles;
        EqualizerProfileComboBox.SelectedItem = selectedProfile;
        _equalizerProfile = selectedProfile;
        RefreshEqualizerProfileText();
        RebuildEqualizerEditor();
    }

    /// <summary>Rebuilds the dynamic equalizer filter editor from the active profile.</summary>
    private void RebuildEqualizerEditor()
    {
        _rebuildingEqualizerEditor = true;
        try
        {
            EqualizerFiltersPanel.Children.Clear();
            EqualizerPreampNumericUpDown.Value = (decimal)(_equalizerProfile?.PreampDb ?? 0);
            EqualizerPreampNumericUpDown.IsEnabled = _equalizerProfile is not null;
            AddEqualizerFilterButton.IsEnabled =
                (_equalizerProfile?.Filters.Count ?? 0) < MaximumEqualizerFilters;
            EqualizerResponseGraph.SetProfile(_equalizerProfile);
            if (_equalizerProfile is null)
                return;

            for (var index = 0; index < _equalizerProfile.Filters.Count; index++)
                EqualizerFiltersPanel.Children.Add(
                    CreateEqualizerFilterRow(_equalizerProfile.Filters[index], index));
        }
        finally
        {
            _rebuildingEqualizerEditor = false;
        }
    }

    /// <summary>Creates one editable row for a parametric equalizer filter.</summary>
    /// <param name="filter">Filter edited by the row.</param>
    /// <param name="index">Zero-based filter index.</param>
    /// <returns>The configured filter row.</returns>
    private Control CreateEqualizerFilterRow(EqualizerFilter filter, int index)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(28)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(220)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var number = new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(number);

        var typeChoices = CreateEqualizerTypeChoices();
        var typeComboBox = new ComboBox
        {
            ItemsSource = typeChoices,
            SelectedItem = typeChoices.First(choice => choice.Value == filter.Type),
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(typeComboBox, 1);
        row.Children.Add(typeComboBox);

        var frequency = CreateEqualizerNumber(filter.Frequency, 1, 100000, 10, "0");
        Grid.SetColumn(frequency, 2);
        row.Children.Add(frequency);

        var gain = CreateEqualizerNumber(filter.GainDb, -100, 100, 0.5, "0.0");
        gain.IsEnabled = SupportsEqualizerGain(filter.Type);
        Grid.SetColumn(gain, 3);
        row.Children.Add(gain);

        var quality = CreateEqualizerNumber(filter.Q, 0.05, 100, 0.05, "0.00");
        Grid.SetColumn(quality, 4);
        row.Children.Add(quality);

        var remove = CreateStyledButton("×", 28, 28);
        remove.Margin = new Thickness(6, 0, 0, 0);
        ToolTip.SetTip(remove, LocalizationManager.Current.EqualizerRemoveFilter);
        Grid.SetColumn(remove, 5);
        row.Children.Add(remove);

        typeComboBox.SelectionChanged += (_, _) =>
        {
            if (typeComboBox.SelectedItem is not SettingChoice<EqualizerFilterType> choice)
                return;
            filter.Type = choice.Value;
            gain.IsEnabled = SupportsEqualizerGain(filter.Type);
            EqualizerEditorChanged();
        };
        frequency.ValueChanged += (_, args) =>
        {
            if (args.NewValue is decimal value)
            {
                filter.Frequency = (double)value;
                EqualizerEditorChanged();
            }
        };
        gain.ValueChanged += (_, args) =>
        {
            if (args.NewValue is decimal value)
            {
                filter.GainDb = (double)value;
                EqualizerEditorChanged();
            }
        };
        quality.ValueChanged += (_, args) =>
        {
            if (args.NewValue is decimal value)
            {
                filter.Q = (double)value;
                EqualizerEditorChanged();
            }
        };
        remove.Click += (_, _) =>
        {
            if (_equalizerProfile is null)
                return;
            _equalizerProfile.Filters.Remove(filter);
            RebuildEqualizerEditor();
            EqualizerEditorChanged();
        };
        return row;
    }

    /// <summary>Creates a numeric editor used by an equalizer filter row.</summary>
    /// <param name="value">Initial value.</param>
    /// <param name="minimum">Minimum accepted value.</param>
    /// <param name="maximum">Maximum accepted value.</param>
    /// <param name="increment">Spinner increment.</param>
    /// <param name="format">Display format.</param>
    /// <returns>The configured numeric editor.</returns>
    private static NumericUpDown CreateEqualizerNumber(
        double value,
        double minimum,
        double maximum,
        double increment,
        string format) =>
        new()
        {
            Value = (decimal)Math.Clamp(value, minimum, maximum),
            Minimum = (decimal)minimum,
            Maximum = (decimal)maximum,
            Increment = (decimal)increment,
            FormatString = format,
            ClipValueToMinMax = true,
            Height = 28,
            Margin = new Thickness(0, 0, 6, 0)
        };

    /// <summary>Creates localized choices for all supported parametric filter types.</summary>
    /// <returns>The localized filter-type choices.</returns>
    private static SettingChoice<EqualizerFilterType>[] CreateEqualizerTypeChoices() =>
    [
        new(EqualizerFilterType.Peak, LocalizationManager.Current.EqualizerPeak),
        new(EqualizerFilterType.LowShelf, LocalizationManager.Current.EqualizerLowShelf),
        new(EqualizerFilterType.HighShelf, LocalizationManager.Current.EqualizerHighShelf),
        new(EqualizerFilterType.LowPass, LocalizationManager.Current.EqualizerLowPass),
        new(EqualizerFilterType.HighPass, LocalizationManager.Current.EqualizerHighPass)
    ];

    /// <summary>Returns whether a filter type uses a gain value.</summary>
    /// <param name="type">Filter type to inspect.</param>
    /// <returns><see langword="true"/> for peak and shelf filters; otherwise <see langword="false"/>.</returns>
    private static bool SupportsEqualizerGain(EqualizerFilterType type) =>
        type is EqualizerFilterType.Peak
            or EqualizerFilterType.LowShelf
            or EqualizerFilterType.HighShelf;

    /// <summary>Updates the graph, profile summary, and debounced live preview after an edit.</summary>
    private void EqualizerEditorChanged()
    {
        if (_initializing)
            return;
        RefreshEqualizerProfileText();
        EqualizerResponseGraph.SetProfile(_equalizerProfile);
        QueueEqualizerPreview();
    }

    /// <summary>Updates the profile preamplification from the numeric editor.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Value-change event data.</param>
    private void EqualizerPreampNumericUpDown_OnValueChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (_initializing
            || _rebuildingEqualizerEditor
            || _equalizerProfile is null
            || e.NewValue is not decimal value)
            return;
        _equalizerProfile.PreampDb = (double)value;
        EqualizerEditorChanged();
    }

    /// <summary>Adds a new peaking filter to the dynamic equalizer profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private void AddEqualizerFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_equalizerProfile is null)
            return;
        if (_equalizerProfile.Filters.Count >= MaximumEqualizerFilters)
            return;
        _equalizerProfile.Filters.Add(new EqualizerFilter
        {
            Type = EqualizerFilterType.Peak,
            Frequency = 1000,
            GainDb = 0,
            Q = 0.7071067811865476
        });
        EqualizerEnabledCheckBox.IsChecked = true;
        RebuildEqualizerEditor();
        EqualizerEditorChanged();
    }

    private void EqualizerEnabledCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        QueueEqualizerPreview();
    }

    private void QueueEqualizerPreview()
    {
        var previewVersion = Interlocked.Increment(ref _equalizerPreviewVersion);
        var enabled = EqualizerEnabled;
        var profile = _equalizerProfile?.Clone();
        _ = Task.Run(async () =>
        {
            await Task.Delay(75);
            if (previewVersion != Volatile.Read(ref _equalizerPreviewVersion))
                return;
            try
            {
                _onEqualizerPreviewChanged?.Invoke(enabled, profile);
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Equalizer live preview");
            }
        });
    }


    // ------------------------------------------------------------------
    // Bibliothek
    // ------------------------------------------------------------------

    /// <summary>Accepts a picker result and starts its initial scan through the existing row controls.</summary>
    /// <param name="sender">Add-directory button.</param>
    /// <param name="e">Click event.</param>
    private async void AddDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Current.AddMusicDirectory,
            AllowMultiple = false
        });
        if (folders.Count == 0)
            return;
        var path = folders[0].TryGetLocalPath() ?? string.Empty;
        await AddLibraryDirectoryAsync(path);
    }

    /// <summary>Adds a directory and awaits its initial scan without rebuilding active rows.</summary>
    /// <param name="path">Selected local directory path.</param>
    /// <returns>Whether a new directory was admitted, regardless of scan outcome.</returns>
    internal async Task<bool> AddLibraryDirectoryAsync(string path)
    {
        if (_metadataDeactivated || string.IsNullOrWhiteSpace(path) ||
            _libraryPaths.Contains(path, StringComparer.OrdinalIgnoreCase) || _activeScans.ContainsKey(path))
            return false;
        _libraryPaths.Add(path);
        DirectoriesPanel.Children.Add(BuildDirectoryRow(path, out var startScan));
        _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
        await startScan();
        return true;
    }

    private void RebuildDirectoryList()
    {
        DirectoriesPanel.Children.Clear();
        foreach (var path in _libraryPaths)
            DirectoriesPanel.Children.Add(BuildDirectoryRow(path));
    }

    private static async Task RefreshCountAsync(string path, TextBlock countBlock)
    {
        try
        {
            var count = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.CountByDirectory(path);
            });
            countBlock.Text = LocalizationManager.FormatTrackCount(count);
        }
        catch
        {
            countBlock.Text = string.Empty;
        }
    }

    /// <summary>Builds an existing directory row without starting a scan.</summary>
    /// <param name="path">Configured directory.</param>
    /// <returns>The directory controls.</returns>
    private Control BuildDirectoryRow(string path) => BuildDirectoryRow(path, out _);

    /// <summary>Builds row controls and exposes their existing ordinary scan action.</summary>
    /// <param name="path">Configured directory.</param>
    /// <param name="startScan">Initial scan sharing progress, cancellation and completion handling.</param>
    /// <returns>The directory controls.</returns>
    private Control BuildDirectoryRow(string path, out Func<Task> startScan)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pathBlock = new TextBlock
        {
            Text = path,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ToolTip.SetTip(pathBlock, path);
        Grid.SetColumn(pathBlock, 0);

        var countBlock = new TextBlock
        {
            Text = "…",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 10, 0)
        };
        ToolTip.SetTip(countBlock, LocalizationManager.Current.TrackCountTooltip);
        Grid.SetColumn(countBlock, 1);

        var scanBtn = CreateStyledButton(LocalizationManager.Current.Scan, 80, 26, new Thickness(0, 0, 4, 0));
        Grid.SetColumn(scanBtn, 2);

        var refreshMetadataBtn = CreateStyledButton(
            LocalizationManager.Current.RefreshAllMetadata,
            150,
            26,
            new Thickness(0, 0, 4, 0));
        ToolTip.SetTip(refreshMetadataBtn, LocalizationManager.Current.RefreshAllMetadataHint);
        Grid.SetColumn(refreshMetadataBtn, 3);

        var removeBtn = CreateStyledButton("×", 26, 26);
        removeBtn.FontSize = 14;
        ToolTip.SetTip(removeBtn, LocalizationManager.Current.RemoveDirectory);
        Grid.SetColumn(removeBtn, 4);

        grid.Children.Add(pathBlock);
        grid.Children.Add(countBlock);
        grid.Children.Add(scanBtn);
        grid.Children.Add(refreshMetadataBtn);
        grid.Children.Add(removeBtn);

        _ = RefreshCountAsync(path, countBlock);

        var statusBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            IsVisible = false
        };

        async Task RunScanAsync(bool forceMetadataRefresh, Button activeButton, Button otherButton)
        {
            if (_activeScans.ContainsKey(path))
            {
                _activeScans[path].Cancel();
                return;
            }
            if (!Directory.Exists(path))
            {
                statusBlock.IsVisible = true;
                statusBlock.Text = LocalizationManager.Current.FolderNotFound;
                return;
            }
            var cts = new CancellationTokenSource();
            _activeScans[path] = cts;
            UpdateBackupButtonAvailability();
            activeButton.Content = LocalizationManager.Current.Cancel;
            otherButton.IsEnabled = false;
            statusBlock.IsVisible = true;
            statusBlock.Text = LocalizationManager.Current.ScanRunning;
            var progress = new Progress<ScanProgress>(p =>
                statusBlock.Text = $"{p.Current}/{p.Total} – {Path.GetFileName(p.CurrentFile)}");
            try
            {
                var result = forceMetadataRefresh
                    ? await LibraryScanner.RefreshMetadataAsync(
                        path,
                        progress,
                        CalculateMissingReplayGainDuringScan,
                        cts.Token)
                    : await DirectoryScanner(
                        path,
                        progress,
                        CalculateMissingReplayGainDuringScan,
                        cts.Token);
                var failed = result.Failed > 0
                    ? $" · {string.Format(LocalizationManager.Current.ScanFailed, result.Failed)}"
                    : string.Empty;
                statusBlock.Text = string.Format(
                    LocalizationManager.Current.ScanCompleted,
                    result.Total, result.Added, result.Updated, result.Removed, failed);
                if (result.Added > 0 || result.Updated > 0 || result.Removed > 0)
                    LocalLibraryChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
                statusBlock.Text = LocalizationManager.Current.ScanCanceled;
            }
            catch (Exception ex)
            {
                statusBlock.Text = string.Format(LocalizationManager.Current.ScanFailed, ex.Message);
            }
            finally
            {
                _activeScans.Remove(path);
                cts.Dispose();
                activeButton.Content = forceMetadataRefresh
                    ? LocalizationManager.Current.RefreshAllMetadata
                    : LocalizationManager.Current.Scan;
                otherButton.IsEnabled = true;
                UpdateBackupButtonAvailability();
                _ = RefreshCountAsync(path, countBlock);
            }
        }

        startScan = () => RunScanAsync(forceMetadataRefresh: false, scanBtn, refreshMetadataBtn);

        scanBtn.Click += async (_, _) =>
            await RunScanAsync(forceMetadataRefresh: false, scanBtn, refreshMetadataBtn);
        refreshMetadataBtn.Click += async (_, _) =>
            await RunScanAsync(forceMetadataRefresh: true, refreshMetadataBtn, scanBtn);

        removeBtn.Click += (_, _) =>
        {
            if (_activeScans.TryGetValue(path, out var cts))
            {
                cts.Cancel();
            }
            _libraryPaths.Remove(path);
            DirectoriesPanel.Children.Remove(outer);
            _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
        };

        outer.Children.Add(grid);
        outer.Children.Add(statusBlock);
        return outer;
    }

    private async void OptimizeDatabaseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OptimizeDatabaseButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.DatabaseOptimizing;
        try
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.Optimize();
            });
            DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.DatabaseOptimized;
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(LocalizationManager.Current.DatabaseOptimizeFailed, ex.Message);
        }
        finally
        {
            OptimizeDatabaseButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    /// <summary>Deletes every rendered Genre Cloud mosaic from the current user's cache.</summary>
    /// <param name="sender">The clear-cache button.</param>
    /// <param name="e">The routed click event.</param>
    private void ClearGenreCloudCacheButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var cacheDirectory = AppPaths.GetDataPath("genre-cloud-backgrounds");
        if (Directory.Exists(cacheDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(cacheDirectory))
            {
                try { File.Delete(file); }
                catch { /* Cache cleanup is best-effort. */ }
            }
        }

        GenreCloudCacheStatusTextBlock.Text = LocalizationManager.Current.GenreCloudCacheCleared;
    }

    /// <summary>Updates the visible percentage beside the Genre Cloud opacity slider.</summary>
    /// <param name="sender">The opacity slider.</param>
    /// <param name="e">The slider value-change event.</param>
    private void GenreCloudVisibilitySlider_OnValueChanged(
        object? sender,
        RangeBaseValueChangedEventArgs e)
        => UpdateGenreCloudVisibilityText();

    /// <summary>Enables the opacity control only when background rendering is active.</summary>
    /// <param name="sender">The background-mode selector.</param>
    /// <param name="e">The selection-change event.</param>
    private void GenreCloudBackgroundComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
        => UpdateGenreCloudVisibilityEnabled();

    /// <summary>Formats the current Genre Cloud opacity as an integer percentage.</summary>
    private void UpdateGenreCloudVisibilityText()
        => GenreCloudVisibilityValueTextBlock.Text = $"{GenreCloudVisibilitySlider.Value:0}%";

    /// <summary>Synchronizes the opacity controls with the selected background mode.</summary>
    private void UpdateGenreCloudVisibilityEnabled()
    {
        var enabled = SelectedGenreCloudBackground != GenreCloudBackgroundMode.None;
        GenreCloudVisibilitySlider.IsEnabled = enabled;
        GenreCloudVisibilityValueTextBlock.Opacity = enabled ? 1 : 0.45;
    }

    private async void RepairAlbumArtworkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RepairAlbumArtworkButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.AlbumArtworkRepairing;
        var progress = new Progress<ScanProgress>(p =>
            DatabaseMaintenanceStatusTextBlock.Text =
                $"{LocalizationManager.Current.AlbumArtworkRepairing} {p.Current}/{p.Total} – {Path.GetFileName(p.CurrentFile)}");
        try
        {
            var repaired = await LibraryScanner.RepairMissingAlbumArtworkAsync(progress);
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.AlbumArtworkRepaired, repaired);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(LocalizationManager.Current.AlbumArtworkRepairFailed, ex.Message);
        }
        finally
        {
            RepairAlbumArtworkButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void NormalizeArtistsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        NormalizeArtistsButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.ArtistsNormalizing;
        try
        {
            var result = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var normalization = db.NormalizeArtists();
                TrackSearchIndex.Rebuild(db.GetAll().ToList());
                return normalization;
            });
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.ArtistsNormalized,
                result.MergedArtists,
                result.UpdatedTracks);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.ArtistNormalizationFailed, ex.Message);
        }
        finally
        {
            NormalizeArtistsButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void DownloadMissingArtworkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DownloadMissingArtworkButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.MissingArtworkDownloading;
        var progress = new Progress<ScanProgress>(p =>
            DatabaseMaintenanceStatusTextBlock.Text =
                $"{LocalizationManager.Current.MissingArtworkDownloading} {p.Current}/{p.Total}");
        try
        {
            var downloaded = await LibraryScanner.DownloadMissingAlbumArtworkAsync(progress);
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.MissingArtworkDownloaded, downloaded);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.MissingArtworkDownloadFailed, ex.Message);
        }
        finally
        {
            DownloadMissingArtworkButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }


    /// <summary>
    /// Runs the optional acoustic descriptor and musical-key analysis for the
    /// complete local library. The operation
    /// is cancellable, never modifies source media, and leaves failed sources on
    /// their normal retry cooldown.
    /// </summary>
    /// <param name="sender">The analyze action.</param>
    /// <param name="e">Click details.</param>
    private async void AnalyzeAudioFeaturesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _audioFeatureAnalysisCts?.Cancel();
        _audioFeatureAnalysisCts?.Dispose();
        var cancellation = new CancellationTokenSource();
        _audioFeatureAnalysisCts = cancellation;
        AnalyzeAudioFeaturesButton.IsEnabled = false;
        var stored = 0;
        var failed = 0;
        AudioFeatureAnalysisStatusTextBlock.Text =
            string.Format(LocalizationManager.Current.AudioFeatureAnalyzing, 0);
        try
        {
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var result = await AudioFeatureMaintenanceService.AnalyzeMissingAsync(
                    AudioDatabase.OpenDefault,
                    maximumTracks: 25,
                    delay: TimeSpan.FromMilliseconds(250),
                    cancellationToken: cancellation.Token);
                stored += result.Stored;
                failed += result.Failed;
                AudioFeatureAnalysisStatusTextBlock.Text =
                    string.Format(LocalizationManager.Current.AudioFeatureAnalyzing, stored);
                if (result.Examined == 0)
                    break;
            }

            AudioFeatureAnalysisStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.AudioFeatureAnalysisDone, stored, failed);
        }
        catch (OperationCanceledException)
        {
            AudioFeatureAnalysisStatusTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            AudioFeatureAnalysisStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.AudioFeatureAnalysisFailed, ex.Message);
        }
        finally
        {
            AnalyzeAudioFeaturesButton.IsEnabled = true;
            if (ReferenceEquals(_audioFeatureAnalysisCts, cancellation))
                _audioFeatureAnalysisCts = null;
            cancellation.Dispose();
        }
    }

    private async void CalculateReplayGainButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _replayGainCalculationCts?.Cancel();
        _replayGainCalculationCts?.Dispose();
        var cancellation = new CancellationTokenSource();
        _replayGainCalculationCts = cancellation;
        CalculateReplayGainButton.IsEnabled = false;
        ReplayGainCalculationStatusTextBlock.Text = LocalizationManager.Current.ReplayGainCalculating;
        try
        {
            var progress = new Progress<ScanProgress>(p =>
                ReplayGainCalculationStatusTextBlock.Text =
                    $"{LocalizationManager.Current.ReplayGainCalculating} {p.Current}/{p.Total} – {Path.GetFileName(p.CurrentFile)}");
            var updated = await LibraryScanner.CalculateMissingReplayGainAsync(
                progress,
                cancellation.Token);
            ReplayGainCalculationStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.ReplayGainCalculated, updated);
        }
        catch (OperationCanceledException)
        {
            ReplayGainCalculationStatusTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            ReplayGainCalculationStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.ReplayGainCalculationFailed, ex.Message);
        }
        finally
        {
            CalculateReplayGainButton.IsEnabled = true;
            if (ReferenceEquals(_replayGainCalculationCts, cancellation))
                _replayGainCalculationCts = null;
            cancellation.Dispose();
        }
    }

    private async void ExportLibraryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanStartLibraryBackupOperation())
            return;
        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Current.ExportLibrary,
            FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
            DefaultExtension = "zip",
            SuggestedFileName = $"HyperFidel-library-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        });
        if (file is null)
            return;
        var filePath = file.TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(filePath))
            return;

        SetLibraryOperationControlsEnabled(false);
        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryExporting;
        LibraryExportProgressBar.Value = 0;
        LibraryExportProgressBar.IsVisible = true;
        var progress = new Progress<LibraryExportProgress>(value =>
        {
            LibraryExportProgressBar.Value = value.Percentage;
            LibraryBackupStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.LibraryExportProgress,
                value.Percentage, value.CurrentFile ?? string.Empty);
        });
        try
        {
            await LibraryBackupService.ExportAsync(filePath, _libraryPaths, progress);
            LibraryExportProgressBar.Value = 100;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryExported, filePath);
        }
        catch (Exception ex)
        {
            LibraryExportProgressBar.IsVisible = false;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryExportFailed, ex.Message);
        }
        finally
        {
            SetLibraryOperationControlsEnabled(true);
        }
    }

    private async void ImportLibraryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanStartLibraryBackupOperation())
            return;
        var topLevel = TopLevel.GetTopLevel(this)!;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Current.ImportLibrary,
            FileTypeFilter = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
            AllowMultiple = false
        });
        if (files.Count == 0)
            return;
        var filePath = files[0].TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(filePath))
            return;

        var confirmed = await AppMessageBox.ConfirmAsync(
            LocalizationManager.Current.LibraryImportConfirm,
            LocalizationManager.Current.ImportLibrary,
            GetHostWindow());
        if (!confirmed)
            return;

        SetLibraryOperationControlsEnabled(false);
        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryImporting;
        LibraryExportProgressBar.Value = 0;
        LibraryExportProgressBar.IsVisible = true;
        var progress = new Progress<LibraryImportProgress>(value =>
        {
            LibraryExportProgressBar.Value = value.Percentage;
            LibraryBackupStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.LibraryImportProgress,
                value.Percentage, value.CurrentFile ?? string.Empty);
        });
        try
        {
            if (GetHostWindow() is MainWindow mainWindow)
                mainWindow.PrepareForLibraryImport();

            var importedPaths = await LibraryBackupService.ImportAsync(filePath, progress);
            LibraryExportProgressBar.Value = 100;
            _libraryPaths.Clear();
            _libraryPaths.AddRange(importedPaths);
            _settings.LibraryPaths = _libraryPaths.ToList();
            _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
            RebuildDirectoryList();
            LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryImported;

            await AppMessageBox.ShowAsync(
                LocalizationManager.Current.LibraryImported,
                LocalizationManager.Current.ImportLibrary,
                GetHostWindow());
            if (AvaloniaApp.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow is MainWindow playbackWindow)
                    await playbackWindow.PreparePlaybackShutdownAsync();
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            LibraryExportProgressBar.IsVisible = false;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryImportFailed, ex.Message);
            SetLibraryOperationControlsEnabled(true);
        }
    }

    private bool CanStartLibraryBackupOperation()
    {
        var maintenanceActive =
            !OptimizeDatabaseButton.IsEnabled ||
            !RepairAlbumArtworkButton.IsEnabled ||
            !NormalizeArtistsButton.IsEnabled ||
            !DownloadMissingArtworkButton.IsEnabled;
        if (_activeScans.Count == 0 && !maintenanceActive)
            return true;
        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryOperationScanActive;
        return false;
    }

    private void SetBackupButtonsEnabled(bool enabled)
    {
        ExportLibraryButton.IsEnabled = enabled;
        ImportLibraryButton.IsEnabled = enabled;
    }

    private void UpdateBackupButtonAvailability()
    {
        SetBackupButtonsEnabled(
            _activeScans.Count == 0 &&
            OptimizeDatabaseButton.IsEnabled &&
            RepairAlbumArtworkButton.IsEnabled &&
            NormalizeArtistsButton.IsEnabled &&
            DownloadMissingArtworkButton.IsEnabled);
    }

    private void SetLibraryOperationControlsEnabled(bool enabled)
    {
        AddDirectoryButton.IsEnabled = enabled;
        OptimizeDatabaseButton.IsEnabled = enabled;
        RepairAlbumArtworkButton.IsEnabled = enabled;
        NormalizeArtistsButton.IsEnabled = enabled;
        DownloadMissingArtworkButton.IsEnabled = enabled;
        SetBackupButtonsEnabled(enabled);
        DirectoriesPanel.IsEnabled = enabled;
    }

    // ------------------------------------------------------------------
    // Dialog
    // ------------------------------------------------------------------

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _equalizerPreviewVersion);
        _settingsAccepted = true;
        CompletionRequested?.Invoke(this, true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) =>
        CompletionRequested?.Invoke(this, false);


}

/// <summary>
/// One metadata-review finding shown in the Settings analysis grid. It lives outside
/// the view so XAML can name it in an <c>x:DataType</c> directive, which compiled
/// bindings require (a nested private type cannot be referenced from XAML).
/// </summary>
/// <param name="Source">Display name of the library source.</param>
/// <param name="Folder">Physical folder the finding belongs to.</param>
/// <param name="SeverityValue">Severity used for sorting.</param>
/// <param name="Severity">Localized severity label.</param>
/// <param name="Issues">Localized list of detected issues.</param>
/// <param name="TrackCount">Number of tracks in the folder.</param>
/// <param name="FindingCodes">Stable finding codes used by the filters.</param>
/// <param name="Candidate">Album candidate offered for the folder, when available.</param>
internal sealed record MetadataProblemRow(
    string Source,
    string Folder,
    LibraryDoctorSeverity SeverityValue,
    string Severity,
    string Issues,
    int TrackCount,
    IReadOnlySet<string> FindingCodes,
    MetadataFolderCandidate? Candidate);
