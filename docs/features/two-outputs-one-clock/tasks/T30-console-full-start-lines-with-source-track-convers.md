---
id: T30
title: "Console: full start lines with source track, conversion and start-to-audio, and the FFmpeg version and license in the log"
layer: "ports"
deps: ["T24", "T29"]
blocks: []
acs: ["AC-01", "AC-12"]
files_hint: ["src/DualPlayer.Console/Commands/PlayCommand.cs", "src/DualPlayer.Core/Playback/EngineEvents.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Media/Audio/MixFormatConverter.cs", "src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs", "src/DualPlayer.Media/Audio/FfmpegLibrary.cs", "src/DualPlayer.Console/Program.cs", "tests/DualPlayer.Console.Tests/ListAndPlayCommandTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F12, F25)"
status: "todo"
step: "—"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T30 — Console: full start lines with source track, conversion and start-to-audio, and the FFmpeg version and license in the log

## Why (review findings)

### F12 — stage-1, low

> `play` start lines print `track 1 (-> stereo 48000)` instead of `track 1 (ukr 5.1 -> stereo, LFE dropped)` and omit `start-to-audio`; `MixFormatConverter.Description` is unused; tests only use `Contains`.
>
> **Where:** `src/DualPlayer.Console/Commands/PlayCommand.cs:32-36`, `src/DualPlayer.Media/Audio/MixFormatConverter.cs:22,57-58`
>
> **Suggested:** Carry source track info + start-to-audio into the events; assert the exact line format.
>
> — `_review/review-2026-11-12.md, F12, verbatim`

### F25 — stage-1, low

> FFmpeg version and license are never logged; `FfmpegInfo` from `FfmpegLibrary.Load` is discarded — T22 evidence has no LGPL proof.
>
> **Where:** `src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs:15,52`, `src/DualPlayer.Console/Program.cs:14-57`
>
> **Suggested:** Log `FfmpegInfo` once at console start; warn if not LGPL.
>
> — `_review/review-2026-11-12.md, F25, verbatim`

## Acceptance criteria

> ### AC-01 (US-01) — happy path
> 
> **Given** the Developer has a media file with at least two tracks and two connected devices
> **When** the Developer starts playback with one track assigned to the first device and another track assigned to the second device
> **Then** both audio outputs start together from the beginning of the file, each Viewer hears the assigned track, and the console shows for every output its track, its device and the current master-clock position. The same track may be assigned to more than one audio output; only a device is exclusive (AC-05)
>
> — `spec.md §5, AC-01, verbatim`

> ### AC-12 (US-07) — cross-context
> 
> **Given** a track's channel layout or sample rate differs from what its device plays natively (for example a 5.1 track on stereo headphones, or a 44.1 kHz track on a 48 kHz device)
> **When** the Developer plays that track on that device
> **Then** the Viewer hears all channels except the low-frequency effects (LFE, ".1") channel folded into the device's layout at the correct pitch and speed, and the output still meets the drift tolerance (§6); a track with fewer channels than the device plays on the device's front channels only (a mono track on both front channels), with no upmix
>
> — `spec.md §5, AC-12, verbatim`

## Hard rules (unchanged)

- Real-time code (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws — `sad.md §8`.
- No `DateTime.Now` / `Stopwatch` in Core; time from `TimeProvider` or the master clock. Expected failures return `Result` / domain events, never thrown across module boundaries — repo `CLAUDE.md`.
- Never weaken an existing test to make it pass; update a test only where it encoded the bug named above.

## Definition of Done

- [ ] the `play` start lines match cli.md §1.3 exactly (e.g. `track 1 (ukr 5.1 -> stereo, LFE dropped)` and `started   clock … start-to-audio 0.8 s`) asserted by exact-format tests; the console logs the FFmpeg version and license once at start and warns when the license is not LGPL
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
