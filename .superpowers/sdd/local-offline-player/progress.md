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

## Task: Cross-device resume removal — DONE
- Deleted MainWindow.CrossDeviceResume.cs, Core/Library/CrossDeviceResume.cs,
  Core.Tests/CrossDeviceResumeTests.cs.
- MainWindow.Playback.cs: removed OfferRemoteResumeAsync call + remoteResumeRow
  pattern, and ClearRemoteResumePrompt in GaplessPlayer_OnTrackChanged.
- MainWindow.PlaybackState.cs / Transport.cs: removed ClearRemoteResumePrompt /
  PublishRemoteTrackPosition calls.
- MainWindow.axaml: removed ResumePositionButton transport control.
- Strings.cs + LocalizationManager.cs: removed 4 ResumeOnThisDevice props,
  resource keys and all 7 language values (28 lines).
- Verified: desktop 0 err/0 AXN; Core tests build 0 err; zero residual refs.

## Task: MusicBrainz rating/enrichment removal — DONE
- Extracted retained offline helper MusicBrainzGenreMetadata into its own Core
  file (Combine/Serialize only, no network) before deleting the service. It is
  still used by AudioDatabase, TrackSearchIndex and local rating tests.
- Deleted HyperFidel.Core/Library/MusicBrainzRatingService.cs (network client),
  MusicBrainzTrackRating record and the local helper duplicate.
- Deleted MainWindow.MusicBrainz.cs (background enrichment worker) and
  MainWindow.RatingColumns.cs (interactive rating column).
- Deleted MusicBrainzRatingGrouping.cs + its test (only used by the removed
  album-detail refresh path).
- MainWindow.AlbumDetail.cs: removed the entire cancellable rating refresh,
  lookup/grouping/persist methods and their 3 call sites.
- MainWindow.Playback.cs: removed StartMusicBrainzBackgroundEnrichment call.
- MainWindow.xaml.cs: removed MusicBrainzRatingHttpClient, cache lifetime,
  _albumMusicBrainzRatingCts/_musicBrainzBackgroundCts/started/foreground fields
  and the OnClosed cancel.
- ContentRow.cs: removed transient loading/failure/display members; kept the
  dormant local data props (rating, votes, MBID, fetched-at) used by Core,
  TrackRecord, search, similarity, ViewPreviews and queue.
- TableRendering.cs: removed the track-info rating entry and the hidden rating
  column.
- TrackRatingTests.cs: removed the 3 network-service tests and JsonHandler;
  kept the 2 dormant local-persistence tests.
- Strings.cs + LocalizationManager.cs: removed 5 rating strings x 7 languages
  (35 assignments) and their schema properties. No JSON resource files exist.
- Verified: desktop 0 CS/0 AXN; Core tests 0; desktop tests 0 build errors;
  Core tests 443 passed; desktop 376 passed / 1 known pre-existing
  YearInReviewPdfExporter failure; zero residual refs to removed symbols.

## Task: Radio + Podcast removal — DONE
- Deleted desktop partials MainWindow.Radio.cs / MainWindow.Podcasts.cs, view
  models (RadioStationViewModel, PodcastViewModel, PodcastEpisodeViewModel),
  PodcastDownloadService.cs.
- Deleted Core services: RadioBrowserService, RadioStreamMetadataService,
  PodcastService, PodcastDownloadCache, CatalogFilterCache, plus records
  RadioStationRecord / PodcastRecord, and PodcastDownloadCacheTests.
- AudioDatabase: removed radio_stations/podcasts/podcast_episode_progress schema
  and all related CRUD.
- MainWindow.axaml: removed InternetRadioNavItem, PodcastsNavItem, OwnRadios /
  MyPodcasts headers, InternetRadioView, PodcastView, PodcastEpisodesView and
  PodcastInfoView. SettingsView.axaml: removed podcast download limit and all
  radio/podcast visibility checkboxes plus loudness normalization section.
- Playback core (MainWindow.Playback.cs): removed radioStation/podcastPlayback
  parameters from the whole StartReadyPlaybackAsync->...->PrepareCurrent...
  chain, collapsed metadata/history/branches to local-only, removed podcast
  progress resume/save and radio metadata monitor calls; playback safety
  machinery (ownership/preparation/readiness/session/retirement/gapless)
  untouched. RefreshWindowsMediaMetadata radio/podcast branches removed.
- MainWindow.xaml.cs / Settings.cs / PlaybackState.cs / Transport.cs / Sidebar /
  ContentLoading / ContextMenus / Helpers / TableRendering / Startup / Playlists /
  ArtistInfo / Dashboard.Media / RemoteHyperFidel: removed all radio/podcast
  fields, branches, flyouts, wiring and doc comments.
- AppSettings: removed 9 radio/podcast settings props. SettingsView.axaml.cs:
  removed matching control reads/properties.
- Localization: removed 65 radio/podcast string properties (x7 languages) plus
  resource map and 4 stale App.axaml keys. KEPT InternetRadio + Podcast strings
  solely for dormant-history/playlist display (DailyHistoryDialog,
  Playlists.Loading). Verified 7/7 language parity via static check.
- Verified: all 5 projects build 0 CS/0 AXN; Core tests 431 passed (12 cache
  tests removed with their file); desktop 376 passed / 1 known pre-existing
  YearInReviewPdfExporter failure; zero residual feature symbols.

## Task: AirPlay removal — DONE
- Deleted Audio/AirPlay2NativeSession.cs, AirPlayAudioPlayer.cs,
  AirPlayDeviceDiscovery.cs, MainWindow.AirPlay.cs and Native/AirPlay2Bridge/
  (Qt-free native sender project).
- HyperFidel.csproj: removed IncludeAirPlay2Bridge props, DLL include, removal
  targets and the Zeroconf package reference.
- CI (.github/workflows/dotnet-desktop.yml, release.yml): removed the
  "Build and test AirPlay 2 bridge" steps and /p:IncludeAirPlay2Bridge lines,
  keeping runner/matrix and offline upload-recovery invariants unchanged.
- build.ps1: removed -SkipAirPlay2Bridge param, bridge build block and property;
  scripts/verify-all.ps1 comment updated.
- OutputBackend enum: removed AirPlay value. AppSettings/OutputProfile/
  SettingsStore/SettingsView/OutputProfileDialog: removed 4 AirPlay device
  fields/properties and the AirPlay backend choice/discovery branch.
- MainWindow.Playback/Settings/Startup: removed AirPlay player creation,
  validation, output-change detection and status label branches.
- Localization: removed 5 AirPlay strings x 7 languages. Verified 7/7 parity.
- Verified: all 5 projects build 0 CS/0 AXN; zero residual AirPlay/Raop/
  Zeroconf source refs; Core tests 431 passed; desktop 376 passed / 1 known
  pre-existing YearInReviewPdfExporter failure.

## Task: HyperFidel Server / Plex network clients → offline shims — DONE (checkpoint)
- Core/Streaming/HyperFidelServerClient.cs and PlexServerClient.cs replaced with
  offline stand-ins: identical public signatures, zero HttpClient/network I/O;
  all reads return empty/default. Static artwork/stream URL builders return
  empty string (no credential URLs emitted).
- StreamingModels.cs data contracts moved to DormantDataContracts.cs (network-
  free) so legacy local settings/data still deserialize. Updates/ReleaseUpdateService
  reduced to its data records only (no signature verification/network).
- Deleted 7 Core.Tests network-client test files and 2 desktop tests
  (MissingArtistImageWriterTests, NowPlayingArtistWriteTests) that exercised
  removed remote upload/provider paths.
- Verified: all projects build 0 CS/0 AXN; Core tests 392 passed; desktop 357
  passed / 1 known pre-existing YearInReviewPdfExporter failure.
- NOTE: genuine network services remain in Core/Library (ArtistProfileService,
  FanartTvArtistImageService, ArtistImageSearchService, MusicBrainzCoverSearch,
  LibraryMetadataRepairService, LibraryScanner) and the remote HTTP DSD players.
  These are the next slices before the final zero-HttpClient gate.

## Task: 消除剩余全部网络调用 — DONE (零网络里程碑)
- 决定（用户确认）: 保留休眠数据契约，移除网络。HyperFidelServerClient /
  PlexServerClient 改为离线 stand-in：签名不变、无 HttpClient/网络 I/O，
  读操作返回空/默认；静态 artwork/stream URL 生成器返回空串（不再产生带
  凭据的 URL）。StreamingModels 数据契约移入 DormantDataContracts.cs；
  ReleaseUpdateService 缩减为纯数据记录。
- Cover Art Archive 封面搜索整体删除：MusicBrainzCoverSearch.cs、
  CoverSearchWindow.axaml(.cs)、CoverSearchDiagnostics.cs、CoverSearchSmoke
  与 CoverSearchUiSmoke 脚本；MainWindow.CoverSearch 的两个在线封面搜索方法
  离线化（保留本地上传/删除封面逻辑）。
- LibraryScanner.DownloadMissingAlbumArtworkAsync 离线化（返回 0）。
- MusicBrainzCoverSearch / FanartTvArtistImageService / ArtistProfileService /
  ArtistImageSearchService / LibraryMetadataRepairService 的网络下载方法改为
  离线 no-op；保留纯本地缓存方法（SaveImageAsync/DeleteImage/Analyze/
  FindDuplicateGroups/CreateOverrides/OrderTracks 等）。
- 删除远程 HTTP DSD/DFF 播放器（RemoteDsf/RemoteDff/RemoteDsfDop），
  CreateRemoteAsync 与 HttpDffSource 移除；MainWindow.Playback/Settings 的
  远程 DSD 分支与 IsRemote* 辅助方法移除。
- 删除桌面端 RemoteArtworkHttpClient / ImageClient，三处远程 artwork 下载
  改为只读本地缓存。
- 删除测试：7 个 Core 网络客户端测试、MissingArtistImageWriterTests、
  NowPlayingArtistWriteTests。
- 静态审计：HttpClient/Socket/TcpClient/WebClient/NetworkStream/Zeroconf
  在 shipped 源码中 0 命中；http:// 字符串仅剩输入路径判断与文档/本地缓存键。
- 验证：5 项目全部 build 0 CS/0 AXN；Core 测试 392 通过；桌面 357 通过 /
  1 已知预先存在的 YearInReviewPdfExporter 失败。

## Task: 文档与本地化验证收尾 — DONE
- CHANGELOG.md：扩展 Unreleased/Removed 条目，明确列出所有已移除的网络功能
  （Server/Plex/AI/MCP/移动远程/广播音频/电台/播客/AirPlay/跨设备续播/
  MusicBrainz 评分与在线封面/搜索/SearXNG/在线歌词/Last.fm/WebDAV/自动更新/
  FFmpeg 下载），说明残余客户端只是离线数据 stand-in，并记录 PCM 路径不碰系统音量。
- README.md：重写为 local-only 离线播放器文档（Highlights/下载表/格式/
  Requirements/Build/Data privacy/Documentation/Known limitations），删除所有
  Server/Plex/AirPlay/电台/播客/AI/MCP/网络/凭据/自动下载描述。
- AGENTS.md（根）：更新 routing，删除 HyperFidel.Server 路由与 web/streaming
  描述，加入离线播放器不变量声明。
- HyperFidel.Core/AGENTS.md：重写为本地版 invariants，删除全部网络/服务器/
  远程/更新相关条目，保留音频/数据库/扫描/搜索/相似度等本地规则。
- HyperFidel/AGENTS.md：顶部加入醒目离线声明（下方远程/网络 invariants 均失效），
  精简 Completion 中的凭据描述。
- scripts/verify-localization.cjs：移除对已删除 MobileRemote.json 和
  ArtistProfileService 语言代码行的检查；node 运行通过：7 语言 x 914 键 +
  占位符 + 网站 selector/metadata/gallery parity。
- 最终验证：5 项目 build 0 CS/0 AXN；Core 测试 392 通过；桌面 357 通过 /
  1 已知预存 YearInReviewPdfExporter 失败；HttpClient/Socket 等网络原语 0 命中。

## Task: 同专辑不同封面歌曲显示同一张图 — FIXED (2026-11-20)
- 根因：tracks 表曾刻意只把 cover_data 存 NULL（封面去重到 artworks 表），
  但每首曲目自己的 artwork_id 从未被保存；EnsureAlbum 用 COALESCE 让第一首歌
  的封面成为整张专辑封面。所有显示路径（正在播放封面/歌词背景/MPRIS/启动恢复）
  都经 GetArtworkPathsByTrackPath 读专辑级 artwork，忽略各曲目封面。
- TDD：新增 PerTrackArtworkTests（2 个测试）先 RED（两首不同封面返回相同路径）
  后 GREEN。
- 修复：tracks 新增 artwork_id 列（schema + EnsureColumn 迁移）；Upsert 把每首
  曲目 EnsureArtwork 得到的 id 存入 tracks.artwork_id；GetArtworkPathsByTrackPath
  改为 COALESCE(track.artwork_id, album.artwork_id)，优先曲目自身封面，回退专辑
  封面。旧库该列为 NULL，下次扫库时补齐。
- CHANGELOG / PROJECT-REFERENCE 已更新。
- 验证：5 项目 build 0 CS/0 AXN；Core 测试 394 全过（含新增 2）；桌面 357 过 /
  1 已知预存 YearInReviewPdfExporter 失败。

## Task: 移除设置界面残留的联网功能 (2026-11-20)
- SettingsView.axaml：删除 HyperFidelServers / Streaming / ArtistInfo 导航项与
  section header、HyperFidelServersSettingsPanel / StreamingPanel /
  ArtistInfoPanel 三个面板；MainWindow.axaml 删除空库"配置服务器"按钮。
- SettingsView.axaml.cs：删除服务器/Plex/Qobuz/Last.fm/Fanart.tv/缺失艺人图片
  的字段、属性、构造回调、初始化、事件处理器与辅助方法（约 930 行）；保留通用
  CreateStyledButton；裁剪 LoadMetadataProblemsAsync / AnalyzeAudioFeatures /
  CalculateReplayGain 的服务器循环为纯本地；更新导航路由与 Deactivate。
  注意：位置构造器 record 参数需按索引删（首次按名字删导致 parity 失败，已回退重做）。
- MainWindow.Settings.cs：删除 ApplySettingsAsync 中的 artistInfo/server/plex
  变更标志与赋值、Plex 凭据保存、服务器导航刷新；删除 LibraryServerSetupButton。
- AppSettings.cs：删除仅设置 UI 用的 LastFmScrobblingEnabled/LastFmUsername/
  QobuzApplicationId；休眠凭据/服务器数据契约保留（与既有决定一致）。
- 删除死代码：HyperFidelServerDialog / PlexServerDialog / ServerConnectionStore /
  PlexServerSettingsEqual。
- 本地化：Strings.cs + 7 语言 LocalizationManager.cs 删除 47 个设置联网字符串；
  verify-localization.cjs PASS（7 语言 x 867 键）。
- Ui.Smoke：EmptyLibraryWorkflows 删除 servers setup 断言；Program.cs 的
  DenyExternalRequests.Install 改为 no-op（无 HttpClient 可隔离）。
- 验证：5 项目 build 0 CS/0 AXN；Core 测试 394 全过；桌面 357 过 / 1 已知预存
  YearInReviewPdfExporter 失败。CHANGELOG 已更新。
