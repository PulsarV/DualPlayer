---
id: T11
title: "Measure each output's drift against clock minus its lag and ramp lag changes at up to 2000 ppm"
layer: "domain"
deps: ["T5"]
blocks: ["T12"]
acs: ["AC-14"]
files_hint: ["src/DualPlayer.Core/Sync/LagRamp.cs", "src/DualPlayer.Core/Sync/DriftController.cs", "src/DualPlayer.Core/Sync/OutputSync.cs", "src/DualPlayer.Core/Sync/DriftMeter.cs", "tests/DualPlayer.Core.Tests/Sync/LagRampTests.cs", "tests/DualPlayer.Core.Tests/Sync/OutputSyncTests.cs", "tests/DualPlayer.Core.Tests/Sync/DriftControllerTests.cs"]
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

# T11 — Measure each output's drift against clock minus its lag and ramp lag changes at up to 2000 ppm

## Place in the sequence

- **Blocked by:** T5 — Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict
- **Blocks:** T12 — Give the audio engine per-output start lags in the pre-roll and lag targets that ramp or jump
- **Wave:** 4 — after its deepest dependency finishes (wave 3).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** the picture to follow smoothly when the slowest output changes — its offset is corrected or its device is lost
> **So that** neither Viewer sees the picture jump or loses lip-sync for longer than a moment
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task gives every output the ability to wait behind the master clock by its lag and to move that lag smoothly, using the resampler that already carries drift correction.

## Inlined context

> **Target lag inside the drift controller.** Each output's drift error is measured against `clock − Dᵢ` instead of the clock. At start the lag joins the silent pre-roll alignment. During playback a change of `Dᵢ` is a target ramp at ≤ 2000 ppm, added to the drift correction and carried by the existing resampler. Changes stay in `Core/Sync/` and `Core/Playback/`.
>
> — `adr/0005 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0005](../adr/0005-hold-faster-outputs-back-with-a-target-lag-moved-by-the-drift-resampler.md)

> During a ramp, the drift metrics must tell an intended move from real drift. Drift is measured against the moving target, and the ramp is logged as its own event. Otherwise the run summary would report false excursions and corrections.
> The resampler's total ratio range widens from ±500 ppm to about ±2500 ppm, which must be checked for audible artefacts on the target PC.
>
> — `adr/0005 §Consequences, negative, verbatim` · full text: [ADR-0005](../adr/0005-hold-faster-outputs-back-with-a-target-lag-moved-by-the-drift-resampler.md)

> `LagRamp.cs` — ≤ 2000 ppm target ramp for an output's lag (ADR-0005)
> `DriftController.cs, OutputSync.cs, DriftMeter.cs` — *changed*: error measured against clock − Dᵢ; ramp added to the ratio
>
> — `sad.md §5, internal decomposition, Sync/, verbatim` · full text: [sad.md](../sad.md)

| Aspect | Target | Measurement |
|---|---|---|
| Picture-delay change during playback | picture and unchanged outputs move together at ≤ 0.2 % speed change (about 5 s per 10 ms of change), each within 10 ms of where it was against the picture throughout | log of picture presentation against due time and of playback speed; unit tests with a fake clock |

— `spec.md §6, NFR row picture-delay change, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule:** **Parallel wave 4 ∥ 5 ∥ 6** (`docs/roadmap.md` execution path, wave 3): this step works only in `Core/Playback/`, `Core/Sync/`, `Core/Settings/` (new files), `src/DualPlayer.Media/` and `src/DualPlayer.App/`, plus new files elsewhere. It does not edit `Core/Outputs/` or the device enumeration in `Platform.Windows/` (owned by step 5).
>
> — `sad.md §2, Technical, parallel wave, abridged` · full text: [sad.md](../sad.md)

> **Hard rule:** No `DateTime.Now` / `Stopwatch` in Core — time comes from an injected `TimeProvider` or the audio master clock, so picture and latency logic is testable with a fake clock.
>
> — `sad.md §2, Conventions, time, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-14 — domain invariant

> **Given** a film is playing and the picture is held back by the picture delay the slowest output needs
> **When** the picture delay has to change — an offset is corrected, or the slowest output's device is lost and another output becomes the slowest
> **Then** the picture and every output whose offset did not change move to the new delay together, at most 0.2 % faster or slower than normal speed, so that nobody sees a jump, a black frame or a repeated scene or hears a change of pitch, and each of those outputs stays within 10 ms of where it was against the picture throughout the move; the corrected output reaches its new offset no later than the move ends; a seek, file change or resume during a move sets the target picture delay and offsets at once instead, since picture and sound are re-aligned then anyway
>
> — `spec.md §5, AC-14, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `LagRamp`: current lag, target, `Advance(elapsed)` at ≤ 2000 ppm, `Retarget`, `SetAtOnce`, `IsMoving`, current rate in ppm — `src/DualPlayer.Core/Sync/LagRamp.cs`
- [ ] `OutputSync` / `DriftMeter`: measure drift against `clock − Dᵢ` with `Dᵢ` the ramp's current value — `src/DualPlayer.Core/Sync/OutputSync.cs`, `src/DualPlayer.Core/Sync/DriftMeter.cs`
- [ ] `DriftController`: resampler ratio = drift correction (still capped at 500 ppm) + ramp rate; keep it allocation-free on the render thread — `src/DualPlayer.Core/Sync/DriftController.cs`
- [ ] Tests — `tests/DualPlayer.Core.Tests/Sync/LagRampTests.cs`, `OutputSyncTests.cs`, `DriftControllerTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Retarget during a ramp | continues from the current lag toward the new target, no queueing |
| Seek, file change or resume during a ramp | `SetAtOnce` — targets set at once (AC-14) |
| Ramp mistaken for drift | never: drift is measured against the moving target (ADR-0005) |
| `Core/Outputs/` (`OutputRenderSource`) | not edited (§2 constraint) |

## Definition of Done

- [ ] new and existing `Core.Tests/Sync` tests pass
- [ ] no file under `src/DualPlayer.Core/Outputs/` changed
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
