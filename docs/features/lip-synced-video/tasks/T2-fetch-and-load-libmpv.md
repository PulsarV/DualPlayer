---
id: T2
title: "Fetch a pinned libmpv, copy it beside the executables, and load it with a version check"
layer: "infra"
deps: []
blocks: ["T4"]
acs: ["AC-01"]
files_hint: ["build/fetch-libmpv.ps1", ".gitignore", ".github/workflows/ci.yml", "src/DualPlayer.Media/DualPlayer.Media.csproj", "src/DualPlayer.Media/Video/MpvLibrary.cs", "tests/DualPlayer.Media.Tests/Video/MpvLibraryTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "S"
status: "blocked"
step: "GATE — full dotnet test has 46 FFmpeg failures (env: brew install ffmpeg@8 && brew link --force ffmpeg@8); task tests, build, lint green"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T2 — Fetch a pinned libmpv, copy it beside the executables, and load it with a version check

## Place in the sequence

- **Blocked by:** — (wave-1 root)
- **Blocks:** T4 — Implement the hardened libmpv video renderer with presented-position snapshots and OSD notes
- **Wave:** 1 — starts at once — no dependencies.
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task makes the video renderer's native library available and loadable on both OSes, so a missing or wrong libmpv is a plain start error rather than a crash.

## Inlined context

> Native libraries sit beside the executable: the LGPL FFmpeg DLLs fetched by `build/fetch-ffmpeg.ps1` (existing) and `libmpv-2.dll` fetched by a new `build/fetch-libmpv.ps1` on the same pattern (pinned version and checksum, `build/mpv/` git-ignored, cached in CI, copied beside every referencing executable by `DualPlayer.Media.csproj`). For this step any prebuilt libmpv is acceptable for development and testing on the owner's PCs, where nothing is distributed. An LGPL build (`-Dgpl=false`) replaces it before roadmap step 13 packages the player (§11). On the dev Mac, libmpv and FFmpeg come from Homebrew (`brew install mpv ffmpeg@8`); CI's macOS job installs both.
>
> — `sad.md §7, deployment, abridged` · full text: [sad.md](../sad.md)

> `MpvLibrary.cs` — libmpv loading (libmpv-2.dll / Homebrew libmpv), version check, Result errors
>
> — `sad.md §5, internal decomposition, Media/Video/MpvLibrary.cs, verbatim` · full text: [sad.md](../sad.md)

> | Prebuilt Windows libmpv builds are usually GPL; the project ships LGPL only | Medium | `build/fetch-libmpv.ps1` pins a build; an LGPL build (`-Dgpl=false`) replaces any GPL one before roadmap step 13 packages the player; no distribution until then (§7) | Volodymyr Kravchuk |
>
> — `sad.md §11, risk row «Prebuilt Windows libmpv builds», verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** FFmpeg 8 through FFmpeg.AutoGen 8.1.0 and libmpv (`libmpv-2.dll` beside the exe) — both LGPL: dynamic linking only, LGPL builds only. On the dev Mac both come from Homebrew, outside the repo; libmpv is not installed there yet.
>
> — `sad.md §2, Technical, native libraries, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** Expected runtime failures (file unreadable, no picture, device lost, decode error) travel as `Result` / `Result<T>` (`src/DualPlayer.Core/Result.cs`, dotted lowercase error codes) or a domain event — never thrown across module boundaries; exceptions mean bugs and are logged at the top of each background loop.
>
> — `sad.md §2, Conventions, errors, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

| Code | When | Exit | Status in repo |
|---|---|---|---|
| `mpv.not_found` / `mpv.load_failed` | The video renderer library is missing or the wrong version | 2 | new |

— `contracts/cli.md §5, error codes, row mpv.*, verbatim` · full text: [cli.md](../contracts/cli.md)

This task returns the codes as `Result` errors; printing them is T19/T21.

## Acceptance criteria

### AC-01 — happy path

> **Given** a video file with a picture and at least two audio tracks, and the Developer's start command names the file, a track and a device for each Viewer, and a screen
> **When** the start command is given
> **Then** the film opens fullscreen on the named screen (on the main screen when none is named), the picture and every output's sound start together within the start budget (§6), and each Viewer hears their own track; on a device whose offset was set in an earlier run (AC-12) the sound is in step with the lips within the lip-sync budget (§6), on a device's first use the error is only measured (§7)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Write `build/fetch-libmpv.ps1` on the `fetch-ffmpeg.ps1` pattern: pinned URL + SHA-256, unpack into `build/mpv/`, marker file, fail without unpacking on a hash mismatch — `build/fetch-libmpv.ps1`
- [ ] Git-ignore `build/mpv/` — `.gitignore`
- [ ] Copy `libmpv-2.dll` beside every referencing executable, as FFmpeg is copied today — `src/DualPlayer.Media/DualPlayer.Media.csproj`
- [ ] CI: fetch and cache libmpv on `windows-latest` keyed on the script hash; `brew install mpv` on `macos-latest` — `.github/workflows/ci.yml`
- [ ] `MpvLibrary`: resolve the library (beside the exe on Windows, Homebrew prefix on macOS), `NativeLibrary.TryLoad`, check `mpv_client_api_version`, return `Result` with `mpv.not_found` / `mpv.load_failed`; declare the minimal P/Invoke surface the renderer needs (create, initialize, set option/property, command, observe property, wait event, terminate) — `src/DualPlayer.Media/Video/MpvLibrary.cs`
- [ ] Tests for the missing-library and version paths with an injected path/version, plus a load test that runs where libmpv is present — `tests/DualPlayer.Media.Tests/Video/MpvLibraryTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| libmpv missing beside the exe / not installed via Homebrew | `mpv.not_found`, never an exception or a crash |
| libmpv present but its client API is older than required | `mpv.load_failed` naming the found version |
| SHA-256 mismatch in the fetch script | script fails and unpacks nothing |

## Definition of Done

- [ ] `MpvLibraryTests` pass on macOS (Homebrew libmpv) and on CI Windows (fetched DLL)
- [ ] `build/mpv/` is git-ignored; the DLL lands beside `DualPlayer.App` and test executables
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
