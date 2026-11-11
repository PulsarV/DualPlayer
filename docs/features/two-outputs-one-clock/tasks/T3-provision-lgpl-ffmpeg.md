---
id: T3
title: "Provision LGPL FFmpeg for the dev Mac, CI and the Windows build"
layer: "wiring"
deps: []
blocks: ["T14"]
acs: ["AC-12"]
files_hint: ["build/fetch-ffmpeg.ps1", ".gitignore", "Directory.Packages.props", "src/DualPlayer.Media/DualPlayer.Media.csproj", "src/DualPlayer.Media/Audio/FfmpegLibrary.cs", "tests/DualPlayer.Media.Tests/", "DualPlayer.slnx", ".github/workflows/ci.yml"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "S"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T3 — Provision LGPL FFmpeg for the dev Mac, CI and the Windows build

## Place in the sequence

- **Blocked by:** nothing (wave 1 root)
- **Blocks:** T14 — List a file's audio tracks and decode the chosen ones once with FFmpeg
- **Wave:** 1 — no dependencies — starts immediately.
- **Lane:** shares `Directory.Packages.props` with T1 — serialized; shares `DualPlayer.slnx` with T2 — serialized; shares `tests/DualPlayer.Media.Tests/` with T14 — serialized; shares `tests/DualPlayer.Media.Tests/` with T15 — serialized; shares `Directory.Packages.props` with T16 — serialized; shares `DualPlayer.slnx` with T18 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to play a 5.1 or 7.1 track on a stereo device
> **So that** I can test the engine with real films, not only specially prepared files
>
> — `spec.md §4, US-07, verbatim` · full text: [spec.md](../spec.md)

This task makes FFmpeg loadable everywhere the engine runs so that the decode and conversion tasks (T14, T15) can be built and tested.

## Inlined context

> It is started from the repository with `dotnet run --project src/DualPlayer.Console` or as a `win-x64` build output; no installer in this step (roadmap step 13). FFmpeg's LGPL shared libraries are fetched on Windows by `build/fetch-ffmpeg.ps1`, which downloads one pinned LGPL win-x64 shared build, checks its SHA-256 and unpacks it into a git-ignored folder; the console project copies the DLLs beside its executable. The pinned FFmpeg major version matches the FFmpeg.AutoGen package version. On the dev Mac FFmpeg comes from Homebrew.
>
> — `sad.md §7, Deployment view, abridged` · full text: [sad.md](../sad.md)

> FFmpeg binary and FFmpeg.AutoGen versions drift apart, or a GPL build slips in — Mitigation: `build/fetch-ffmpeg.ps1` pins one LGPL shared build with SHA-256; FFmpeg major version pinned to the FFmpeg.AutoGen package; console logs FFmpeg version and license at start
>
> — `sad.md §11, Risks row 6, abridged` · full text: [sad.md](../sad.md)

> **Hard rule:** libmpv and FFmpeg are LGPL: dynamic linking only (`libmpv-2.dll` beside the exe); FFmpeg builds must be LGPL. Native libs on the dev Mac (libmpv, FFmpeg) are installed outside the repo (Homebrew); on Windows they ship in the package.
>
> — `CLAUDE.md §Constraints, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> - One converter per output, so a track on two devices is converted twice.
> - Fold-down correctness is verified by tests in `DualPlayer.Media` that need FFmpeg installed (Homebrew on the dev Mac, shipped DLLs on Windows).
>
> — `adr/0005 §Consequences, Negative, verbatim` · full text: [ADR-0005](../adr/0005-convert-tracks-to-the-device-mix-format-with-ffmpeg-swresample-in-media.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-12 — cross-context

> **Given** a track's channel layout or sample rate differs from what its device plays natively (for example a 5.1 track on stereo headphones, or a 44.1 kHz track on a 48 kHz device)
> **When** the Developer plays that track on that device
> **Then** the Viewer hears all channels except the low-frequency effects (LFE, ".1") channel folded into the device's layout at the correct pitch and speed, and the output still meets the drift tolerance (§6); a track with fewer channels than the device plays on the device's front channels only (a mono track on both front channels), with no upmix
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Pin `FFmpeg.AutoGen` in `Directory.Packages.props` and reference it from `src/DualPlayer.Media/DualPlayer.Media.csproj`
- [ ] `build/fetch-ffmpeg.ps1`: download one pinned LGPL win-x64 **shared** build whose major version matches FFmpeg.AutoGen, verify SHA-256, unpack into a git-ignored folder (add it to `.gitignore`)
- [ ] `FfmpegLibrary` — resolves the native root (beside the executable on Windows, Homebrew lib dir on macOS), loads it and returns `Result<FfmpegInfo>` with version and license string; a missing library is a failure `Result`, not an exception — `src/DualPlayer.Media/Audio/FfmpegLibrary.cs`
- [ ] New `tests/DualPlayer.Media.Tests/` project (in `DualPlayer.slnx`) with a test that loads FFmpeg and checks the major version matches FFmpeg.AutoGen
- [ ] CI: install FFmpeg before tests — `brew install ffmpeg` on `macos-latest`, `build/fetch-ffmpeg.ps1` on `windows-latest` — `.github/workflows/ci.yml`

## Edge cases

| Case | Behaviour |
|---|---|
| SHA-256 mismatch in the fetch script | script fails, nothing unpacked |
| FFmpeg not installed on the machine | `FfmpegLibrary` returns a failure `Result` naming the expected location |
| Homebrew FFmpeg on the dev Mac is not an LGPL build | allowed — it is never shipped; version and license are logged, only the Windows fetch is pinned to LGPL |

## Definition of Done

- [ ] `FfmpegLibraryTests` pass locally on the dev Mac and in both CI jobs
- [ ] `build/fetch-ffmpeg.ps1` downloads, verifies and unpacks the pinned LGPL build into the git-ignored folder
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
