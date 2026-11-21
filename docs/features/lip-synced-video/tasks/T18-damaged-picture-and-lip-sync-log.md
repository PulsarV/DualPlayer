---
id: T18
title: "Skip damaged frames, end on an unrecoverable picture, and log picture timing and frame counters"
layer: "app"
deps: ["T13"]
blocks: ["T22"]
acs: ["AC-06b", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPictureHealthTests.cs"]
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

# T18 — Skip damaged frames, end on an unrecoverable picture, and log picture timing and frame counters

## Place in the sequence

- **Blocked by:** T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events
- **Blocks:** T22 — Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code
- **Wave:** 7 — after its deepest dependency finishes (wave 6).
- **Lane:** shares `src/DualPlayer.Core/Playback/PlaybackSession.cs` with T13, T14, T15, T16, T17 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to measure, per output, how far the sound is from the picture with a click-and-flash test video filmed by a phone, and see in the log which latency, offset and picture delay the player used
> **So that** I can accept this step on measurements of what the Viewer actually hears, not only on the player's own numbers
>
> — `spec.md §4, US-07, verbatim` · full text: [spec.md](../spec.md)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task gives the Developer the numbers to pair with each phone measurement and turns a broken picture into a clean end instead of a hang.

## Inlined context

> Flow 7: renderer reports hardware decoding is not active after load → treat as picture cannot be decoded further; stretch of damaged frames → sound plays on, skipped frames counted per minute in the log → picture decodes again → log the damaged stretch with its position and length; picture cannot be decoded again at all → stop as at the natural end, picture on its last good frame → note that the picture could not be decoded further → plain-language reason, run summary printed and logged.
> Flow 9: on picture delay, latency or offset change, and on seek, pause, resume or file change → record master-clock position, picture delay and per-output values; presented position with its timestamp → record how far the picture was presented behind or ahead of due; every minute → late, dropped and skipped frame counters, a frame later than 20 ms after due counts as late.
>
> — `sad.md §6, flows 7 and 9, abridged` · full text: [sad.md](../sad.md)

| Aspect | Target | Measurement |
|---|---|---|
| Picture presentation on the target PC | ≤ 1 frame presented late or dropped per minute, averaged over a 2 h film, for 1080p H.264 and HEVC files with hardware decoding; a frame is late when presented more than 20 ms after its due time (the due time follows the picture delay, so a deliberate move at ≤ 0.2 % is not lateness) | the renderer's late and dropped frame counters in the log |

— `spec.md §6, NFR row picture presentation, verbatim` · full text: [spec.md](../spec.md)

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| picture failed | Picture cannot be decoded further (AC-06b, flow 7) | `OsdNote` — `The picture could not be decoded further - film ended` | wireframe below |

— `screens.md SCR-03, row picture failed, verbatim` · full text: [screens.md](../screens.md)

> The session runs on the step-3 engine control loop (two-outputs-one-clock ADR-0007), so transport commands, offset changes, device loss and picture-sync ticks are serialized on one thread and never touch a render thread or the UI thread. New session events travel on their own `SessionEvent` stream in `Core/Playback/`, merged with the engine events by the host, so `Core/Diagnostics/` (step 6) is not edited.
>
> — `sad.md §5, intro, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

```
picture-failed   clock 01:10:05.120  the picture could not be decoded further, film ended
```

— `contracts/cli.md §2.3, picture-failed line, verbatim` · full text: [cli.md](../contracts/cli.md)

Log records `PictureTiming` and `FrameCounters` as defined in `cli.md §3` (inlined in T13); printing is T21.

## Acceptance criteria

### AC-06b — error

> **Given** a film is playing
> **When** part of its picture cannot be decoded (a damaged stretch of the file)
> **Then** the damaged frames are skipped while the sound plays on and the log counts them; if the picture cannot be decoded again at all, the film stops as at its natural end (AC-06) and the window and the Developer's log say in plain words that the picture could not be decoded further
>
> — `spec.md §5, AC-06b, verbatim` · full text: [spec.md](../spec.md)

### AC-17 — happy path

> **Given** a film is playing
> **When** the picture delay or any output's latency or offset changes, or a seek, pause, resume or file change happens
> **Then** the log records the master-clock position, the new picture delay, each output's assumed latency split into reported part and type default, and its offset, and how far behind or ahead the picture was presented compared with when it was due
>
> — `spec.md §5, AC-17, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Subscribe to the renderer's decode-failure / hardware-inactive events on the control loop → ended + `PictureFailed` + note + run summary — `src/DualPlayer.Core/Playback/PlaybackSession.cs`
- [ ] Track damaged stretches from the skipped-damaged counter (start position, length) and log them
- [ ] Emit `PictureTiming` once per second and `FrameCounters` once per minute from `TimeProvider` timers on the control loop
- [ ] Tests — `tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPictureHealthTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Damage then recovery | sound never stops; one damaged-stretch record (AC-06b) |
| Two FFmpeg builds disagree (probe ok, mpv not hardware) | backstop → picture failure, never software playback (ADR-0007) |
| Deliberate 0.2 % move | not counted as lateness — due follows the picture delay (§6) |

## Definition of Done

- [x] `PlaybackSessionPictureHealthTests` pass for AC-06b and the AC-17 cadence
- [x] every Hard Rule inlined above still holds
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
