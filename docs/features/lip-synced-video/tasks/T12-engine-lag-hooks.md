---
id: T12
title: "Give the audio engine per-output start lags in the pre-roll and lag targets that ramp or jump"
layer: "app"
deps: ["T11"]
blocks: ["T13"]
acs: ["AC-01", "AC-14"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Playback/EngineOptions.cs", "src/DualPlayer.Core/Playback/IOutputLagControl.cs", "src/DualPlayer.Core/CoreServiceCollectionExtensions.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineLagTests.cs"]
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

# T12 — Give the audio engine per-output start lags in the pre-roll and lag targets that ramp or jump

## Place in the sequence

- **Blocked by:** T11 — Measure each output's drift against clock minus its lag and ramp lag changes at up to 2000 ppm
- **Blocks:** T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events
- **Wave:** 5 — after its deepest dependency finishes (wave 4).
- **Lane:** shares `src/DualPlayer.Core/CoreServiceCollectionExtensions.cs` with T13 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

> **As a** Viewer
> **I want** the picture to follow smoothly when the slowest output changes — its offset is corrected or its device is lost
> **So that** neither Viewer sees the picture jump or loses lip-sync for longer than a moment
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task wires the lag mechanism into the step-3 engine: lags join the silent pre-roll at start, and later lag changes reach every render thread through the control loop.

## Inlined context

> **Target lag inside the drift controller.** Each output's drift error is measured against `clock − Dᵢ` instead of the clock. At start the lag joins the silent pre-roll alignment. During playback a change of `Dᵢ` is a target ramp at ≤ 2000 ppm, added to the drift correction and carried by the existing resampler. Changes stay in `Core/Sync/` and `Core/Playback/`.
>
> — `adr/0005 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0005](../adr/0005-hold-faster-outputs-back-with-a-target-lag-moved-by-the-drift-resampler.md)

> With a negative picture delay the picture starts before the slowest output hears frame 0, timed from the pre-roll measurement (`sad.md` §6 flow 1).
>
> — `adr/0005 §Consequences, neutral, verbatim` · full text: [ADR-0005](../adr/0005-hold-faster-outputs-back-with-a-target-lag-moved-by-the-drift-resampler.md)

> `AudioEngine.cs` — *changed*: per-output start lag in the pre-roll release; lag targets per output
>
> — `sad.md §5, internal decomposition, Playback/AudioEngine.cs, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** Step 4 confines engine changes to the lag hooks and only adds new files elsewhere (§2, §5).
>
> — `sad.md §11, risk row parallel wave, mitigation, abridged` · full text: [sad.md](../sad.md)

> Flow 1: silent pre-roll on every output, then release with each output's lag → slowest output hears the first frame → master clock starts
>
> — `sad.md §6, flow 1, engine steps, abridged` · full text: [sad.md](../sad.md)

| Aspect | Target | Measurement |
|---|---|---|
| Start command to picture and sound | ≤ 3 s, picture and every output together (step 3 budget, ADR-0008); "together" means that from the first frame on, the picture is presented within 20 ms of due and every output is within 10 ms of its due position | timestamps in the log: start command, first frame presented, first audio heard per output (logged as the moment the first sound is given to the device plus its assumed latency; what is actually heard is proven by AC-16) |

— `spec.md §6, NFR row start, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule:** **Parallel wave 4 ∥ 5 ∥ 6** (`docs/roadmap.md` execution path, wave 3): this step works only in `Core/Playback/`, `Core/Sync/`, `Core/Settings/` (new files), `src/DualPlayer.Media/` and `src/DualPlayer.App/`, plus new files elsewhere. It does not edit `Core/Outputs/` or the device enumeration in `Platform.Windows/` (owned by step 5).
>
> — `sad.md §2, Technical, parallel wave, abridged` · full text: [sad.md](../sad.md)

> **Hard rule:** The playback session, picture sync and lag ramps run on the step-3 engine control loop. mpv property and end events arrive on mpv's event thread; the adapter publishes immutable snapshots without locks the control loop could wait on. Render threads are never blocked. UI updates marshal through `Dispatcher.UIThread`. The picture probe runs off the UI thread with a time limit
>
> — `sad.md §8, Threading, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-01 — happy path

> **Given** a video file with a picture and at least two audio tracks, and the Developer's start command names the file, a track and a device for each Viewer, and a screen
> **When** the start command is given
> **Then** the film opens fullscreen on the named screen (on the main screen when none is named), the picture and every output's sound start together within the start budget (§6), and each Viewer hears their own track; on a device whose offset was set in an earlier run (AC-12) the sound is in step with the lips within the lip-sync budget (§6), on a device's first use the error is only measured (§7)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-14 — domain invariant

> **Given** a film is playing and the picture is held back by the picture delay the slowest output needs
> **When** the picture delay has to change — an offset is corrected, or the slowest output's device is lost and another output becomes the slowest
> **Then** the picture and every output whose offset did not change move to the new delay together, at most 0.2 % faster or slower than normal speed, so that nobody sees a jump, a black frame or a repeated scene or hears a change of pitch, and each of those outputs stays within 10 ms of where it was against the picture throughout the move; the corrected output reaches its new offset no later than the move ends; a seek, file change or resume during a move sets the target picture delay and offsets at once instead, since picture and sound are re-aligned then anyway
>
> — `spec.md §5, AC-14, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Add `InitialLags` (per output) to `EngineOptions` as an optional init property so existing callers compile — `src/DualPlayer.Core/Playback/EngineOptions.cs`
- [ ] New port `IOutputLagControl` (set lag targets per output, mode ramp | at once; pre-roll-to-frame-0 instant; ramp-in-progress info) implemented by `AudioEngine`; **do not add members to `IAudioEngine`** — its fakes in `DualPlayer.Console.Tests` must stay untouched — `src/DualPlayer.Core/Playback/IOutputLagControl.cs`
- [ ] `AudioEngine`: insert each output's lag as extra silence in the pre-roll release; apply lag targets on the control loop to each output's `LagRamp`; seek/resume use at-once targets — `src/DualPlayer.Core/Playback/AudioEngine.cs`
- [ ] Register `IOutputLagControl` to the same `AudioEngine` singleton — `src/DualPlayer.Core/CoreServiceCollectionExtensions.cs`
- [ ] Tests — `tests/DualPlayer.Core.Tests/Playback/AudioEngineLagTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Lag target for a lost output | ignored; remaining outputs unaffected |
| All lags 0 (one output, or equal assumed latencies) | behaviour identical to step 3 |
| Ramp running when the run ends | run summary counts no correction or excursion from it (ADR-0005) |

## Definition of Done

- [ ] `AudioEngineLagTests` pass; every existing Core and Console test passes unchanged
- [ ] `IAudioEngine.cs` unchanged; no file in `Core/Outputs/` or `Core/Diagnostics/` changed
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
