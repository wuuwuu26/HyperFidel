# HyperFidel Local-Only Player Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans
> to implement this plan task-by-task in this session. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Turn HyperFidel into a purely local/offline Ubuntu music player:
never change system/hardware volume, offline garbled-Chinese-safe lyrics, and
remove all networking/server/MCP code.

**Architecture:** In-place staged removal on the existing Avalonia/C# app.
Keep the local library core (SQLite/Lucene, scanning, playback, local audio
backends, queue, playlists, equalizer); delete every network project,
service, client, package, UI surface, setting, string and test; reduce the
shared catalog core to local-only paths. Build and unit-test after each stage.

**Tech Stack:** C# / .NET 10, Avalonia 12, Microsoft.Data.Sqlite, Lucene.NET,
TagLibSharp, SkiaSharp, OpenAL / ALSA (P/Invoke), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-16-local-offline-player-design.md`

## Global Constraints

- Build: `dotnet build` with SDK 10.0.404 at `/tmp/opencode/dotnet` (set
  `DOTNET_ROOT` and `PATH`; `DOTNET_CLI_TELEMETRY_OPTOUT=1`).
- Linux target `net10.0`; Windows-only files are removed from Linux build via
  `HyperFidel.csproj` `Compile Remove` items.
- Root AGENTS.md checklist: CHANGELOG (Unreleased), README, AGENTS.md,
  PROJECT-REFERENCE.md; 7-language localization parity; English XML docs on
  every affected C# member; `.md` wrapped at 80 cols, prettier + markdownlint.
- Root maintenance invariant: macOS CI/release runner selection stays matched
  to matrix; keep offline upload recovery CI checks; regenerate AUR
  `.SRCINFO` if touched. Do not wholesale-delete player release workflows.
- Owner decisions: (1) keep compilable/testable local skeleton; (2) keep
  DSD/DoP hardware-mixer writes (bit-perfect DSD), but PCM paths never touch
  system/hardware volume.
- No credentials, authenticated URLs, or secrets persisted/logged/exposed.
- Verification limits: no GUI here; compile + unit tests + static checks.
  Owner confirms the two runtime behaviours on Ubuntu.

## Decisions Locked In

- Delete `HyperFidel.Server` + `HyperFidel.Server.Tests` + `.sln` entries +
  server release workflow/systemd/docs.
- Keep MPRIS (local D-Bus), local scheduled backup, direct ALSA
  `PipeWireExclusiveHandoff` (`set-card-profile`, non-volume).
- Remove `FfmpegLocator` GitHub download; keep system FFmpeg discovery.
- Keep dormant offline DB columns (MusicBrainz IDs, ratings, AcoustID).
- Online-derived enrichment worker and "Load rating"/"Try again" UI removed.

## Review Focus (spec-implied, test-pinned)

1. Any PCM track switch must not call `set-sink-volume`/`set-sink-mute` or
   `AlsaHardwareVolume.EnsureUnity`. Test: static-call audit + volume tests.
2. A GBK/GB18030 sidecar `.lrc` must decode without mojibake, and a UTF-8
   one must not be mangled. Test: `LocalLyricsLoaderTests`.
3. No shipped C# may contain `HttpClient`/`Socket`/`lrclib.net`/
   `api.github.com`/`set-sink-volume`. Test: final grep gate.
4. Removing remote types must not break local library build or the local
   unit-test suite. Test: `dotnet build` + `dotnet test` per stage.
5. Old settings JSON with removed remote fields must still load without
   throwing or persisting secrets. Test: settings-load test.

---

## Phase 1: Bug fixes (TDD)

### Task 1: Offline local lyrics loader with encoding sniffing (Core)

**Files:**
- Modify: `HyperFidel.Core/Library/LyricsService.cs`
- Test: `HyperFidel.Core.Tests/LocalLyricsLoaderTests.cs` (create)

**Interfaces:**
- Produces: `LyricsService.TryLoadLocalLyrics(string trackPath)` returns
  `LyricsDownloadResult?` (plain + synced strings, or null); reads a sidecar
  `<basename>.lrc` next to the track, BOM/UTF-8/GB18030 sniffing.
- Keeps: `LyricsService.ParseLrc(string?)`.

- [ ] Step 1: Failing test - write `LocalLyricsLoaderTests` with cases:
  UTF-8 `.lrc`, UTF-8 BOM `.lrc`, GB18030 Chinese `.lrc` (assert recovered
  CJK text), UTF-16 LE BOM, missing file (null), a sidecar with LRC
  timestamps retained in the synced result.
- [ ] Step 2: Run `dotnet test HyperFidel.Core.Tests/... --filter LocalLyricsLoaderTests` -> FAIL (method missing).
- [ ] Step 3: Implement `TryLoadLocalLyrics` + `DecodeLyricsBytes` in
  `LyricsService.cs`; remove `DownloadAsync`/`SearchAsync`/`HttpClient`/
  LRCLIB records only after Task 3 rewires callers.
- [ ] Step 4: Run the test -> PASS; run full `HyperFidel.Core.Tests` -> PASS.
- [ ] Step 5: Commit.

### Task 2: Never write PCM system/hardware volume

**Files:**
- Modify: `HyperFidel/Compatibility/Linux/WindowsAudioCompatibility.cs`,
  `HyperFidel/Compatibility/Linux/AlsaHardwareVolume.cs`,
  `HyperFidel/Compatibility/Linux/WasapiAudioPlayer.cs`,
  `HyperFidel/Audio/WindowsEndpointVolumeSynchronizer.cs`,
  `HyperFidel/MainWindow.Settings.cs`, `HyperFidel/MainWindow.Playback.cs`
- Test: static-audit test in `HyperFidel.Tests/NoSystemVolumeWritesTests.cs`

**Interfaces:**
- Keep `AlsaHardwareVolume.SetVolume(deviceId, volume)` for DSD/DoP only.
- Remove `AlsaHardwareVolume.EnsureUnity`.
- Linux `WindowsEndpointVolumeSynchronizer` becomes inert
  (`SupportsEndpointVolume` returns false, `SetVolume` no-op, no pactl
  subscription).

- [ ] Step 1: Failing test - assert the compiled source of the PCM open path
  contains no `EnsureUnity` call and no `set-sink-volume`/`set-sink-mute`
  outside DSD players; assert `SupportsEndpointVolume` is false on Linux.
- [ ] Step 2: Run -> FAIL.
- [ ] Step 3: Remove the PCM writes (keep DSD `SetVolume`); make the Linux
  synchronizer inert; drop `EnsureUnity` call in `WasapiAudioPlayer`; reduce
  `MainWindow.Settings.cs` slider/apply paths to software gain only.
- [ ] Step 4: Run test -> PASS; `dotnet build HyperFidel/HyperFidel.csproj` -> PASS.
- [ ] Step 5: Commit.

### Task 3: Rewire lyrics to offline local provider (desktop)

**Files:**
- Modify: `HyperFidel/NowPlayingMetadataProviders.cs`,
  `HyperFidel/MainWindow.Transport.cs`, `HyperFidel/MainWindow.LibraryViews.cs`
- Test: update/add `HyperFidel.Tests` for offline provider behaviour.

- [ ] Step 1: Failing test for `LocalNowPlayingMetadataProvider` returning
  sidecar/embedded lyrics without any network call.
- [ ] Step 2: Run -> FAIL.
- [ ] Step 3: Remove remote `INowPlayingMetadataProvider` impl and LRCLIB
  `DownloadLyricsAsync`; simplify `LoadLyricsForTrackAsync` to a local read;
  update `L_RefreshLyrics`-family strings in all 7 languages.
- [ ] Step 4: Build + test -> PASS.
- [ ] Step 5: Commit.

## Phase 2: Networking removal (staged; build+test per stage)

### Task 4: Delete server projects and solution entries
- [ ] Remove `HyperFidel.Server/`, `HyperFidel.Server.Tests/`, their `.sln`
  entries, server release workflow, systemd/packaging/docs. Build.

### Task 5: Remove self-contained desktop feature folders
- [ ] Delete `HyperFidel/Mcp/`, `AI/`, `Remote/`, `Scrobbling/`, AirPlay,
  browser-audio, cover-search, radio/podcast/plex/remote/mobile/update/
  cross-device-resume partials, dialogs and embedded resources; strip their
  `MainWindow.xaml.cs`/Startup/Settings wiring. Build.

### Task 6: Remove Core networking services
- [ ] Delete `HyperFidel.Core/Streaming/`, `Web/`, `Updates/`,
  `Scrobbling/`, FFmpeg GitHub download, and network-using library services
  (radio, podcast, MusicBrainz, Fanart.tv, Wikipedia, artist profile/image
  search, backup uploader, remote artwork, HTTP scanner paths). Build.

### Task 7: Reduce shared catalog to local-only
- [ ] Strip remote branches from `ContentRow`, catalog providers,
  `LibraryViewCache`, `MainWindow.LibraryViews`/`Dashboard`/`GenreCloud`/
  `Playlists`/`Queue`/`Search`/`Navigation`/`Sidebar`, settings, dialogs.
  Keep the local skeleton. Build.

### Task 8: Settings, localization, packages, credentials
- [ ] Remove `ModelContextProtocol*`, `QRCoder`, `Zeroconf` package refs;
  strip `AppSettings`/`ApplicationCredentialStore` network fields; remove
  network strings from `Strings.cs` + 7 languages; remove
  `verify-mcp-tool-parity.ps1`; delete/adjust affected tests. Build + test.

### Task 9: Documentation and verification
- [ ] Update `AGENTS.md` (audio invariants + drop network invariants),
  `README.md`, `CHANGELOG.md` (Unreleased), `docs/PROJECT-REFERENCE.md`,
  AUR `.SRCINFO` if needed; run `verify-localization-parity.ps1`, prettier,
  markdownlint. Final: solution build, all surviving tests, static grep
  gate, report to owner.

## Execution notes

- After each stage run `dotnet build HyperFidel.sln -c Debug` and
  `dotnet test` on surviving test projects; fix compile errors from removed
  types by removing the referencing code (not by reintroducing network code).
- The `HyperFidel.Server.Tests` baseline post-build copy conflict is moot
  once that project is removed.
- Commit after each task with a descriptive message.
