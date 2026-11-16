# HyperFidel - Local-Only Player Design (2026-11-16)

## Status

Refined after an independent builder check. Two owner decisions are still open
(section "Owner decisions"). No product code has been changed yet. Sending the
decision form approves this design and starts implementation.

## Intent and why

The owner runs HyperFidel on Ubuntu 26 and wants a purely local, offline
music player. Three requests:

1. When switching or playing tracks, the system volume jumps to maximum.
   HyperFidel must never change the system (or hardware) volume.
2. The lyrics page shows garbled text; the owner suspected incomplete
   bundled fonts. Lyrics must be offline/local and render correctly.
3. Remove all networking, server, and MCP functionality.

Owner decisions already made: remove networking as thoroughly as possible;
replace online lyrics with offline local lyrics and fix the garbling.

## Goals

- Zero network calls at runtime: no HTTP/DNS/Socket/WebDAV/LAN servers, no
  update checks, no cloud metadata or accounts, no casting.
- Never write to the system mixer (PipeWire/PulseAudio via `pactl`) or to the
  ALSA hardware mixer. Volume is applied only as in-app software gain.
- Lyrics come only from local sources (sidecar `.lrc` files and embedded
  tags), with correct decoding for GBK/GB18030 Chinese files; CJK text
  renders correctly with the bundled fonts.
- The local library (scan, SQLite, Lucene, playlists, queue, equalizer, all
  local audio backends) keeps working.
- The solution builds on Linux with .NET 10 and its unit tests pass.

## Non-goals and anti-goals

- No remote libraries, Plex, servers, MCP, AI chat, mobile/LAN remote,
  radio, podcasts, AirPlay/casting, scrobbling, online cover or metadata
  lookups, auto-updates, WebDAV uploads, or QR codes.
- Not a clean-slate rewrite: keep the existing local player core so the GUI,
  library and playback keep working.
- Anti-goal: leaving any code path that can still open a socket or change the
  system volume, even if labelled "disabled".

## Constraints and verification limits

- This environment has a .NET 10 SDK (10.0.404) but no reliable GUI session;
  Avalonia rendering, the real PulseAudio session and CJK glyph display
  cannot be visually verified here.
- Baseline established: Core, Server, desktop HyperFidel, Tests and
  Ui.Smoke all compile. The only baseline failure is a pre-existing
  post-build copy conflict in `HyperFidel.Server.Tests` (a `TranscodeChild`
  directory); that project is deleted in this work, so it is irrelevant.
- Verification is `dotnet build` of every surviving project, `dotnet test`
  of the surviving test projects, and static checks that no
  `HttpClient`/`Socket`/`set-sink-volume`/remote URLs remain in shipped code.
- The two runtime behaviours (system volume untouched, lyrics render) must be
  confirmed by the owner on Ubuntu.
- Repository rules (AGENTS.md): update CHANGELOG (Unreleased), README,
  AGENTS.md, and docs/PROJECT-REFERENCE.md; keep 7-language localization
  parity; keep English XML docs on every affected C# member; wrap `.md`
  prose at 80 columns and run prettier + markdownlint on changed docs.
- Root AGENTS.md maintenance requirement must survive: keep macOS CI and
  release runner selections matched to matrix architectures, keep offline
  upload recovery checks in the CI verify job, and regenerate
  `packaging/aur/.SRCINFO` if AUR recipes change. Do not wholesale-delete
  player release workflows that these invariants require; delete only the
  server/MCP-specific workflows.

## Approaches considered

1. Disable-and-hide only - fastest, but leaves all network code and
   dependencies in place. Rejected by the owner.
2. Clean-slate minimal player - write a new app from scratch. Rejected:
   discards the working local library, playback and UI; far larger risk.
3. Systematic in-place removal (chosen, recommended): delete every
   networking project, folder, service, client, package, UI surface,
   setting, string and test, then rewire startup/sidebar/settings to the
   local core; build and test after each stage.

## Settled implementation calls (builder decisions, not owner questions)

These are recorded so the owner can see every default applied:

- The whole `HyperFidel.Server` + `HyperFidel.Server.Tests` projects, their
  `.sln` entries, server release workflow, systemd units, and server docs
  are deleted (already implied by "remove all networking/server").
- MPRIS media-key integration stays: it is local session-D-Bus IPC, not
  networking, and does not contact any host.
- `FfmpegLocator` GitHub download of FFmpeg/ffprobe on Windows/macOS is
  removed for a true zero-network codebase; Linux is already
  system-package-managed per AGENTS.md, so this has no Linux impact.
  System FFmpeg discovery remains.
- Local scheduled library backup (`LibraryBackupService`, `AppSettings.
  ScheduledBackup`) stays; only the WebDAV upload (`BackupUploader`) is
  removed.
- Direct-ALSA `PipeWireExclusiveHandoff` keeps its `pactl set-card-profile`
  use: it is a local, non-volume card handoff required for direct ALSA. Only
  volume-related `pactl` calls are removed. The static check targets
  `set-sink-volume`/`set-sink-mute`/`subscribe` and `HttpClient`/`Socket`,
  not all `pactl` or `-protocol_whitelist`/`hyperfidel://` text.
- Online MusicBrainz rating enrichment worker and its "Load rating"/"Try
  again" UI are removed. Already-stored local rating values / offline DB
  columns stay as dormant data (no network calls).
- Legacy persisted remote settings/credentials/queue refs: the settings
  serializer ignores removed fields, so old JSON loads without active
  remote features; no plaintext credentials are written. No active
  migration of `hyperfidel://` queue entries beyond ignoring them.
- Dormant offline DB columns fetched historically from the network
  (MusicBrainz IDs, cached ratings/genres, AcoustID) stay in the schema;
  removing them would break the local library format for no runtime benefit.

## What "delete all networking code" means here

The shared catalog model (`ContentRow`, `ILibraryCatalogProvider`,
`LibraryViewCache`, SQLite/Lucene, the `MainWindow` view partials) IS the
local library. It must remain or there is no player. "Delete all networking
code" therefore means removing every remote/network branch, provider,
service, UI surface, setting, string and test, and reducing that shared core
to local-only paths - while keeping the local-only skeleton so the player
still works. The verification posture and how much dormant structure to
tolerate is one of the two owner decisions below.

### Removed (projects, folders, packages)

- Projects: `HyperFidel.Server`, `HyperFidel.Server.Tests` plus their `.sln`
  entries, release workflows, packaging, systemd units and docs.
- Desktop folders/partials: `Mcp/`, `AI/`, `Remote/`, `Scrobbling/`,
  AirPlay, browser-audio, cover-search, radio/podcast/plex/server/remote/
  cross-device-resume/musicbrainz-rating/backup-upload/update partials and
  dialogs; the service start/stop wiring in `MainWindow.xaml.cs`/Startup.
- Core folders/files: `Streaming/`, `Web/`, `Updates/`, `Scrobbling/` and
  network-using library services (radio, podcast, MusicBrainz, Fanart.tv,
  Wikipedia, artist profile/image search, backup uploader, LRCLIB, remote
  artwork, HTTP scanner paths, FFmpeg GitHub download).
- NuGet: `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`,
  `QRCoder`, `Zeroconf`; ASP.NET/TestHost for the server tests.
- Settings/credential/localization fields and strings for all of the above;
  tests that reference removed types; `verify-mcp-tool-parity.ps1`.
- Remote HTTP-range DSD players (`RemoteDsf*`, `RemoteDff*`) and the
  native `AirPlay2Bridge` are removed; local DSD/DoP players remain.

### Kept

- Local library scan, SQLite/Lucene, playlists, queue/Up Next, history,
  equalizer, all local audio backends (OpenAL, direct ALSA, local DSD/DoP),
  local artwork cache, ReplayGain, waveform, lyrics view, local scheduled
  backup, MPRIS media keys.

## Design and how

### Phase 1 - the two bug fixes (small, verifiable)

Volume - never touch system or hardware volume:

- `HyperFidel/Compatibility/Linux/WindowsAudioCompatibility.cs`: remove the
  `#if HYPERFIDEL_LINUX` pactl volume synchronizer (its
  `set-sink-volume`/`set-sink-mute`/`subscribe` writes and the
  `pactl` subscription); replace with an inert synchronizer whose
  `SupportsEndpointVolume` returns false. Keep `PipeWireExclusiveHandoff`.
- `HyperFidel/Audio/WindowsEndpointVolumeSynchronizer.cs` (Windows) and the
  `WindowsMediaTransportService` volume paths: keep as no-ops / remove the
  system-volume write; the transport slider never writes to an endpoint.
- `HyperFidel/Compatibility/Linux/AlsaHardwareVolume.cs`: remove
  `EnsureUnity` (the per-open push to max) and hardware-mixer writes; see
  the DSD owner decision below for `SetVolume`.
- `Compatibility/Linux/WasapiAudioPlayer.cs`: drop the `EnsureUnity` call.
- DSD/DoP players (`DsfDopAudioPlayer`, `DffDopAudioPlayer`): drop hardware
  writes at creation/setter per the owner decision; new players still start
  silent via software/prepared volume where applicable.
- `MainWindow.Settings.cs` (`ConfigureEndpointVolumeSynchronizationAsync`,
  `VolumeSlider_OnValueChanged`, `ApplySystemVolume`, slider change handler)
  and `MainWindow.Playback.cs`: reduce to in-app software gain only; never
  push to an endpoint or the system. Keep cubic perceptual PCM curve and
  "new players start silent and receive the prepared slider value".
- `HyperFidel.Core/Audio/PactlVolumeOutput.cs` and its tests are removed if
  unused after the above.
- Update the audio invariants in `HyperFidel/AGENTS.md` and root
  `AGENTS.md` routing accordingly.

Lyrics - offline local, no mojibake:

- Evidence the fonts are not the cause: bundled `Noto Serif SC` OTFs are
  full CJK fonts (~30,900 mapped codepoints), the `HyperFidel Serif` alias
  and CJK fallback are configured in `Program.cs`, and the lyrics view uses
  the inherited default family. A font problem would garble all UI text,
  not only lyrics. The garbling is an encoding issue in lyric text.
- `HyperFidel.Core/Library/LyricsService.cs`: remove `DownloadAsync`,
  `SearchAsync`, the `HttpClient`, LRCLIB URLs/records and `CreateClient`;
  keep `ParseLrc` / `ParseLineContent` (pure, already unit-tested). Add a
  local loader `TryLoadLocalLyrics(trackPath)` that:
  - looks for a sidecar `<track-basename>.lrc` (and a shared lyrics folder
    variant) next to the audio file;
  - reads bytes and sniffs encoding: UTF-8/UTF-16 BOM, valid UTF-8, else
    fallback to GB18030 (which covers GBK/GB2312 Chinese `.lrc` files);
  - returns plain or synced text; embedded-tag lyrics (`TrackRecord.Lyrics`
    / `SyncedLyrics`, already decoded by TagLib) remain available as-is.
- `HyperFidel/NowPlayingMetadataProviders.cs`: drop the remote provider and
  the LRCLIB `DownloadLyricsAsync`; `LocalNowPlayingMetadataProvider` reads
  local DB lyrics plus the sidecar `.lrc`. `GetArtistProfileAsync` becomes
  offline (no Wikipedia/Last.fm/Fanart download).
- `MainWindow.Transport.cs` `LoadLyricsForTrackAsync`: replace the 30-day
  online lookup/expiry/force-refresh/download-failed flow with a direct
  local read; keep the lyrics UI, word highlighting and smooth scroll.
  Update or remove `LyricsDownloading`/`LyricsDownloadFailed`/
  `LyricsNotFound`/`L_RefreshLyrics` strings in all 7 languages.
- The exact original source of the owner's garbled lyrics (embedded tag vs
  sidecar) cannot be confirmed without their files; the loader is made
  robust to the common GBK/GB18030 and BOM cases and the owner confirms the
  result on Ubuntu.

### Phase 2 - systematic networking removal

Staged, building and testing after each stage:

1. Delete server projects and `.sln`/workflow/packaging entries.
2. Remove desktop feature folders and their startup wiring.
3. Remove Core networking services and DTOs.
4. Strip remote branches from the shared catalog/library views and
   `MainWindow` partials; reduce to local-only providers (dominant risk;
   keep the local skeleton).
5. Strip settings, sidebar, dialogs, localization (7 languages), credential
   store, `AppSettings`, package references; remove `verify-mcp-tool-parity`.
6. Delete or rewrite affected tests; remove dead `using`s.
7. Update docs: AGENTS.md (drop network invariants, fix audio invariants),
   README, CHANGELOG (Unreleased), PROJECT-REFERENCE, AUR `.SRCINFO` if
   touched; run `verify-localization-parity.ps1`, prettier, markdownlint.
8. Final: build all, run tests, precise static grep for residual network or
   system-volume calls.

## Verification checklist (final)

- `dotnet build HyperFidel.sln -c Debug` on the reduced solution.
- `dotnet test` for each surviving test project.
- `scripts/verify-localization-parity.ps1` (pwsh if available, else a static
  7-language parity check).
- Static grep: no `HttpClient`, `Socket`, `set-sink-volume`,
  `set-sink-mute`, `pactl subscribe`, `lrclib.net`, `api.github.com`,
  remote metadata hosts, or `https://` service URLs in shipped C#.
- Changed `.md`: prettier `--prose-wrap always --print-width 80` and
  `markdownlint-cli2`.
- Owner confirms on Ubuntu: (a) system/hardware volume never changes on
  track switch/play, (b) Chinese lyrics render without mojibake.

## Owner decisions

1. Verification/scope posture (see "What delete all networking means"):
   prioritize a compilable, test-passing local player that keeps a dormant
   local-only catalog skeleton, versus a literal maximal purge that rewrites
   the shared abstractions (higher risk, unverifiable without a GUI).
2. Bit-perfect DSD/DoP volume: leave hardware fully untouched (slider
   controls PCM only, DSD plays at the hardware's current level) versus
   keeping DSD-only hardware writes versus forcing DSD-to-PCM conversion.
