# HyperFidel.Core Instructions

This file applies to `HyperFidel.Core/` and supplements `../AGENTS.md`.

HyperFidel is a local-only offline player. Core contains no network clients,
HTTP clients, sockets, remote-library code or web-fetching services. Dormant
data contracts and database columns retained from earlier remote features hold
inert local data only and must never perform I/O. Do not reintroduce networking.

## Completion

- Follow the root mandatory completion checklist.
- Build with `dotnet build HyperFidel.Core/HyperFidel.Core.csproj` and also build each
  affected consumer when a public contract changes.
- Public/internal C# APIs require complete English XML documentation.

## Core Invariants

- Keep the project cross-platform `net10.0`; do not introduce Avalonia, Windows,
  DPAPI, WASAPI, ASIO, or other platform-specific dependencies.
- Put shared library scanning, SQLite persistence, search, and FFmpeg primitives
  here. This project performs no network I/O.
- Preserve SQLite migrations, stable IDs, WAL behavior, CUE virtual-path
  identity, user favorites, artwork caches, ReplayGain data, and `added_at`.
- `LibraryBackupService` supports both the process data root and an explicit
  alternate data root. Backups remain versioned, exclude audio and credentials,
  use a consistent SQLite snapshot, validate staged imports, and roll back
  partial replacements before rebuilding the search index.
- Chaptered MKA containers use FFprobe-derived stable `mka://chapter/` virtual
  paths and the existing segment columns; unchaptered MKA files remain ordinary
  tracks. Each probe uses bounded analysis and a 30-second timeout.
- Library-only genre corrections live in `track_genre_overrides` (keyed by
  stable track path, so `cue://` and `mka://chapter/` tracks are covered) and
  are reapplied by every `AudioDatabase.Upsert`. `SetTrackGenres` updates
  several tracks in one transaction and writes the override at the same time; an
  empty value removes it so the next scan restores the embedded genre. Never
  write these corrections into media files.
- Library-only title corrections live in `track_title_overrides` and must be
  applied by every `AudioDatabase.Upsert`; never write these corrections back to
  the source media.
- Scanner, watcher, and reconciliation writes must keep SQLite and Lucene in
  sync and use the shared scanner gate.
- Explicit ReplayGain maintenance must retain only missing album IDs across the
  track pass and update Lucene in bounded batches. Never accumulate complete
  `TrackRecord` instances or one library-wide update list for this workflow.
- Multi-root owner scans must hold a watcher-operation suspension for their
  complete duration. This prevents incremental updates or periodic
  reconciliations from taking the shared scanner gate between roots and making
  the reported foreground scan appear stalled on its next root.
- `LibraryScanner.RefreshMetadataAsync` is the explicit maintenance path that
  bypasses timestamp skipping and re-reads every supported file. It shares the
  normal scanner gate and reconciliation/indexing pipeline, preserves
  library-only overrides, and never changes source media.
- A failed TagLib read must never upsert the file-system-only fallback record.
  Full, forced, watcher, and reconciliation scans retry boundedly, count a final
  failure, and preserve the existing database metadata unchanged.
- For WavPack, technical property access can fail at extended sample rates while
  tags remain readable. Preserve successful tags and supplement missing source
  properties with a cancellable, 15-second FFprobe read of technical fields
  only. Never probe failed tag opens, replace valid values or write source
  files. CUE projection caches each physical source probe, including failure,
  per sheet and applies source duration before computing the final segment
  duration.
- When an upsert changes a track's album identity inside the same physical album
  directory, carry the previous album's artwork and favorite flag to the target.
  Existing target artwork wins; favorites are combined rather than cleared.
- Keep compact query models compact; do not add artwork BLOBs, lyrics, or full
  records to list/facet/folder queries.
- Free-text Lucene queries are term-centric across their supplied fields: every
  analysed term is required, but different terms may match different fields.
  Preserve this behavior so combined artist/title searches work identically.
- `LibrarySearchFilter` owns structured release-year and library-added timestamp
  filtering plus deterministic relevance/title/year/addition ordering. Date
  ranges are half-open Unix ranges after the client converts inclusive calendar
  dates. `SmartPlaylistTrackInfo.AlbumId` supports bounded album grouping and
  must remain compact.
- `QueuePathPolicy.CanPersist` is the single decision for whether a queue or
  playback path may be persisted. It keeps local and `cue://` paths persistable
  and rejects HTTP/HTTPS URLs that embed user information or credential query
  parameters. A pure helper with no network behavior; covered by
  `HyperFidel.Core.Tests/QueuePathPolicyTests.cs`.
- Album catalog queries, recent-album queries, detail lookup, and Dashboard
  album totals expose only albums referenced by at least one indexed track.
  Artist catalog rows and artist totals likewise require a track-backed album;
  never surface orphaned normalization rows as usable library entities.
- `LibraryMetadataRepairService` groups tracks by their immediate physical
  directory and detects inconsistent album metadata from local tags. Confirmed
  corrections are persisted in `track_metadata_overrides` and reapplied by every
  `AudioDatabase.Upsert`; never write them into media files implicitly. Its
  duplicate detection uses local evidence only: equal non-empty AcoustID
  fingerprints on distinct physical paths, sequential hashing of equal-size
  files into exact SHA-256 matches, and “likely” when a hash is unavailable.
  `FindDuplicateGroups` is read-only and must never remove, move, or merge
  anything. `LibraryScanner.RemoveTracksByPaths` keeps SQLite, Lucene, and the
  waveform cache in sync, removes virtual CUE/MKA tracks sharing a removed source,
  and only deletes files when the caller asks. Artist spelling variants are
  guided-review findings only. `Analyze(inspectFiles: false)` is index-only;
  default callers retain full checks. `OrderTracks` is shared by search, display
  and persistence (disc, track, source, stable ID).
- Dashboard recommendations use compact album-level genre/BPM candidates from
  `AudioDatabase.GetRecommendationAlbums`. Keep this payload free of track rows
  and artwork bytes. Local recent-album and recommendation queries must aggregate
  the `tracks` table before joining album, artist, and artwork metadata.
  Preserve the covering `(album_id, added_at DESC)` index.
- `SimilarityFeatureService` owns the versioned provider-neutral track feature
  contract. Version two uses effective genre, BPM, explicit mood, favorite,
  personal/community rating, listening history, and optional cached acoustic
  descriptors. Preserve deterministic normalization and credential-free source
  keys. `RankSimilar`, `RankMood`, and `RankPreset` are deterministic and share
  one diversity-limited selection helper; **Focus**, **Workout** and **Wind down**
  presets use cached descriptors with a neutral prior for missing values.
  `SmartPlaylistCriteria.Resolve` applies `RankSimilar` when a reference is set,
  removes weak neighbours by `SimilarityMinimumScore`, and returns an empty list
  for an unresolvable reference.
- Optional acoustic descriptors live in the provider-local
  `track_audio_features` table and survive metadata scans. Version changes must
  trigger bounded reanalysis. `AudioFeatureAnalysisService` decodes at most 90
  seconds as 8-kHz mono, restricts FFmpeg to one thread and best-effort reduced
  priority, and produces only normalized energy, brightness, and dynamics plus
  an optional estimated key. Maintenance is sequential with a seven-day retry
  cooldown for failed sources.
- `CamelotKey` is the single Camelot-wheel mapping: it parses conventional key
  names and wheel labels, exposes wheel distance (identical `0`, relative or
  neighbouring `1`), and never guesses an unsupported spelling. Key estimation
  uses a bounded Goertzel chromagram correlated against Krumhansl-Schmuckler
  profiles and must stay conservative. `HarmonicOrdering.Order` is the single
  greedy wheel walk used by playback batches: deterministic, keeps keyless items
  in relative order, and returns its input unchanged below two keyed items.
- `GenreCloudService` owns the stable hierarchical genre taxonomy, tag
  normalization, count aggregation, breadcrumbs, and bounded candidate selection
  from the local library. Its curated data lives in the embedded
  `Library/GenreTaxonomy.json`; definitions may be top-level and have multiple
  parents, so traversal must be cycle-safe and node counts must deduplicate a
  track across graph paths. Every node carries a distinct album count from the
  compact facet row's optional `AlbumId`. Unmapped tags retain dynamic
  `unmapped:` keys under `more-genres`. `ResolveLeafGenreKeys` and
  `ResolveDescendantGenreKeys` perform cycle-safe recursive expansion for
  Genre Cloud-driven Infinite Mix filters.
- `AudioDatabase.GetListeningTrend` supports up to 366 equal chronological
  buckets so the client can request daily Dashboard points without materializing
  playback-history rows.
- `ArtistNameNormalizer.CreateComparisonKey` is the shared identity key for
  comparing artist names in the local library.
- Local artist browsing is album-artist-centered. The scanner records whether
  `ALBUMARTIST` was missing, imports supported compilation flags, and
  `AudioDatabase.ReconcileAlbumArtists` resolves the complete album before the
  Artists view is exposed. Explicit consistent album artists win; otherwise
  compilation or differing inferred track artists resolve to `Various Artists`.
  Primary track artists stay attached to their tracks. Featured suffixes remain
  governed by `ArtistNameNormalizer`. Embedded MusicBrainz artist IDs in tags
  take precedence over name comparison when resolving a local artist identity.
- `ArtistImageSearchService` owns writes and explicit deletion of the local
  `artist-images/<id>.*` cache variants. Cache stems and delete patterns must use
  invariant-culture numeric artist IDs. Shared in-process mutation admission
  uses canonical data-directory plus artist identity, never logged or persisted.
  Hold ownership through unique staging, publication and old-format cleanup.
  Cancelled waiters must not release another writer; cancellation before
  publication preserves the prior image. Keep caller-owned streams open and
  reject empty transfers before publication. UI callers must await
  `DeleteImageAsync` or use a worker for legacy deletion. File serialization
  does not order user intent or coordinate separate processes.
- `FfmpegLocator` searches platform-specific installation directories in
  addition to the inherited `PATH`. Linux remains system-package-managed; do not
  download binaries at runtime.

Consult the detailed database, scanner, search, and audio rules in
`docs/PROJECT-REFERENCE.md` (repository-relative) before modifying those areas.

- `AudioDatabase` applies connection pragmas on every connection but coalesces
  schema creation/migration once per physical path and process. A newly created
  file at a reused path must discard the old initialization marker.
- Use `GetTrackListPage` when only one ordered page is required; do not load the
  complete track list and page it in managed memory.
- All compact track readers (full list, album, paths, IDs and pages) must
  overlay active-profile favorites and user ratings in SQLite with the
  established legacy-column fallback. Explicit false/zero state overrides legacy
  values; absent state keeps fallback. Preserve bounded paging/batching and
  caller ID order; never materialize the whole library to apply personal state
  after paging.
- Explicit HYPERFIDEL_DATA_DIR roots never import legacy Player data. Default-root
  migration preserves current destination files, tolerates concurrent file
  creation only for file-exists errors and marks completion only after success.
  The immediate legacy root is `HyperFi`, falling back to `Orynivo` when no
  HyperFi directory exists, so upgrades from either earlier name keep their
  library. Keep migration tests on fixture-owned roots, never personal
  application data.
