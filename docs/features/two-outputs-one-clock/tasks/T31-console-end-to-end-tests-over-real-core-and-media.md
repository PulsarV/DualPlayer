---
id: T31
title: "Console end-to-end tests over real Core and Media with a fake backend, and a smoke test that resolves the play path"
layer: "ports"
deps: ["T29", "T30"]
blocks: []
acs: ["AC-01", "AC-02", "AC-03", "AC-05", "AC-08", "AC-09", "AC-10", "AC-13"]
files_hint: ["tests/DualPlayer.Console.Tests/E2E/", "tests/DualPlayer.Console.Tests/ConsoleCompositionRootSmokeTests.cs", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F15, F27)"
status: "todo"
step: "—"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T31 — Console end-to-end tests over real Core and Media with a fake backend, and a smoke test that resolves the play path

## Why (review findings)

### F15 — stage-1 (test adequacy), medium

> No Console test drives real Core + Media with a fake backend; all Console tests substitute `IAudioEngine`.
>
> **Where:** `tests/DualPlayer.Console.Tests/`
>
> **Suggested:** Add an E2E class: `PlayCommand` + real `AudioEngine` + `FakeAudioBackend` + real `FfmpegAudioDecoder` on `TestMedia/two-tracks.mkv`, scripted stdin.
>
> — `_review/review-2026-11-12.md, F15, verbatim`

### F27 — stage-2, low

> Console smoke test resolves only `IStateStore`/`AppPaths`, not the play path.
>
> **Where:** `tests/DualPlayer.Console.Tests/ConsoleCompositionRootSmokeTests.cs:12-20`
>
> **Suggested:** Resolve `PlayCommand`, `DevicesCommand`, `TracksCommand`, `IAudioEngine`.
>
> — `_review/review-2026-11-12.md, F27, verbatim`

## Acceptance criteria

> ### AC-01 (US-01) — happy path
> 
> **Given** the Developer has a media file with at least two tracks and two connected devices
> **When** the Developer starts playback with one track assigned to the first device and another track assigned to the second device
> **Then** both audio outputs start together from the beginning of the file, each Viewer hears the assigned track, and the console shows for every output its track, its device and the current master-clock position. The same track may be assigned to more than one audio output; only a device is exclusive (AC-05)
>
> — `spec.md §5, AC-01, verbatim`

> ### AC-02 (US-02) — happy path
> 
> **Given** devices are connected and the Developer has a media file
> **When** the Developer asks for the device list and the track list
> **Then** the console shows every playback device the operating system currently offers (name, device type when known, device id) and every audio track of the file (number, language, channel layout), and nothing starts playing. Tracks are numbered from 1 among the file's audio tracks only; the Developer names a device either by its number in this list or by its full operating-system id, and when playback starts the console echoes the id each output resolved to
>
> — `spec.md §5, AC-02, verbatim`

> ### AC-03 (US-01) — error
> 
> **Given** the Developer names a file that cannot be opened as media, a track number the file does not have, or a device that is not connected
> **When** the Developer starts playback
> **Then** no audio output starts and the console names the invalid input and the reason in plain language (for example: track 3 does not exist, the file has tracks 1 and 2)
>
> — `spec.md §5, AC-03, verbatim`

> ### AC-05 (US-01) — domain invariant
> 
> **Given** the Developer assigns the same device to two audio outputs
> **When** the Developer starts playback
> **Then** the system refuses to start and names the rule "one audio output per device"
>
> — `spec.md §5, AC-05, verbatim`

> ### AC-08 (US-04) — happy path
> 
> **Given** two audio outputs are playing
> **When** the Developer pauses and later resumes playback
> **Then** on pause (and on stop) all outputs fall silent within 10 ms of each other, judged by each device's reported playback position, and the master clock stops; on resume all outputs continue from the same master-clock position and are back within the drift tolerance within the recovery time (§6)
>
> — `spec.md §5, AC-08, verbatim`

> ### AC-09 (US-04) — happy path
> 
> **Given** two audio outputs are playing
> **When** the Developer seeks 10 or 60 seconds forward or backward
> **Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console; the console prints that confirmation only after every output has discarded the audio it had queued from the old position
>
> — `spec.md §5, AC-09, verbatim`

> ### AC-10 (US-04) — error
> 
> **Given** two audio outputs are playing
> **When** the Developer seeks to a point before the start or past the end of the file
> **Then** a seek before the start plays from the beginning; a seek past the end moves the position to the end of the file at once, all outputs fall silent and the run ends with the run summary (AC-13); in both cases the console tells the Developer the target was outside the file
>
> — `spec.md §5, AC-10, verbatim`

> ### AC-13 (US-08) — happy path
> 
> **Given** a run has been playing
> **When** playback stops because the file ended, the Developer gave the stop command, pressed Ctrl+C, or closed the console window (a crash or a killed process need not produce a summary; the AC-11 records up to that point stay in the log)
> **Then** the console prints and the log records, for every audio output: its device, played duration, largest steady-state drift, estimated device clock mismatch in parts per million, number of corrections, number of underruns and any device-lost events. The clock mismatch is the device clock's rate relative to the master clock; a correction (CONTEXT) is counted only when the output's position is re-aligned in one step, so a steady run is expected to count 0, while the continuous rate adjustment is what AC-11 reports as current correction, in parts per million
>
> — `spec.md §5, AC-13, verbatim`

## Hard rules (unchanged)

- Real-time code (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws — `sad.md §8`.
- No `DateTime.Now` / `Stopwatch` in Core; time from `TimeProvider` or the master clock. Expected failures return `Result` / domain events, never thrown across module boundaries — repo `CLAUDE.md`.
- Never weaken an existing test to make it pass; update a test only where it encoded the bug named above.

## Definition of Done

- [ ] an E2E test class drives `play` through the console entry point (arguments + scripted stdin) with real `AudioEngine`, real `FfmpegAudioDecoder` on `TestMedia`, and a fake audio backend, covering the test-plan E2E rows for AC-01/02/03/05/08/09/10/13; the console smoke test resolves `PlayCommand`, `DevicesCommand`, `TracksCommand` and `IAudioEngine`
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
