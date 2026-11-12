---
id: T25
title: "Align every output in one step at start, and keep seek confirmation and correction alive when a device hangs"
layer: "app"
deps: ["T23"]
blocks: []
acs: ["AC-01", "AC-06", "AC-09"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineTransportTests.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs", "tests/DualPlayer.Core.Tests/Fakes/FakeAudioDevice.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F5, F7, F20)"
status: "todo"
step: "—"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T25 — Align every output in one step at start, and keep seek confirmation and correction alive when a device hangs

## Why (review findings)

### F5 — stage-2 (correctness), medium

> `StartCore` never calls `BeginAlignment`; any start offset > ~10.5 ms is corrected only at the ~2 s record, breaking the ≤ 1 s recovery target and inflating steady-state drift / excursions.
>
> **Where:** `src/DualPlayer.Core/Playback/AudioEngine.cs:166-183,547-551,571-578,614`
>
> **Suggested:** Call `BeginAlignment` for every output at start; add a fake device with start latency.
>
> — `_review/review-2026-11-12.md, F5, verbatim`

### F7 — stage-2 (correctness), medium

> `WaitForSeek` waits on every active output; a hung/lost device blocks the control loop 10 s, and on failure `Polling` is never re-enabled so all outputs lose rate/step correction for the rest of the run.
>
> **Where:** `src/DualPlayer.Core/Playback/AudioEngine.cs:290-294,307,325-340,596-600,614`
>
> **Suggested:** Exclude stalled outputs from confirmation; on any seek failure end alignment so correction resumes.
>
> — `_review/review-2026-11-12.md, F7, verbatim`

### F20 — stage-2, low

> `Pump.Seek` runs before `BeginEpoch`, so new-epoch frames can be consumed as old content after EOF, a hidden offset up to one device period.
>
> **Where:** `src/DualPlayer.Core/Playback/AudioEngine.cs:273,278`
>
> **Suggested:** Call `BeginEpoch` on every source before `Pump.Seek`.
>
> — `_review/review-2026-11-12.md, F20, verbatim`

## Acceptance criteria

> ### AC-01 (US-01) — happy path
> 
> **Given** the Developer has a media file with at least two tracks and two connected devices
> **When** the Developer starts playback with one track assigned to the first device and another track assigned to the second device
> **Then** both audio outputs start together from the beginning of the file, each Viewer hears the assigned track, and the console shows for every output its track, its device and the current master-clock position. The same track may be assigned to more than one audio output; only a device is exclusive (AC-05)
>
> — `spec.md §5, AC-01, verbatim`

> ### AC-06 (US-05) — domain invariant
> 
> **Given** two audio outputs are playing
> **When** the second output's device disappears (switched off, unplugged, out of range)
> **Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically. With three or more outputs, each further loss behaves the same way
>
> — `spec.md §5, AC-06, verbatim`

> ### AC-09 (US-04) — happy path
> 
> **Given** two audio outputs are playing
> **When** the Developer seeks 10 or 60 seconds forward or backward
> **Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console; the console prints that confirmation only after every output has discarded the audio it had queued from the old position
>
> — `spec.md §5, AC-09, verbatim`

## Hard rules (unchanged)

- Real-time code (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws — `sad.md §8`.
- No `DateTime.Now` / `Stopwatch` in Core; time from `TimeProvider` or the master clock. Expected failures return `Result` / domain events, never thrown across module boundaries — repo `CLAUDE.md`.
- Never weaken an existing test to make it pass; update a test only where it encoded the bug named above.

## Definition of Done

- [ ] with a fake device that starts reporting position 50 ms late, every output is within 10 ms of the master clock ≤ 1 s after start (engine-level test); a seek with one hung output confirms on the rendering outputs without blocking 10 s and the surviving outputs keep rate correction afterwards; `BeginEpoch` runs on every source before `Pump.Seek`
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
