# SDD ledger — plan: docs/superpowers/plans/2026-11-16-local-offline-player.md

Ruling: work on branch local-offline-player in-place (not a separate worktree) — clean fresh clone, user expects changes in this dir — cost: no physical isolation from main.

## Task 1: Offline local lyrics loader (Core) — DONE
- RED: LocalLyricsLoaderTests failed to compile (TryLoadLocalLyrics missing).
- GREEN: added LyricsService.TryLoadLocalLyrics + DecodeLyricsBytes
  (BOM sniff -> strict UTF-8 -> GB18030 fallback), CodePages provider
  registered in static ctor. 6/6 targeted + 547/547 full Core tests pass.
- Kept network methods for now; removed in Task 3/6.

## Task 2: Never write PCM system/hardware volume — DONE
- RED: NoSystemVolumeWritesTests 3/3 failed (sync enabled for alsa,
  EnsureUnity in PCM open, set-sink-volume in compat layer).
- GREEN: Linux sync rewritten inert (no pactl/subscribe, SupportsEndpointVolume
  always false); AlsaHardwareVolume.EnsureUnity removed (SetVolume kept for
  DSD/DoP); WasapiAudioPlayer PCM open no longer forces mixer; MainWindow slider
  + ApplySystemVolume are software-gain only (MPRIS echo retained).
- 3/3 targeted pass; desktop project builds; full HyperFidel.Tests 402/403.
- KNOWN PRE-EXISTING FAIL (confirmed at HEAD with changes stashed):
  YearInReviewPdfExporterTests.TryWrite_WritesAPdfDocument — SkiaSharp PDF
  is 901 bytes on this machine. Unrelated to this work.

## Task 3: Rewire lyrics to offline local source (desktop) — DONE
- RED: OfflineLyricsWiringTests 2/2 failed (no sidecar use, online download path).
- GREEN: LocalNowPlayingMetadataProvider.GetCachedLyricsAsync now falls back to
  LyricsService.TryLoadLocalLyrics sidecar when no embedded lyrics; embedded
  tag lyrics still win. LoadLyricsForTrackAsync is now offline-only (no
  expiry/download/failed state). 2/2 targeted pass, desktop builds.
- NOTE: LyricsService.DownloadAsync/LRCLIB + remote provider still present;
  removed in Phase 2 (network purge).

## Phase 2 — Stage A: delete HyperFidel.Server + Tests — DONE
- Removed both projects via `dotnet sln remove`, deleted dirs. Solution now 7 projects.
- Desktop build succeeds. (A dependency-closure analysis subagent is running to
  plan the safe-delete set for the deeply woven network code.)

## Phase 2 — Dependency-closure analysis (subagent) findings
- Core builds clean WITHOUT Core/{Streaming,Web,Updates,Scrobbling}; client does NOT.
- Deleting all network folders at once => 534 compile errors in HyperFidel alone
  (~50 files). Dominant missing types: HyperFidelServerSettings (284 refs),
  PlexServerSettings (36), HyperFidelTrackLiteInfo (16), HyperFidelServerClient (16).
- SAFE WHOLESALE (leaf): QobuzStreamingProvider, AirPlay2NativeSession,
  CoverSearchDiagnostics, AI leaf files + Mcp (with their tests), Windows
  streaming credential shim.
- HIGHEST RISK: unified catalog/provider stack (ILibraryCatalogProvider,
  ContentRow, LibraryCatalog*, LibraryViewCache) + MainWindow playback wiring.
  Strategy: compile-driven staged removal; verify build+tests after each slice;
  keep inert local-only skeleton/types where needed to compile (user approved).

## Phase 2 — Slice B1: remove LRCLIB from Core + dead DownloadLyrics — DONE
- LyricsService.cs: removed network usings, HttpClient, JsonSerializerOptions,
  DownloadAsync x2, SearchAsync, Lrclib* records, CreateClient, NullIfWhiteSpace.
  Kept TryLoadLocalLyrics + DecodeLyricsBytes + ParseLrc. Type is now 100% offline.
- NowPlayingMetadataProviders.cs: removed INowPlayingMetadataProvider.DownloadLyricsAsync
  and both provider implementations (dead after Task 3).
- Verified: Core build 0 err; desktop build 0 err; Core lyrics tests 16/16.

## Ruling: direction change to "clean local player" (2026-11-16)
- User explicitly chose "重构为干净本地播放器": physically delete ALL networking
  code/UI, not runtime-disable. This supersedes the plan's dormant-skeleton
  approach. Spec's goal (zero network I/O, no credentials, no server UI) still binds.
- Ruling: abandoned the "delete whole client network chain at once" attempt —
  it produced 608 compile errors because MainWindow.RemoteHyperFidel.cs (850 lines)
  holds only 1 network call but 849 lines of local playback/catalog/favorite helpers
  (GetServerSourceKey x32, IsHyperFidelFavorite x30, CreateHyperFidelCatalogProvider x26).
  Reverted to clean baseline 0449a43 (0 errors). Cost: none (reverted).
- Ruling: proceed domain-by-domain, deleting ONLY true network leaf modules first
  (AI Chat, then MCP, Scrobbling, Radio/Podcast partials, browser audio, remote web,
  server dialogs, and Core network dirs). For each, keep local-only shared helpers
  by moving them out of the deleted partial into a retained partial. Verify build
  and commit after each domain. Cost: more commits, but every commit compiles.

## Task: AI Chat removal — DONE
- Deleted HyperFidel/AI/ (10 files) + AiToolDefinitionsTests.cs.
- Removed AI refs from: MainWindow.xaml/.xaml.cs, MainWindow.Settings.cs,
  MainWindow.Sidebar.cs, MainWindow.ContentLoading.cs, MainWindow.Plex.cs,
  SettingsView.axaml/.axaml.cs, AppSettings.cs, SettingsStore.cs,
  ApplicationCredentialStore.cs.
- Mcp untouched (verified it does not depend on AI).
- Verified: desktop build 0 err/0 AXN warn; HyperFidel.Tests build 0 err.
- Deferred: AI localization strings in Strings.cs + LocalizationManager.cs (7 langs)
  to be purged in the final localization pass.

## Task: Last.fm Scrobbling removal — DONE
- Deleted HyperFidel/Scrobbling/ (2 files) and HyperFidel.Core/Scrobbling/ (4 files).
- Deleted tests: PendingScrobbleStoreTests, ScrobbleRulesTests,
  LastFmSignatureTests, LastFmClientTests.
- Removed _lastFmScrobbler field (MainWindow.xaml.cs), Configure/Flush
  (MainWindow.Startup.cs), Configure in Settings save, authorization callbacks
  passed to SettingsView, SetNowPlaying + BuildLastFmTrack + Complete in
  MainWindow.PlaybackState.cs, and SetTrackLoved in MainWindow.Favorites.cs.
- AppSettings LastFm*/FanartTvApiKey properties KEPT (shared with
  ArtistProfileService, handled in the online-metadata slice).
- SettingsView Last.fm UI + callbacks KEPT but now null-safe (callbacks not
  passed) — to be removed in the online-metadata slice.
- Verified: desktop build 0 err/0 AXN warn; HyperFidel.Tests 0 err;
  Core.Tests 0 err. CHANGELOG Unreleased/Removed updated.

## Task: Desktop update flow removal — DONE
- Deleted HyperFidel/DesktopUpdatePlatform.cs.
- AboutWindow: rewritten to version/build/links/licenses only; removed update
  buttons/status and servers ctor param. AboutWindow.axaml update StackPanel removed.
- App.xaml.cs: removed startup update check method + call + unused usings
  (Streaming, Updates, Reflection; kept InteropServices for DllImport).
- SettingsView.axaml: removed CheckForUpdatesOnStartup checkbox/Updates heading.
- SettingsView.axaml.cs: removed load line, CheckForUpdatesOnStartup property,
  per-server Update button + column + UpdateHyperFidelServerButton_OnClick handler,
  unused usings (Updates, Reflection).
- AppSettings.CheckForUpdatesOnStartup removed; MainWindow.Settings save line removed.
- MainWindow.AppShell AboutWindow ctor now parameterless.
- KEPT HyperFidel.Core/Updates/ReleaseUpdateService.cs for now: the server client's
  UploadUpdateAsync(VerifiedReleaseManifest) still needs it until the server slice.
- Localization strings (CheckForUpdates/Updates/etc.) deferred to final pass.
- Verified: desktop 0 err/0 AXN warn; HyperFidel.Tests 0 err; Core.Tests 0 err.

## Task: Web browsing (SearXNG) removal — DONE
- Deleted HyperFidel.Core/Web/ (4 files) + Core.Tests PrivateNetworkPolicyTests,
  HtmlContentExtractorTests.
- Removed 3 MCP web tools (search_web/fetch_page/fetch_page_as_markdown) from
  McpTools.cs and the WebBrowsing property from McpPlayerBridge.
- MainWindow.xaml.cs: removed _webBrowsing field/lock, startup construction,
  AppendWebBrowsingLog.
- SettingsView: removed web panel XAML, 3 tool checkboxes, load lines,
  WebBrowsingValue property and tool-checklist entries; removed using.
- MainWindow.Settings.cs: removed WebBrowsing save lines.
- AppSettings.WebBrowsing + using removed.
- Localization strings deferred to final pass.
- Verified: desktop 0 err/0 AXN; Tests 0 err; Core.Tests 0 err.
- NOTE: scripts/verify-mcp-tool-parity.ps1 is now stale (references deleted
  HyperFidel/AI/); will be removed with the MCP slice.

## Task: MCP/MobileRemote/BrowserAudio removal — IN PROGRESS
- Goal: delete HyperFidel/Mcp/, HyperFidel/Remote/, HyperFidel/Audio/BrowserAudio*,
  MainWindow.MobileRemote*.cs atomically (mutually coupled). Keep local
  release-output-device feature (_releasedOutput* fields) and the local
  ResolveRemoteMcpTrack* helpers used by Queue.cs/Playback.cs.
- Radio/Podcast/Plex/CrossDeviceResume/MusicBrainz/AirPlay kept for later slices
  (verified they do not reference this chain).

## Task: MCP/MobileRemote/BrowserAudio removal — DONE
- Deleted HyperFidel/Mcp/, HyperFidel/Remote/, HyperFidel/Audio/BrowserAudio*.cs,
  MainWindow.MobileRemote*.cs, SettingsView.MobileRemote.cs.
- MainWindow.xaml.cs: removed 3 bridge fields, startup server starts, InitMcpBridge(),
  OnClosed server stops; kept local ResolveRemoteMcpTrack* helpers (used by Queue/
  Playback) and updated their XML doc to say persisted playlist/queue reference.
- MainWindow.Settings.cs: removed browser output state from SelectOutputProfileAsync
  and UpdateOutputDeviceLockButton; kept a browser-free RefreshOutputPickerSelection
  that populates from saved profiles; removed MCP/mobile change detection, save
  lines and server start/stop blocks. Local release-output-device preserved.
- MainWindow.Playback.cs / PlaybackState.cs / Transport.cs: removed browser player
  branches, format display and _browserOutputResume resets.
- AppSettings: removed MCP/mobile props; SettingsStore + ApplicationCredentialStore:
  removed MobileRemoteAccessToken overlay/snapshot/legacy import.
- SettingsView.axaml(.cs): removed Integration nav group, MCP + MobileRemote panels,
  token generators, status badges, tool checkbox parity list, properties/load/save.
- csproj: removed QRCoder + both ModelContextProtocol packages, embedded
  MobileRemote.* resources, BrowserAudioSmoke InternalsVisibleTo. Zeroconf kept
  (AirPlay discovery, later slice).
- Deleted dead scripts/BrowserAudioSmoke, scripts/RemoteSmoke, verify-mcp-tool-parity;
  removed their CI/verify-all/build.ps1 refs. Removed stale settings keys from
  prepare-first-playback-check.ps1.
- AboutWindow.axaml removed QRCoder + MCP SDK license lines; THIRD_PARTY_NOTICES
  removed QRCoder + MCP SDK; deleted licenses/QRCoder-MIT.txt and orphaned
  Localization/MobileRemote.json; StatusBadge doc updated.
- Deleted chain tests (7) + Ui.Smoke BrowserOutputReclaimWorkflows (and its Program.cs
  call).
- KEPT Radio/Podcast/Plex/CrossDeviceResume/MusicBrainz/AirPlay for later slices.
- CHANGELOG Unreleased/Removed updated to describe the fully local-only player.
- Verified: desktop 0 err/0 AXN; Tests 0 err (only known pre-existing
  YearInReviewPdfExporter failure); Core.Tests 0 err; Ui.Smoke 0 err.

## Task: FFmpeg GitHub download removal — DONE
- Ruling: this code-removal plan is verified by build + surviving tests + static
  grep, not per-task TDD RED→GREEN (there is no new behavior to pin); ledgered
  per executing-plans.
- FfmpegLocator.cs: removed all HttpClient/api.github.com download/extract code
  (WindowsReleaseApiUrl, MacReleaseApiUrl, Download*, Resolve*Url*, TryReadAsset,
  ExtractBinaryEntry) and the `progress`/download branch. EnsureAvailableAsync()
  now only checks local dirs/PATH and prepends; kept mac package-dir probing.
- App.xaml.cs: simplified startup call to EnsureAvailableAsync() with no progress
  callback; still shows offline-install message when absent (string reworded in
  final localization pass).
- Verified: desktop 0 err/0 AXN; no HttpClient/URL left in FfmpegLocator.

## Task: WebDAV backup upload removal — DONE
- Ruling: same build+tests+static-grep gate (no TDD RED/GREEN for removals).
- Deleted Core: BackupUploader.cs, BackupTargets.cs + BackupTargetsTests.cs,
  BackupUploaderTests.cs.
- MainWindow.ScheduledBackup.cs: removed _backupUploader field, upload call after
  local export, and the whole TryUploadBackupAsync method. Local scheduled
  backup (export + retention pruning + status) preserved.
- AppSettings.cs: removed BackupTarget property and BackupTargetSettings class.
- ApplicationCredentialStore.cs: removed BackupTargetPassword.
- SettingsStore.cs: removed apply/capture/legacy-import of BackupTarget password.
- MainWindow.Settings.cs: removed 6 BackupTarget save lines.
- SettingsView.axaml(.cs): removed the WebDAV panel (7 controls + hint), load
  block, and 5 value properties.
- Strings.cs + LocalizationManager.cs: removed 9 BackupTarget/BackupUpload
  string props, resource-map keys, and all 7 language values.
- Verified: desktop 0 err/0 AXN; Core tests build 0 err; Core tests 467 passed;
  desktop tests 380 passed / 1 known pre-existing PDF fail; zero residual
  BackupTarget/BackupUpload refs. Note: verify-localization.cjs is stale
  (references deleted MobileRemote.json) — final-pass cleanup.
