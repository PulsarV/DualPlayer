---
id: T9
title: "Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting"
layer: "app"
deps: ["T1", "T5", "T6", "T7"]
blocks: ["T11"]
acs: ["AC-07", "AC-08", "AC-09"]
files_hint: ["src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "src/DualPlayer.Core/Outputs/AudioRingBuffer.cs", "tests/DualPlayer.Core.Tests/Outputs/OutputRenderSourceTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "M"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T9 — Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting

## Place in the sequence

- **Blocked by:** T1 — Define the audio engine ports, value types and test fakes in Core, T5 — Build the epoch-tagged single-producer/single-consumer ring buffer, T6 — Write the managed adaptive resampler with one-step re-alignment, T7 — Implement the master clock, drift measurement, PI drift controller and clock-mismatch estimate
- **Blocks:** T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss
- **Wave:** 3 — after its deepest dependency finishes (wave 2).
- **Lane:** shares `src/DualPlayer.Core/Outputs/AudioRingBuffer.cs` with T5 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to hear my track without drift building up over a feature-length film
> **So that** my sound never wanders away from the shared playback timeline
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

> **As a** Developer
> **I want** to pause, resume and seek all audio outputs at once
> **So that** start and stop alignment is proven before video is added
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task is the code each device thread calls every period; it delivers drift-corrected audio, falls silent at the right frame and counts underruns.

## Inlined context

> **Chosen — Pull:** the backend opens the device, owns its render thread and, whenever the device needs data, calls a Core-provided source `Render(buffer, devicePosition)` where `devicePosition` carries the frames the device has played and the monotonic timestamp of that reading; device loss and access denial arrive as backend events.
>
> — `adr/0004 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0004](../adr/0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position.md)

> **Chosen — Playback epochs with streams kept running:** one engine-control loop applies commands one at a time; seek and stop increment an epoch, the decoder tags frames with the epoch they belong to, render sources discard older-epoch frames and play silence until new frames arrive; pause and stop choose an effective instant on the master clock that lies beyond the audio already queued in every device (the largest queued amount among the devices, known from each device's written frames minus its reported position), and every render source switches to silence exactly at the frame that maps to that instant; device streams are never stopped during a run.
>
> — `adr/0006 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0006](../adr/0006-coordinate-pause-and-seek-with-playback-epochs-while-device-streams-keep-running.md)

> **Hard rule — Real-time code.** **Override:** code called on a render thread (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws; it only reads its ring and updates atomic counters; faults are reported to the control loop as events
>
> — `sad.md §8, Real-time code, verbatim` · full text: [sad.md](../sad.md)

```text
loop every device period, on that device's render thread
    X->>S: render request with device position
    S->>S: measure drift of the device position against the master clock
    S->>S: apply the controller's rate ratio in the adaptive resampler
    alt ring holds enough frames
        S-->>X: frames for that device
    else ring ran dry
        S-->>X: silence, underrun counter increases
```

— `sad.md §6, Flow 7 render loop, sequence lines, verbatim` · full text: [sad.md](../sad.md)

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

### AC-08 — happy path

> **Given** two audio outputs are playing
> **When** the Developer pauses and later resumes playback
> **Then** on pause (and on stop) all outputs fall silent within 10 ms of each other, judged by each device's reported playback position, and the master clock stops; on resume all outputs continue from the same master-clock position and are back within the drift tolerance within the recovery time (§6)
>
> — `spec.md §5, AC-08, verbatim` · full text: [spec.md](../spec.md)

### AC-09 — happy path

> **Given** two audio outputs are playing
> **When** the Developer seeks 10 or 60 seconds forward or backward
> **Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console; the console prints that confirmation only after every output has discarded the audio it had queued from the old position
>
> — `spec.md §5, AC-09, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `OutputRenderSource : IRenderSource` — `src/DualPlayer.Core/Outputs/OutputRenderSource.cs`
- [ ] On each `Render(buffer, position)`: publish the latest position atomically for the control loop; read the ring through `AdaptiveResampler` at the ratio last set by the control loop (atomic)
- [ ] Make `AudioRingBuffer` implement `IFrameReader` (current epoch passed by the source) — `src/DualPlayer.Core/Outputs/AudioRingBuffer.cs`
- [ ] Ring dry while playing → fill silence, increment the underrun counter (atomic); silence while paused/stopped is not an underrun
- [ ] Silence instant: when the control loop sets a silence-from content frame, hand frames up to it, then zeros
- [ ] Track frames written to the device, so queued = written − `FramesPlayed`; expose it and the content position of the last old-epoch frame handed out (seek confirmation gate)
- [ ] Pending one-step re-alignment requested by the control loop is applied via `Realign` at the next call
- [ ] Tests with `FakeAudioDevice` — `tests/DualPlayer.Core.Tests/Outputs/OutputRenderSourceTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Ring dry during playback | silence for the missing frames; underruns += 1 per affected call |
| New epoch while old frames still queued in the ring | old frames discarded, silence until new ones arrive; not an underrun |
| Silence instant falls inside the current buffer | audio exactly up to that frame, zeros after |
| Exception inside `Render` | must not happen by design; there is no try/catch on this path (real-time rule) |

## Definition of Done

- [ ] `OutputRenderSourceTests` pass: underrun counting, epoch discard without underrun, silence at the exact frame, queued-frames accounting, re-alignment applied once
- [ ] a test asserts `Render` allocates 0 bytes
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
