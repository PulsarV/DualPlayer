---
id: T9
title: "Probe a file's picture with FFmpeg in hardware, off the caller's thread, with a time limit"
layer: "infra"
deps: ["T5"]
blocks: ["T17", "T19"]
acs: ["AC-02", "AC-03", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/IPictureProbe.cs", "src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "src/DualPlayer.Media/MediaServiceCollectionExtensions.cs", "tests/DualPlayer.Core.Tests/Fakes/FakePictureProbe.cs", "tests/DualPlayer.Media.Tests/Video/FfmpegPictureProbeTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T9 — Probe a file's picture with FFmpeg in hardware, off the caller's thread, with a time limit

## Place in the sequence

- **Blocked by:** T5 — Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict
- **Blocks:** T17 — Move to the next or previous playable file in the folder, skipping files that cannot play, T19 — Parse and validate the play start command in the App executable before any window opens
- **Wave:** 4 — after its deepest dependency finishes (wave 3).
- **Lane:** shares `src/DualPlayer.Media/MediaServiceCollectionExtensions.cs` with T4 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task decides before any window opens — and before a file change touches the playing film — whether a file has a picture this PC can decode in hardware.

## Inlined context

> **FFmpeg probe, with mpv as a backstop.** `IPictureProbe` in Media opens the file, finds the video stream, creates a D3D11VA hardware device and decodes the first frame in hardware, with no window. If mpv later reports that hardware decoding is not active (`hwdec-current` = no), that is treated as a picture decode failure (AC-06b path, or a skip during next/previous).
>
> — `adr/0007 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0007](../adr/0007-probe-the-picture-with-ffmpeg-before-the-window-opens.md)

> The probe runs off the UI thread with a time limit, so a hostile file cannot hang the start (spec §6.1).
>
> — `adr/0007 §Consequences, positive, verbatim` · full text: [ADR-0007](../adr/0007-probe-the-picture-with-ffmpeg-before-the-window-opens.md)

> On the dev Mac the probe uses VideoToolbox in place of D3D11VA; the dev path is not held to spec §6 targets.
>
> — `adr/0007 §Consequences, neutral, verbatim` · full text: [ADR-0007](../adr/0007-probe-the-picture-with-ffmpeg-before-the-window-opens.md)

> A malformed or hostile video file: if it cannot be read, or its first picture cannot be decoded in hardware within the start budget (§6), the player refuses it at start with a plain explanation (AC-02), or skips it on next or previous (AC-09b); damage found after playback has started is handled as in AC-06b. The player never crashes, and no key goes unanswered for longer than 3 s.
>
> — `spec.md §6.1, abuse case malformed file, verbatim` · full text: [spec.md](../spec.md)

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
| `playback.file_access_denied` | The OS denied the App read access to the file (AC-03) | 4 | new |
| `playback.file_unreadable` | The file cannot be opened as media | 2 | exists |
| `video.no_picture` | The file has no video stream (AC-02) | 2 | new |
| `video.not_hardware_decodable` | The picture's format (codec + profile named in the message) cannot be decoded in hardware on this PC, or its first picture did not decode within the start budget (AC-02, spec §6.1) | 2 | new |

— `contracts/cli.md §5, error codes, probe rows, verbatim` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

### AC-02 — error

> **Given** a start command whose file has no picture, or whose picture the PC cannot decode
> **When** the start command is given
> **Then** nothing plays, no window stays open, and the Developer is told in plain words that the file has no picture or that its picture format cannot be played on this PC, naming the format; a picture this PC can decode only in software counts as "cannot decode" in this step (software decoding is roadmap step 6)
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — authorization

> **Given** a start command naming a file the operating system does not let the player read
> **When** the start command is given
> **Then** nothing plays and the Developer is told that the operating system denied the player access to that file, so they can fix the permission and retry
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

### AC-09b — error

> **Given** the neighbouring file cannot be read, has no picture, or has a picture this PC cannot decode in hardware (as in AC-02)
> **When** a Viewer presses the next-file or previous-file key
> **Then** that file is skipped in the same way as in AC-09, the window shows briefly which file was skipped and why in plain words, and the Developer's log names the file and the reason
>
> — `spec.md §5, AC-09b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `IPictureProbe.Probe(file, timeLimit)` → `Result<PictureInfo>` (codec, profile, size) — `src/DualPlayer.Core/Playback/IPictureProbe.cs`
- [ ] `FfmpegPictureProbe`: open input (map an OS access refusal to `playback.file_access_denied`), find the video stream, create the D3D11VA (Windows) / VideoToolbox (macOS) device, decode the first frame with no software fallback; run on a worker with the time limit — `src/DualPlayer.Media/Video/FfmpegPictureProbe.cs`
- [ ] Register in `AddMedia` — `src/DualPlayer.Media/MediaServiceCollectionExtensions.cs`
- [ ] `FakePictureProbe` with per-file scripted outcomes — `tests/DualPlayer.Core.Tests/Fakes/FakePictureProbe.cs`
- [ ] Tests on samples generated with FFmpeg into a temp dir — `tests/DualPlayer.Media.Tests/Video/FfmpegPictureProbeTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Picture decodable only in software | `video.not_hardware_decodable` (AC-02: counts as «cannot decode») |
| First frame not decoded within the limit (hostile file) | `video.not_hardware_decodable`; the probe never hangs the caller (§6.1) |
| No read permission | `playback.file_access_denied` — distinct from unreadable (AC-03) |
| Probe crashes inside native code | never — any native error maps to a `Result` error |

## Definition of Done

- [ ] `FfmpegPictureProbeTests` pass for each error code and the time limit
- [ ] the message for `video.not_hardware_decodable` names codec and profile
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
