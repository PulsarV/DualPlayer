---
id: T12
title: "Pause, resume and seek every output together with playback epochs"
layer: "app"
deps: ["T11"]
blocks: ["T20"]
acs: ["AC-08", "AC-09", "AC-09b", "AC-10"]
files_hint: ["src/DualPlayer.Core/Playback/IAudioEngine.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Playback/EngineControlLoop.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineTransportTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "M"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T12 — Pause, resume and seek every output together with playback epochs

## Place in the sequence

- **Blocked by:** T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss
- **Blocks:** T20 — Console: pause, resume, seek and stop a running run from standard input
- **Wave:** 5 — after its deepest dependency finishes (wave 4).
- **Lane:** shares `src/DualPlayer.Core/Playback/IAudioEngine.cs` with T11 — serialized; shares `src/DualPlayer.Core/Playback/EngineControlLoop.cs` with T13 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to pause, resume and seek all audio outputs at once
> **So that** start and stop alignment is proven before video is added
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task delivers the transport commands that prove start and stop alignment before video arrives.

## Inlined context

> **Chosen — Playback epochs with streams kept running:** one engine-control loop applies commands one at a time; seek and stop increment an epoch, the decoder tags frames with the epoch they belong to, render sources discard older-epoch frames and play silence until new frames arrive; pause and stop choose an effective instant on the master clock that lies beyond the audio already queued in every device (the largest queued amount among the devices, known from each device's written frames minus its reported position), and every render source switches to silence exactly at the frame that maps to that instant; device streams are never stopped during a run.
>
> — `adr/0006 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0006](../adr/0006-coordinate-pause-and-seek-with-playback-epochs-while-device-streams-keep-running.md)

> - Devices keep consuming silence while paused (small CPU cost, counted in the ≤ 5 % budget).
> - Pause and stop take effect a few tens of milliseconds after the command (the largest device queue), not instantly.
> - A seek is confirmed only after each output has discarded its old-epoch ring frames **and** its device's reported position has passed the last old-epoch frame already handed to the device; confirmation time therefore includes the device buffer.
>
> — `adr/0006 §Consequences, Negative, verbatim` · full text: [ADR-0006](../adr/0006-coordinate-pause-and-seek-with-playback-epochs-while-device-streams-keep-running.md)

```text
Core->>Core: new epoch, master clock jumps to target
Core->>Media: seek decoder to target, tag frames with new epoch
Core->>Core: drop old-epoch frames, emit silence until new frames arrive
Media-->>Core: new-epoch frames into each ring buffer
Core->>Core: every output reports old ring frames discarded
Core->>Core: every device position has passed its last old-epoch frame
Core-->>Console: seek confirmed at new position
Core->>Core: drift controller re-aligns each output within 1 s
```

— `sad.md §6, Critical flow 2 «seek while playing», sequence lines, abridged` · full text: [sad.md](../sad.md)

```text
S->>S: pick the pause instant beyond the audio queued in every device
S-->>X: audio up to the pause instant, then silence
S->>S: stop the master clock at the pause instant
opt Developer seeks while paused
    S->>S: new epoch, clock moves to the target and stays stopped
    S-->>C: position moved, still paused, every output silent
S->>S: wait until every ring holds the start level
S->>S: start the master clock from the paused or new position
S->>S: drift controller brings each output within tolerance
```

— `sad.md §6, Flow 5 «pause and resume, seek while paused», sequence lines, abridged` · full text: [sad.md](../sad.md)

```text
alt target before the start
    S->>S: clamp the target to the beginning, new epoch
    S-->>C: target was outside the file, playing from the beginning
else target past the end
    S->>S: move the position to the end of the file at once, new epoch
    S->>S: all outputs fall silent, master clock stops, run ends
    S->>D: write the run summary
    S-->>C: target was outside the file, run summary
```

— `sad.md §6, Flow 6 «seek outside the file», sequence lines, abridged` · full text: [sad.md](../sad.md)

> | Recovery after start, resume or seek | every output within the drift tolerance ≤ 1 s after the command | per-device records (AC-11); unit tests with a fake clock and fake devices |
>
> — `spec.md §6, NFR row 2, verbatim` · full text: [spec.md](../spec.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

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

### AC-09b — happy path (seek while paused)

> **Given** playback is paused
> **When** the Developer seeks
> **Then** the position moves to the new target and playback stays paused with every output silent; on resume all outputs continue from the new position as in AC-08
>
> — `spec.md §5, AC-09b, verbatim` · full text: [spec.md](../spec.md)

### AC-10 — error

> **Given** two audio outputs are playing
> **When** the Developer seeks to a point before the start or past the end of the file
> **Then** a seek before the start plays from the beginning; a seek past the end moves the position to the end of the file at once, all outputs fall silent and the run ends with the run summary (AC-13); in both cases the console tells the Developer the target was outside the file
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `Pause()`, `Resume()`, `Seek(target)` on `IAudioEngine`, executed on the control loop — `IAudioEngine.cs`, `AudioEngine.cs`, `EngineControlLoop.cs`
- [ ] Pause: effective instant = clock now + largest device queue; set silence-from on every render source; stop the clock at that instant; reply `paused at <clock>`
- [ ] Resume: wait for the start level in every ring, start the clock from the paused or new position, reset drift controllers (recovery window starts)
- [ ] Seek while playing: new epoch, clock jumps, `DecodePump.Seek`; reply only after every output discarded old-epoch ring frames **and** its device position passed the last old-epoch frame
- [ ] Seek while paused: new epoch, clock moves and stays stopped, outputs silent
- [ ] Out of range: before start → clamp to 0 + warning; past end → move to end, silence, end run `SeekPastEnd` with summary + warning
- [ ] Tests with fake devices — `tests/DualPlayer.Core.Tests/Playback/AudioEngineTransportTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Devices with different queue depths | silence starts at the same master-clock instant; reported positions within 10 ms of each other |
| Seek ±10 s and ±60 s | confirmation after discard + position gate; every output within 10 ms ≤ 1 s after the command |
| Seek while paused, then resume | continues from the new position as in AC-08 |
| Seek before 0 / past the end | plays from 0 with warning / run ends with summary and warning |
| Pause while already paused | no-op reply with current position |

## Definition of Done

- [ ] `AudioEngineTransportTests` pass for AC-08, AC-09, AC-09b and AC-10, including the 10 ms pause alignment by device position and ≤ 1 s recovery on the fake clock
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
