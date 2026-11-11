---
id: T7
title: "Implement the master clock, drift measurement, PI drift controller and clock-mismatch estimate"
layer: "domain"
deps: ["T1"]
blocks: ["T9", "T11"]
acs: ["AC-07"]
files_hint: ["src/DualPlayer.Core/Sync/MasterClock.cs", "src/DualPlayer.Core/Sync/DriftMeter.cs", "src/DualPlayer.Core/Sync/DriftController.cs", "src/DualPlayer.Core/Sync/ClockMismatchEstimator.cs", "tests/DualPlayer.Core.Tests/Sync/MasterClockTests.cs", "tests/DualPlayer.Core.Tests/Sync/DriftControllerTests.cs"]
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

# T7 — Implement the master clock, drift measurement, PI drift controller and clock-mismatch estimate

## Place in the sequence

- **Blocked by:** T1 — Define the audio engine ports, value types and test fakes in Core
- **Blocks:** T9 — Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting, T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss
- **Wave:** 2 — after its deepest dependency finishes (wave 1).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to hear my track without drift building up over a feature-length film
> **So that** my sound never wanders away from the shared playback timeline
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task gives the engine its one timeline and the control law that keeps every device on it.

## Inlined context

> **A master clock owned by no device** (committed in spec §1, not re-opened here) — the master clock advances with the PC's monotonic clock read through the injected `TimeProvider`, stops on pause and jumps on seek; devices follow it, so losing any device never stops the timeline (CONTEXT invariant).
>
> — `sad.md §4, strategic choice 4, verbatim` · full text: [sad.md](../sad.md)

> **Chosen:** Controller in `Core/Sync/` + managed adaptive resampler on the render thread, between the ring and the device — a proportional-integral controller turns the measured error into a rate ratio; the resampler applies it to the frames being handed to the device; one-step re-alignment drops or inserts frames at the same point.
>
> — `adr/0003 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0003](../adr/0003-correct-drift-with-a-managed-adaptive-resampler-after-the-ring-buffer.md)

> **Hard rule — Time and clocks.** No `DateTime.Now` / `Stopwatch` in Core; the master clock and record cadence read the injected `TimeProvider`; device positions are converted by each adapter to the same monotonic base (ADR-0004)
>
> — `sad.md §8, Time and clocks, verbatim` · full text: [sad.md](../sad.md)

> | Aspect | Target | Measurement |
> |---|---|---|
> | Steady-state drift of each audio output from the master clock | ≤ 10 ms (absolute) | per-device records (AC-11) over a ≥ 3 h run on the target PC with both Bluetooth headsets; each record's drift is the average of the device-position readings over its 1 s interval; largest value outside the recovery windows (the 1 s after start, resume or seek — nothing else is excluded). Measured against the device-reported playback position only; acoustic verification is roadmap step 12 |
> | Recovery after start, resume or seek | every output within the drift tolerance ≤ 1 s after the command | per-device records (AC-11); unit tests with a fake clock and fake devices |
>
> — `spec.md §6, NFR rows 1–2, verbatim` · full text: [spec.md](../spec.md)

> unit tests in `tests/DualPlayer.Core.Tests/Sync/` drive the drift controller and adaptive resampler with a fake `TimeProvider` and fake devices whose clocks run fast or slow by a fixed ppm, asserting |drift| ≤ 10 ms in steady state and recovery ≤ 1 s after start, resume and seek
>
> — `sad.md §10, QG-1 How verify (a), verbatim` · full text: [sad.md](../sad.md)

> The clock mismatch is the device clock's rate relative to the master clock; a correction (CONTEXT) is counted only when the output's position is re-aligned in one step, so a steady run is expected to count 0, while the continuous rate adjustment is what AC-11 reports as current correction, in parts per million
>
> — `spec.md §5, AC-13 (last sentence), abridged` · full text: [spec.md](../spec.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-07 — happy path (long run)

> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `MasterClock(TimeProvider)` — `Start()`, `StopAt(instant)`, `JumpTo(position)`, `IsRunning`, `PositionAt(timestamp)` on the `TimeProvider` timestamp base — `src/DualPlayer.Core/Sync/MasterClock.cs`
- [ ] `DriftMeter` — drift = content position of the frame the device reports played − `PositionAt(reading timestamp)`; keeps the 1 s average for records — `DriftMeter.cs`
- [ ] `DriftController` — PI controller from drift to a near-unity rate ratio (ppm, clamped); `Reset()` on start/resume/seek — `DriftController.cs`
- [ ] `ClockMismatchEstimator` — device clock rate vs master clock in ppm from position readings over the run — `ClockMismatchEstimator.cs`
- [ ] Closed-loop tests with `FakeTimeProvider` and fake devices at fixed ±ppm — `tests/DualPlayer.Core.Tests/Sync/MasterClockTests.cs`, `DriftControllerTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Clock stopped (paused) | `PositionAt` returns the stop position for any later timestamp |
| Jump while stopped | position moves, clock stays stopped |
| Device at +300 ppm and at −300 ppm | steady-state |drift| ≤ 10 ms; mismatch estimate within a tolerance named in the test |
| Step error after a seek | back within 10 ms ≤ 1 s after the command |

## Definition of Done

- [ ] `MasterClockTests` pass (start, stop, jump, stopped-jump)
- [ ] `DriftControllerTests` pass: fake devices at ±ppm hold |drift| ≤ 10 ms in steady state and recover ≤ 1 s after start, resume and seek; mismatch estimate matches the configured ppm
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
