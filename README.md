<!-- markdownlint-disable MD033 MD041 -->
<p align="center">
  <img src="Logo/hyperfidel.png" alt="HyperFidel" width="160">
</p>

# HyperFidel

**A local-only Hi-Fi player and library for Windows, Linux, and macOS.**

HyperFidel is for people who keep their music as files. It plays that library
through your own DAC and keeps the interface out of the way. It is completely
offline: no account, no subscription, no network requests, and no telemetry.

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/wuuwuu26/HyperFidel)](https://github.com/wuuwuu26/HyperFidel/releases/latest)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%C2%B7%20Linux%20%C2%B7%20macOS-lightgrey)

**Downloads:** [latest release](https://github.com/wuuwuu26/HyperFidel/releases/latest) ·
**Wiki:** [github.com/wuuwuu26/HyperFidel/wiki](https://github.com/wuuwuu26/HyperFidel/wiki) ·
**Full reference:** [docs/FULL-FEATURE-REFERENCE.md](docs/FULL-FEATURE-REFERENCE.md)

## Highlights

- **Direct hardware playback.** Exclusive WASAPI, cwASIO, and optional Steinberg
  ASIO on Windows; direct ALSA on Linux with native DSD (DSF/DFF) and DoP
  fallback; OpenAL on macOS. Gapless playback, ReplayGain, a +6 dB PCM boost,
  and a parametric EQ with Equalizer APO / AutoEQ import.
- **DSD without conversion when the hardware supports it.** Linux opens
  `DSD_U32_BE` directly and falls back to DoP; Windows uses cwASIO or ASIO.
  WASAPI can convert DSF/DFF to PCM in real time when native DSD is unavailable.
- **A complete local library.** Artists, Albums, Tracks, and Folder views, plus
  CUE sheets, MKA chapters, smart playlists, full-text search, and bulk
  favorite/rating/genre edits. All data stays on this machine.
- **A warm, gold interface.** Translucent surfaces over a soft gradient, a
  focused Dashboard, artwork-led browsing, and a lyrics view that scrolls
  smoothly and highlights enhanced-LRC words as they become audible.
- **Discovery that uses your own collection.** A hierarchical Genre Cloud,
  Infinite Mix, similar-track queues, and listening statistics.
- **Private by default.** No account, no telemetry, and no network access. The
  complete interface is available in English, German, French, Spanish, Russian,
  Simplified Chinese, and Hindi.

## Screenshot

<img src="html/assets/screenshots/hyperfidel.png" alt="HyperFidel library view" width="800">

## Download

Self-contained builds bundle the .NET runtime. Pick the package for your
platform from the [latest release](https://github.com/wuuwuu26/HyperFidel/releases/latest):

| Component      | Packages                                                       |
| -------------- | -------------------------------------------------------------- |
| Windows player | `Setup.exe`, portable ZIP (Windows 10/11 x64)                  |
| Linux player   | DEB, RPM, Arch `.pkg.tar.zst`, portable tar.gz (x64 and ARM64) |
| macOS player   | PKG and portable app ZIP (Apple Silicon and Intel)             |

Desktop updates are handled outside the app; the offline player itself never
contacts a release server.

## Supported formats

`DSF`, `DFF`, `FLAC`, `MP3`, `WAV`, `AIFF`, `M4A`, `MKA` (with chapters), `AAC`,
`OGG`, `Opus`, `WMA`, and CUE sheets that reference PCM sources. PCM decoding
uses FFmpeg, discovered from the system installation on every platform.

## Requirements

The desktop interface bundles **Noto Serif SC** and **Noto Serif Devanagari**
under the SIL Open Font License 1.1, so it renders the same on every system
without installing fonts. The fonts are embedded in the application and add
about 36 MB to the download.

### Windows player

- Windows 10 or Windows 11, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for building
- FFmpeg and FFprobe available on `PATH`
- For cwASIO: Visual Studio 2022 with the **Desktop development with C++**
  workload and an installed ASIO driver
- Optional Steinberg ASIO SDK 2.3

The MIT-licensed cwASIO sources are included under `third_party/cwasio`, so ASIO
works without the Steinberg SDK. The Steinberg SDK itself is not part of this
repository.

### Linux player

- Linux x86-64 or ARM64 with an X11 or Wayland desktop
- FFmpeg and FFprobe available on `PATH`
- ALSA runtime (`libasound.so.2`) for direct PCM, native DSD, and DoP output
- OpenAL runtime (`libopenal.so.1`) for the system-routed output profile
- A DAC/ALSA endpoint advertising `DSD_U32_BE` for native DSD, or a DoP-capable
  DAC for fallback playback

The Linux player does not build or load the Windows ASIO bridges. Native DSF and
uncompressed DFF output is sent directly through ALSA.

### macOS player

- macOS 10.15 or newer on Apple Silicon (`arm64`) or Intel (`x64`)
- FFmpeg and FFprobe from `PATH` or common package-manager locations
- The system OpenAL framework for PCM output
- No ASIO, cwASIO, ALSA, or native DSD support

Release builds provide self-contained PKG installers and portable app bundles.
Development builds require the .NET 10 SDK.

## Build from source

Clone the repository and build the desktop client:

```bash
git clone https://github.com/wuuwuu26/HyperFidel.git
cd HyperFidel
dotnet restore HyperFidel/HyperFidel.csproj
dotnet build HyperFidel/HyperFidel.csproj --configuration Release
dotnet run --project HyperFidel/HyperFidel.csproj
```

Create a self-contained Linux build:

```bash
dotnet restore HyperFidel/HyperFidel.csproj --runtime linux-x64
dotnet publish HyperFidel/HyperFidel.csproj --configuration Release \
  --runtime linux-x64 --self-contained true --no-restore \
  --output artifacts/HyperFidel-linux-x64
```

The same pattern works for `win-x64`, `osx-x64`, and `osx-arm64`. Windows also
provides `build.ps1`, which builds the native cwASIO bridge, optionally builds
the Steinberg bridge, and compiles the managed application. See the
[full reference](docs/FULL-FEATURE-REFERENCE.md) for SDK discovery and native
bridge options.

Run the managed tests:

```bash
dotnet test HyperFidel.Core.Tests/HyperFidel.Core.Tests.csproj
dotnet test HyperFidel.Tests/HyperFidel.Tests.csproj
```

## Data and privacy

HyperFidel stores player data under `%LOCALAPPDATA%\HyperFidel\` on Windows and the
operating system's local application-data directory (normally
`$HOME/.local/share/HyperFidel/`) on Linux and macOS. Set `HYPERFIDEL_DATA_DIR` to use
a different root.

- `settings.json` contains non-secret preferences and may retain dormant entries
  from earlier remote features; no credentials are written to it.
- `library.db`, `artworks/`, `search-index/`, `waveforms/`, and the other cache
  folders hold the managed library, artwork, search data, and playback state.

Audio files are never rewritten by metadata edits, favorite/rating changes, or
library scans. The library export creates a ZIP of managed data without audio
files. There is no telemetry, and credentials are never written to
`settings.json`, logs, or documentation.

## Documentation

- [GitHub Wiki](https://github.com/wuuwuu26/HyperFidel/wiki) — end-user guides
- [Full feature and technical reference](docs/FULL-FEATURE-REFERENCE.md) — every
  feature, output profile, format behavior, and build detail
- [CHANGELOG.md](CHANGELOG.md) — shipped changes and current work
- [ROADMAP.md](ROADMAP.md) — historical implementation milestones
- [docs/WORKFLOW-TEST-COVERAGE.md](docs/WORKFLOW-TEST-COVERAGE.md) — automated
  workflow evidence and scope

## Known limitations

- Direct ALSA takes exclusive ownership of the selected card. While HyperFidel is
  playing, the matching PipeWire/PulseAudio profile is disabled so other
  applications cannot use that card; the profile is restored when playback is
  released. Volume keys are mirrored to the player through the system mixer.
- DST-compressed DFF is not supported for direct DSD output. Uncompressed
  stereo DFF and DSF are supported.
- macOS builds use OpenAL for PCM and do not provide ASIO, cwASIO, ALSA, or
  native DSD output.
- Windows Kernel Streaming is selectable but not implemented as a playback
  backend.

## License and attribution

HyperFidel is licensed under the [Apache License, Version 2.0](LICENSE). The
[NOTICE](NOTICE) file records the HyperFidel copyright, the retained Orynivo
notice, and the trademark statement for the HyperFidel name and logo.

HyperFidel is an independent derivative of
[Orynivo](https://github.com/bschlaack/Orynivo), Copyright 2026 Björn Schlaack,
which is also licensed under Apache-2.0. The original author and project are
credited in NOTICE; the Orynivo name and logo remain that project's assets.

Third-party components and their licenses are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the `licenses/` directory.
