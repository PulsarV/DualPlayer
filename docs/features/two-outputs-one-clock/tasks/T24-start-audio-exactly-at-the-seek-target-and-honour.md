---
id: T24
title: "Start audio exactly at the seek target and honour block start positions from the decoder"
layer: "infra"
deps: ["T23", "T27"]
blocks: []
acs: ["AC-09", "AC-01"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs", "src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs", "src/DualPlayer.Core/Playback/DecodePump.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "tests/DualPlayer.Media.Tests/Audio/FfmpegAudioDecoderTests.cs", "tests/DualPlayer.Media.Tests/TestMedia/", "tests/DualPlayer.Core.Tests/Playback/DecodePumpTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F4)"
status: "todo"
step: "—"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T24 — Start audio exactly at the seek target and honour block start positions from the decoder

## Why (review findings)

### F4 — stage-2 (correctness), high

> Seek uses `av_seek_frame(-1, …, BACKWARD)` → lands on the video keyframe; decoded audio is never trimmed by pts while `_startFrame = target × rate`, so audio after seek can be seconds off and still reported in sync. Track start pts / skipped packets ignored too.
>
> **Where:** `src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs:53-54,198-211,280-286`, `src/DualPlayer.Core/Playback/AudioEngine.cs:277-278`
>
> **Suggested:** Discard samples with pts before the target (or seek the audio stream and trim); derive block `StartFrame` from pts and honour it in Core; add a position-checking seek test.
>
> — `_review/review-2026-11-12.md, F4, verbatim`

## Acceptance criteria

> ### AC-09 (US-04) — happy path
> 
> **Given** two audio outputs are playing
> **When** the Developer seeks 10 or 60 seconds forward or backward
> **Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console; the console prints that confirmation only after every output has discarded the audio it had queued from the old position
>
> — `spec.md §5, AC-09, verbatim`

> ### AC-01 (US-01) — happy path
> 
> **Given** the Developer has a media file with at least two tracks and two connected devices
> **When** the Developer starts playback with one track assigned to the first device and another track assigned to the second device
> **Then** both audio outputs start together from the beginning of the file, each Viewer hears the assigned track, and the console shows for every output its track, its device and the current master-clock position. The same track may be assigned to more than one audio output; only a device is exclusive (AC-05)
>
> — `spec.md §5, AC-01, verbatim`

## Hard rules (unchanged)

- Real-time code (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws — `sad.md §8`.
- No `DateTime.Now` / `Stopwatch` in Core; time from `TimeProvider` or the master clock. Expected failures return `Result` / domain events, never thrown across module boundaries — repo `CLAUDE.md`.
- Never weaken an existing test to make it pass; update a test only where it encoded the bug named above.

## Definition of Done

- [ ] a Media test on a file whose video GOP is longer than the seek distance asserts the first decoded audio after `seek` starts at the target (± 1 audio frame duration), not at the keyframe; `StartFrame` of each block is derived from pts; Core detects a gap/overlap between consecutive blocks and keeps content position true (unit test)
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
