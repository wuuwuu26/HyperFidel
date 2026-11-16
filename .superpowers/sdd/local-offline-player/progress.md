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
