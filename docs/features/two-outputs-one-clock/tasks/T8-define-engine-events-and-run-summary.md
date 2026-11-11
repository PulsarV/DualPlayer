---
id: T8
title: "Define engine events, the excursion tracker and the run-summary builder"
layer: "domain"
deps: []
blocks: ["T11"]
acs: ["AC-07", "AC-11", "AC-13"]
files_hint: ["src/DualPlayer.Core/Diagnostics/EngineEvent.cs", "src/DualPlayer.Core/Diagnostics/ExcursionTracker.cs", "src/DualPlayer.Core/Diagnostics/RunSummaryBuilder.cs", "tests/DualPlayer.Core.Tests/Diagnostics/ExcursionTrackerTests.cs", "tests/DualPlayer.Core.Tests/Diagnostics/RunSummaryBuilderTests.cs"]
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

# T8 — Define engine events, the excursion tracker and the run-summary builder

## Place in the sequence

- **Blocked by:** nothing (wave 1 root)
- **Blocks:** T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss
- **Wave:** 1 — no dependencies — starts immediately.
- **Lane:** own lane.

## Why (user story)

> **As a** Developer
> **I want** drift, correction and underrun counters recorded separately for every device
> **So that** roadmap steps 4–6 and the crackling investigation work from data instead of guesses
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

> **As a** Developer
> **I want** a per-output summary of the run when playback stops
> **So that** I can record the target-PC measurements in the repository and close this step on evidence
>
> — `spec.md §4, US-08, verbatim` · full text: [spec.md](../spec.md)

This task defines what the engine tells the world — records, excursions, device loss, the run summary — and the bookkeeping behind the last two.

## Inlined context

> **Chosen:** `IObservable<EngineEvent>` published by the engine-control loop — render threads only update lock-free counters; once per second, timed by `TimeProvider`, the control loop snapshots every output, publishes one `SyncRecord` per device and writes the structured log entry; excursions, device-lost events and the run summary go through the same stream.
>
> — `adr/0007 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0007](../adr/0007-publish-engine-events-as-an-observable-stream-from-the-engine-control-loop.md)

> `EngineEvent` is a closed set of records in `Core/Diagnostics/`; adding an event kind later is additive for subscribers that ignore unknown kinds.
>
> — `adr/0007 §Consequences, Neutral, verbatim` · full text: [ADR-0007](../adr/0007-publish-engine-events-as-an-observable-stream-from-the-engine-control-loop.md)

> Excursion — A stretch of time during which an output is outside the 10 ms drift tolerance, recorded once with device, peak, duration and start position (spec AC-07)
>
> — `sad.md §12, Glossary, Excursion, verbatim` · full text: [sad.md](../sad.md)

> Correction — the adjustment the engine makes to bring an audio output back to the master clock: reported as the continuous playback-rate adjustment in parts per million, and counted only when the output's position is re-aligned in one step. NOT Drift (drift is the error; correction is the response to it).
>
> — `CONTEXT.md §Glossary, Correction, verbatim` · full text: [CONTEXT.md](../../../../CONTEXT.md)

**Breakdown reading:** AC-07 bounds the *steady-state* tolerance, so excursions are tracked outside the recovery windows (the 1 s after start, resume or seek), the same windows spec §6 excludes from the largest steady-state drift.

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

> `ended` is one of `file-end`, `stop`, `interrupt` (Ctrl+C or window closed), `seek-past-end`, `all-devices-lost`. `max_drift_ms` is the largest steady-state drift outside the recovery windows; `corrections` counts one-step re-alignments only.
> Record fields: `clock` (master-clock position), `device` (OS endpoint id), `drift_ms` (average drift over the record's 1 s interval), `correction_ppm` (current continuous rate adjustment), `underruns` (cumulative underruns of this device in the run). Excursion fields: `device`, `start`, `duration_ms`, `peak_ms`.
>
> — `contracts/cli.md §2.1, §2.2, §2.4, abridged` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

### AC-07 — happy path (long run)

> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

### AC-11 — cross-context

> **Given** a run is in progress on one or more devices
> **When** playback continues
> **Then** once per second the system publishes, for every device, a record of its current drift, current correction and cumulative underruns, tied to that device's operating-system id and the master-clock position; each record is both raised as an engine event that other parts of the player can subscribe to and written to the log, so roadmap step 6 (self-diagnostics) can subscribe to these records without any change to the engine
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

### AC-13 — happy path

> **Given** a run has been playing
> **When** playback stops because the file ended, the Developer gave the stop command, pressed Ctrl+C, or closed the console window (a crash or a killed process need not produce a summary; the AC-11 records up to that point stay in the log)
> **Then** the console prints and the log records, for every audio output: its device, played duration, largest steady-state drift, estimated device clock mismatch in parts per million, number of corrections, number of underruns and any device-lost events. The clock mismatch is the device clock's rate relative to the master clock; a correction (CONTEXT) is counted only when the output's position is re-aligned in one step, so a steady run is expected to count 0, while the continuous rate adjustment is what AC-11 reports as current correction, in parts per million
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Closed record set `EngineEvent` → `SyncRecord(DeviceId, ClockPosition, DriftMs, CorrectionPpm, Underruns)`, `Excursion(DeviceId, Start, Duration, PeakMs)`, `DeviceLost(OutputIndex, DeviceId, ClockPosition, OthersContinue)`, `RunSummary(EndReason, ClockPosition, Outputs)` — `src/DualPlayer.Core/Diagnostics/EngineEvent.cs`
- [ ] `RunEndReason`: `FileEnd`, `Stop`, `Interrupt`, `SeekPastEnd`, `AllDevicesLost`
- [ ] `ExcursionTracker` per device: fed each 1 s drift value + whether it is in a recovery window; opens past 10 ms, tracks peak, closes once back within tolerance and emits one `Excursion` — `ExcursionTracker.cs`
- [ ] `RunSummaryBuilder`: per output device, played duration, largest steady-state drift (recovery windows excluded), clock mismatch ppm, corrections, underruns, device-lost events — `RunSummaryBuilder.cs`
- [ ] Tests — `tests/DualPlayer.Core.Tests/Diagnostics/`

## Edge cases

| Case | Behaviour |
|---|---|
| Excursion still open when the run ends | emitted once with duration up to the end |
| Drift above 10 ms only inside a recovery window | no excursion, not counted in largest steady-state drift |
| Output lost mid-run | played duration stops at the loss; its device-lost event is in the summary |

## Definition of Done

- [ ] `ExcursionTrackerTests` pass: one excursion per out-of-tolerance stretch with correct device, peak, duration and start position
- [ ] `RunSummaryBuilderTests` pass: recovery windows excluded from largest drift; every output and device-lost event present
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
