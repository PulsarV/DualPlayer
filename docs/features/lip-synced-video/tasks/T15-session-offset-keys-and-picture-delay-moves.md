---
id: T15
title: "Apply offset keys per output and move picture and unchanged outputs to a new picture delay together"
layer: "app"
deps: ["T14"]
blocks: ["T16", "T20"]
acs: ["AC-10", "AC-11", "AC-14"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionOffsetTests.cs"]
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

# T15 — Apply offset keys per output and move picture and unchanged outputs to a new picture delay together

## Place in the sequence

- **Blocked by:** T14 — Pause, resume and seek picture and every output together in the session
- **Blocks:** T16 — Keep playing when an output's device is lost, and end the film when the last one is lost, T20 — Build the fullscreen playback window hosting mpv's video, mapping the fixed keys and styling notes from tokens
- **Wave:** 8 — after its deepest dependency finishes (wave 7).
- **Lane:** shares `src/DualPlayer.Core/Playback/PlaybackSession.cs` with T13, T14, T16, T17, T18 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to shift my own output's sound earlier or later in small steps while the film plays, and have the player remember it for my device
> **So that** I fix a visible mismatch once and never have to fix it again for the same device
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

> **As a** Viewer
> **I want** the picture to follow smoothly when the slowest output changes — its offset is corrected or its device is lost
> **So that** neither Viewer sees the picture jump or loses lip-sync for longer than a moment
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task lets a Viewer fix lip-sync by ear while the film plays, without anyone else's picture or sound jumping.

## Inlined context

> Flow 8: offset already at ±500 ms in that direction → note that the offset cannot go beyond plus or minus 500 ms, nothing changes; within range and paused or ended → new offset, new assumed latency, picture delay and lags recomputed, note with output and new offset, applied when playback continues; within range and playing → new offset and targets, moved as in flow 2, note. Offset changed → wait until 1 s after the last press → save offsets by device id; save fails → log the failure, keep the offset for this run.
> Flow 2: new picture delay and new lags → picture delay changed: picture speed set to at most 0.2 percent off normal until the new delay is reached, unchanged outputs ramp their lag at the same rate → changed output ramps its lag at most 0.2 percent until its target → picture delay, latency, offset and picture timing logged.
>
> — `sad.md §6, flows 8 and 2, abridged` · full text: [sad.md](../sad.md)

> Offsets are integer milliseconds, step 10, range −500…+500; a positive offset makes sound heard later. Assumed latency `Aᵢ = reported part + type default − offset`; picture delay `PD = max Aᵢ` may be negative; lag `Dᵢ = PD − Aᵢ ≥ 0`. Type defaults (spec §8): Bluetooth headphones 200 ms, Bluetooth speaker 200 ms, wired 0 ms, HDMI 0 ms, other 0 ms, kept in one table in `Core/Sync/LatencyModel`
>
> — `sad.md §8, Units and sign, verbatim` · full text: [sad.md](../sad.md)

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| offset changed | Offset key within range, any window state (AC-10, flow 8) | NEW: `OsdNote` — `Output 1: +40 ms` | wireframe below |
| offset limit | Offset key past ±500 ms (AC-11, flow 8) | `OsdNote` — `Output 1: offset cannot go beyond ±500 ms` | wireframe below |

— `screens.md SCR-03, rows offset changed + offset limit, verbatim` · full text: [screens.md](../screens.md)

> The session runs on the step-3 engine control loop (two-outputs-one-clock ADR-0007), so transport commands, offset changes, device loss and picture-sync ticks are serialized on one thread and never touch a render thread or the UI thread. New session events travel on their own `SessionEvent` stream in `Core/Playback/`, merged with the engine events by the host, so `Core/Diagnostics/` (step 6) is not edited.
>
> — `sad.md §5, intro, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-10 — happy path

> **Given** a film is playing on two outputs (or is paused or at its end — the new offset is then shown and remembered at once and applied when playback continues)
> **When** a Viewer presses their output's offset key to make the sound later (offset +10 ms) or earlier (offset −10 ms)
> **Then** that output's sound moves by 10 ms per press against the picture, gradually at no more than 0.2 % faster or slower than normal speed (about 5 s per 10 ms) with no click or pitch change heard, while every other output stays within 10 ms of where it was against the picture (if the change moves the picture delay, the picture follows as in AC-14); further presses during the move change its target rather than queue behind it; the picture delay is the largest assumed latency (latency minus offset) among the playing outputs and has no lower limit — when every output's assumed latency is below zero the picture is shown ahead of the master clock, so every "later" press takes effect up to +500 ms; the window shows briefly the output and its new offset, and the next start on the same device uses this offset again (it is saved no later than 1 s after the last press, so a crash of the player or the PC does not lose it)
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

### AC-11 — error

> **Given** an output's offset is already at +500 ms (or −500 ms)
> **When** a Viewer presses the key that would make it later (or earlier)
> **Then** the offset stays at the limit and the window shows briefly that the offset cannot go beyond ±500 ms
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

### AC-14 — domain invariant

> **Given** a film is playing and the picture is held back by the picture delay the slowest output needs
> **When** the picture delay has to change — an offset is corrected, or the slowest output's device is lost and another output becomes the slowest
> **Then** the picture and every output whose offset did not change move to the new delay together, at most 0.2 % faster or slower than normal speed, so that nobody sees a jump, a black frame or a repeated scene or hears a change of pitch, and each of those outputs stays within 10 ms of where it was against the picture throughout the move; the corrected output reaches its new offset no later than the move ends; a seek, file change or resume during a move sets the target picture delay and offsets at once instead, since picture and sound are re-aligned then anyway
>
> — `spec.md §5, AC-14, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `ChangeOffset(outputNumber, ±10)`: `OffsetBook.Step`; at limit → `NoteShown` limit text; else recompute the model — `src/DualPlayer.Core/Playback/PlaybackSession.cs`
- [ ] Playing: changed output's lag and, if PD moved, `PictureSync.MoveTo` + unchanged outputs' lag ramps at the same rate; a new press retargets
- [ ] Paused / ended: store targets; resume/seek applies them at once (T14 path)
- [ ] Emit `OffsetChanged`, `NoteShown` (`Output n: ±x ms`), `PictureDelayChanged` (cause `offset`) and a `PictureTiming` record
- [ ] Tests with the fake clock — `tests/DualPlayer.Core.Tests/Playback/PlaybackSessionOffsetTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Every output's assumed latency below zero | picture shown ahead of the clock; «later» presses take effect up to +500 (AC-10) |
| Press while a move is running | changes its target, no queue (AC-10) |
| Seek/resume during a move | targets set at once (AC-14) |
| Output key 3 with only two outputs | ignored (outputs numbered by the start command, at most three) |

## Definition of Done

- [ ] `PlaybackSessionOffsetTests` pass for AC-10, AC-11 and the offset half of AC-14
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
