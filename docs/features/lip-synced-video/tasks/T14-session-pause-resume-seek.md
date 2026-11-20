---
id: T14
title: "Pause, resume and seek picture and every output together in the session"
layer: "app"
deps: ["T13"]
blocks: ["T15"]
acs: ["AC-04", "AC-05"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionTransportTests.cs"]
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

# T14 — Pause, resume and seek picture and every output together in the session

## Place in the sequence

- **Blocked by:** T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events
- **Blocks:** T15 — Apply offset keys per output and move picture and unchanged outputs to a new picture delay together
- **Wave:** 7 — after its deepest dependency finishes (wave 6).
- **Lane:** shares `src/DualPlayer.Core/Playback/PlaybackSession.cs` with T13, T15, T16, T17, T18 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to pause, resume and seek with a few keys on a wireless keyboard
> **So that** the picture and both Viewers' sound stop, start and jump together without anyone getting up
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

This task makes the couch transport keys move picture and both Viewers' sound as one.

## Inlined context

> Flow 4: pause → stop giving sound to every output at the same master-clock instant; sound already inside each device plays out, up to its latency → freeze point = content position of the last sound heard on the slowest output → picture keeps running and pauses at the freeze point, no later than the picture delay after the key → state paused. Resume → give sound again from the freeze point on every output with its lag → play the picture from the freeze point at its due time → log lip-synced again with the command time, or the recovery excursion per output and for the picture.
>
> — `sad.md §6, flow 4, abridged` · full text: [sad.md](../sad.md)

> Flow 5: target = current position plus step; before the start → start of the film; past the end → stop sound and picture as at the natural end, state ended; inside and playing → new playback epoch at the target on every output with its lag, picture delay and offsets set at once, picture seeks and plays at its due time; inside and paused → picture seeks and shows that frame, new freeze point = target; inside and at the end → new playback epoch at the target, picture seeks and plays.
>
> — `sad.md §6, flow 5, abridged` · full text: [sad.md](../sad.md)

| Aspect | Target | Measurement |
|---|---|---|
| Recovery after pause, resume, seek or file change | picture and every output lip-synced again ≤ 1 s after the command (file change: within the start budget); "lip-synced again" = the picture presented within 20 ms of due and every output within 10 ms of its due position | log: command time, the first picture presentation within 20 ms of due, and each output's position error; unit tests with a fake clock |

— `spec.md §6, NFR row recovery, verbatim` · full text: [spec.md](../spec.md)

| Key | Action |
|---|---|
| Space | pause / resume |
| ← / → | seek −10 s / +10 s |
| ↓ / ↑ | seek −60 s / +60 s |
| Page Up / Page Down | previous / next file |
| Esc | exit |
| 1 / Q | output 1: offset +10 ms (later) / −10 ms (earlier) |
| 2 / W | output 2: offset +10 ms / −10 ms |
| 3 / E | output 3: offset +10 ms / −10 ms |

— `spec.md §5, fixed keys, verbatim` · full text: [spec.md](../spec.md)

> The session runs on the step-3 engine control loop (two-outputs-one-clock ADR-0007), so transport commands, offset changes, device loss and picture-sync ticks are serialized on one thread and never touch a render thread or the UI thread. New session events travel on their own `SessionEvent` stream in `Core/Playback/`, merged with the engine events by the host, so `Core/Diagnostics/` (step 6) is not edited.
>
> — `sad.md §5, intro, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-04 — happy path

> **Given** a film is playing on two outputs
> **When** a Viewer presses the pause key, and later the pause key again
> **Then** every output stops being given sound at the same moment, the sound already inside each device plays out, and the picture keeps running until that sound has played out (no longer than the picture delay) and then freezes on the frame that matches the last sound heard; on the second press picture and sound continue together from that point, with no words heard twice and no picture skipped, lip-synced again within the recovery time (§6)
>
> — `spec.md §5, AC-04, verbatim` · full text: [spec.md](../spec.md)

### AC-05 — happy path

> **Given** a film is playing on two outputs, is paused, or has reached its end
> **When** a Viewer presses a seek key for −10 s, +10 s, −60 s or +60 s
> **Then** while playing, the picture and every output continue from the new position together, lip-synced again within the recovery time (§6); while paused, the frame at the new position is shown and the film stays paused; at the end, a backward seek returns into the film and it plays from there; a seek before the start goes to the start, and a seek past the end ends the film as at its natural end
>
> — `spec.md §5, AC-05, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `Pause()`: engine pause (step-3 epoch instant), compute the freeze point from the slowest output, let `PictureSync` run on and pause the renderer there — `src/DualPlayer.Core/Playback/PlaybackSession.cs`
- [ ] `Resume()`: engine resume with at-once lag targets, picture aligned from the freeze point; log recovery
- [ ] `Seek(step)`: clamp to start, past end → ended; playing / paused / ended branches per flow 5; offsets and PD applied at once
- [ ] Emit `PictureDelayChanged` (cause `pause`/`resume`/`seek`) and a `PictureTiming` record per command (AC-17)
- [ ] Tests — `tests/DualPlayer.Core.Tests/Playback/PlaybackSessionTransportTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Seek before the start | goes to the start (AC-05) |
| Seek past the end | film ends as at its natural end (AC-05) |
| Backward seek while ended | returns into the film and plays (AC-05) |
| Seek while paused | new frame shown, stays paused (AC-05) |
| Offset changed while paused | applied at once on resume (AC-10, flow 8) |

## Definition of Done

- [ ] `PlaybackSessionTransportTests` pass for every AC-04/AC-05 branch on the fake clock
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
