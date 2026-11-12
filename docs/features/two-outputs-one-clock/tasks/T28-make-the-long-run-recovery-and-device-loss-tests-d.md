---
id: T28
title: "Make the long-run, recovery and device-loss tests drive the real engine with realistic fakes"
layer: "app"
deps: ["T23", "T24", "T25", "T26", "T27"]
blocks: []
acs: ["AC-06", "AC-07", "AC-09"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/LongRunSimulationTests.cs", "tests/DualPlayer.Core.Tests/Sync/DriftControllerTests.cs", "src/DualPlayer.Core/Sync/DriftController.cs", "tests/DualPlayer.Core.Tests/Fakes/", "tests/DualPlayer.Core.Tests/Playback/AudioEngineSyncRecordTests.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F14, F16)"
status: "todo"
step: "—"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T28 — Make the long-run, recovery and device-loss tests drive the real engine with realistic fakes

## Why (review findings)

### F14 — stage-1 (test adequacy), medium

> The 3 h test drives `OutputSync` directly at 10 ms ticks, not the engine; recovery tests exercise `DriftController.PendingRealignment`, which no production code calls; fakes (lockstep equal blocks, exact seeks, zero start latency) hide F3–F6.
>
> **Where:** `tests/DualPlayer.Core.Tests/Playback/LongRunSimulationTests.cs:16-31`, `tests/DualPlayer.Core.Tests/Sync/DriftControllerTests.cs:100,244-293`, `src/DualPlayer.Core/Sync/DriftController.cs:45`, `tests/DualPlayer.Core.Tests/Fakes/FakeAudioDecoder.cs:41-66`, `tests/DualPlayer.Core.Tests/Fakes/FakeAudioDevice.cs:34-38`
>
> **Suggested:** Run the long-run and recovery tests through `AudioEngine` (±200 ppm); delete `PendingRealignment`; extend fakes (mismatched block sizes/rates, early-landing seek, start latency).
>
> — `_review/review-2026-11-12.md, F14, verbatim`

### F16 — stage-1 (test adequacy), low

> Test-plan row 'each further loss with three outputs' is not covered.
>
> **Where:** `tests/DualPlayer.Core.Tests/Playback/AudioEngineSyncRecordTests.cs:247`, `tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs:297`
>
> **Suggested:** Three outputs, lose two in turn, assert the third keeps 0 underruns.
>
> — `_review/review-2026-11-12.md, F16, verbatim`

### Flake observed during implement (2026-11-12)

> `AudioEngineSyncRecordTests.An_injected_step_error_is_re_aligned_in_one_step_counted_once_and_recorded_as_one_excursion` failed once in ~15 full-suite runs at line 295 (`dev-2 Corrections.ShouldBe(0)`), passing on every rerun. It waits on the real-time Rig loop — make it deterministic while moving these tests onto the realistic engine fakes.
>
> — implement run, T25 escalation report

## Acceptance criteria

> ### AC-06 (US-05) — domain invariant
> 
> **Given** two audio outputs are playing
> **When** the second output's device disappears (switched off, unplugged, out of range)
> **Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically. With three or more outputs, each further loss behaves the same way
>
> — `spec.md §5, AC-06, verbatim`

> ### AC-07 (US-03) — happy path (long run)
> 
> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim`

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

- [ ] the 3 h simulation runs through `AudioEngine` with fake devices at ±200 ppm on `FakeTimeProvider` at the engine's real sampling cadence and asserts max steady-state drift ≤ 10 ms and 0 underruns; recovery-after-start/resume/seek tests go through the engine; `DriftController.PendingRealignment` is removed if unused; fakes support mismatched block sizes, per-output rates, start latency and an early-landing seek; a three-output test loses two devices in turn and the third keeps 0 underruns
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
