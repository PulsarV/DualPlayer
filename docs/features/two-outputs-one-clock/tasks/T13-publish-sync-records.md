---
id: T13
title: "Publish per-device sync records, excursions and one-step corrections once per second"
layer: "app"
deps: ["T11"]
blocks: ["T21"]
acs: ["AC-07", "AC-11"]
files_hint: ["src/DualPlayer.Core/Playback/EngineControlLoop.cs", "src/DualPlayer.Core/Diagnostics/SyncRecordPublisher.cs", "tests/DualPlayer.Core.Tests/Diagnostics/SyncRecordPublisherTests.cs", "tests/DualPlayer.Core.Tests/Playback/LongRunSimulationTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T13 — Publish per-device sync records, excursions and one-step corrections once per second

## Place in the sequence

- **Blocked by:** T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss
- **Blocks:** T21 — Console: print sync records, excursions, device loss and the run summary, also on Ctrl+C and window close
- **Wave:** 5 — after its deepest dependency finishes (wave 4).
- **Lane:** shares `src/DualPlayer.Core/Playback/EngineControlLoop.cs` with T11 — serialized; shares `src/DualPlayer.Core/Playback/EngineControlLoop.cs` with T12 — serialized.

## Why (user story)

> **As a** Developer
> **I want** drift, correction and underrun counters recorded separately for every device
> **So that** roadmap steps 4–6 and the crackling investigation work from data instead of guesses
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

> **As a** Viewer
> **I want** to hear my track without drift building up over a feature-length film
> **So that** my sound never wanders away from the shared playback timeline
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task produces the per-device evidence — records and excursions — that the evidence run and roadmap step 6 read.

## Inlined context

> **Chosen:** `IObservable<EngineEvent>` published by the engine-control loop — render threads only update lock-free counters; once per second, timed by `TimeProvider`, the control loop snapshots every output, publishes one `SyncRecord` per device and writes the structured log entry; excursions, device-lost events and the run summary go through the same stream.
>
> — `adr/0007 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0007](../adr/0007-publish-engine-events-as-an-observable-stream-from-the-engine-control-loop.md)

> **Engine changes roadmap step 6 needs to read the counters** — baseline: n/a (step 6 not started), target: 0 changes when step 6 is built.
>
> — `spec.md §7, KPI 5, verbatim` · full text: [spec.md](../spec.md)

> | Aspect | Target | Measurement |
> |---|---|---|
> | Steady-state drift of each audio output from the master clock | ≤ 10 ms (absolute) | per-device records (AC-11) over a ≥ 3 h run on the target PC with both Bluetooth headsets; each record's drift is the average of the device-position readings over its 1 s interval; largest value outside the recovery windows (the 1 s after start, resume or seek — nothing else is excluded). Measured against the device-reported playback position only; acoustic verification is roadmap step 12 |
> | Recovery after start, resume or seek | every output within the drift tolerance ≤ 1 s after the command | per-device records (AC-11); unit tests with a fake clock and fake devices |
>
> — `spec.md §6, NFR rows 1–2, verbatim` · full text: [spec.md](../spec.md)

> | Number of outputs | ≥ 3 outputs simultaneously within the drift tolerance | test with fake devices (no third physical device is required) |
>
> — `spec.md §6, NFR row 7, verbatim` · full text: [spec.md](../spec.md)

> Correction — the adjustment the engine makes to bring an audio output back to the master clock: reported as the continuous playback-rate adjustment in parts per million, and counted only when the output's position is re-aligned in one step. NOT Drift (drift is the error; correction is the response to it).
>
> — `CONTEXT.md §Glossary, Correction, verbatim` · full text: [CONTEXT.md](../../../../CONTEXT.md)

```text
loop once per second, on the engine-control loop
    S->>S: snapshot the counters of every output
    opt drift too large for the rate adjustment
        S->>S: re-align that output's position in one step, count a correction
    S-->>C: one sync record per device as an engine event
    S->>D: write the sync records
    alt drift beyond the 10 ms tolerance
        S->>S: open or extend the excursion for that device
    else back within tolerance after an excursion
        S->>D: write the excursion
        S-->>C: excursion event
```

— `sad.md §6, Flow 7 once-per-second loop, sequence lines, abridged` · full text: [sad.md](../sad.md)

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

## Checklist

- [ ] `SyncRecordPublisher` driven by a `TimeProvider` timer on the control loop: snapshot every output's counters, feed drift controllers, publish one `SyncRecord` per device, log it as a structured event — `src/DualPlayer.Core/Diagnostics/SyncRecordPublisher.cs`
- [ ] One-step re-alignment when drift exceeds what the clamped rate can remove within the 1 s recovery time (threshold named as a constant); count a correction
- [ ] Feed `ExcursionTracker`; publish + log each closed `Excursion`
- [ ] Hook the publisher into `EngineControlLoop` — `src/DualPlayer.Core/Playback/EngineControlLoop.cs`
- [ ] Long-run simulation at fake time — `tests/DualPlayer.Core.Tests/Playback/LongRunSimulationTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Steady run | corrections stay 0; only the ppm value moves |
| Injected step error (e.g. stalled output resumes) | one correction counted, excursion recorded once |
| Device lost | its records stop; others continue at 1 s cadence |
| Subscriber throws or is slow | does not affect render threads (events are on the control loop) |

## Definition of Done

- [ ] `SyncRecordPublisherTests` pass: one record per device per second with device id + clock position, also written to the log
- [ ] `LongRunSimulationTests` pass: 3 fake outputs at different ±ppm over a simulated ≥ 3 h run keep every record ≤ 10 ms outside recovery windows with 0 corrections
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
