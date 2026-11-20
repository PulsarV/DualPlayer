---
id: T16
title: "Keep playing when an output's device is lost, and end the film when the last one is lost"
layer: "app"
deps: ["T15"]
blocks: ["T22"]
acs: ["AC-15", "AC-15b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionDeviceLossTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T16 — Keep playing when an output's device is lost, and end the film when the last one is lost

## Place in the sequence

- **Blocked by:** T15 — Apply offset keys per output and move picture and unchanged outputs to a new picture delay together
- **Blocks:** T22 — Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code
- **Wave:** 9 — after its deepest dependency finishes (wave 8).
- **Lane:** shares `src/DualPlayer.Core/Playback/PlaybackSession.cs` with T13, T14, T15, T17, T18 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** the picture to follow smoothly when the slowest output changes — its offset is corrected or its device is lost
> **So that** neither Viewer sees the picture jump or loses lip-sync for longer than a moment
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task makes one Viewer's headset dropping out a short note for everyone instead of a broken film.

## Inlined context

> Flow 2, device-lost branch: device lost → drop the output, keep the others playing → note with the lost output → new picture delay and new lags → picture speed set to at most 0.2 percent off normal until the new delay is reached; unchanged outputs ramp their lag at the same rate.
> Flow 6, last output: device lost on the only output left → picture freezes, stop as at the natural end → note that the last output was lost → brief note for 2 s, window stays open → run summary printed and logged for every output.
>
> — `sad.md §6, flows 2 and 6, device-lost branches, abridged` · full text: [sad.md](../sad.md)

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| output lost | A device lost while others remain (AC-15, flow 2) | `OsdNote` — `Output 2 lost` | wireframe below |
| last output lost | The only remaining device lost (AC-15b, flow 6) | `OsdNote` — `Output 1 lost - last output, film ended` | wireframe below |

— `screens.md SCR-03, rows output lost + last output lost, verbatim` · full text: [screens.md](../screens.md)

> The session runs on the step-3 engine control loop (two-outputs-one-clock ADR-0007), so transport commands, offset changes, device loss and picture-sync ticks are serialized on one thread and never touch a render thread or the UI thread. New session events travel on their own `SessionEvent` stream in `Core/Playback/`, merged with the engine events by the host, so `Core/Diagnostics/` (step 6) is not edited.
>
> — `sad.md §5, intro, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-15 — happy path

> **Given** a film is playing on two outputs
> **When** one output's device is lost
> **Then** the picture and the other output keep playing (step 3 AC-06), the picture follows the remaining outputs' largest assumed latency as in AC-14, and the loss is shown briefly in the window
>
> — `spec.md §5, AC-15, verbatim` · full text: [spec.md](../spec.md)

### AC-15b — error

> **Given** a film is playing and only one output is left
> **When** that output's device is lost
> **Then** the film stops as at its natural end (AC-06): the picture freezes, the window stays open, the loss is shown briefly in the window, the run summary is printed and logged for every output, and the next-file, previous-file and exit keys still work
>
> — `spec.md §5, AC-15b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Subscribe to the engine's `DeviceLost` event on the control loop; recompute the model without that output; move PD as in T15 — `src/DualPlayer.Core/Playback/PlaybackSession.cs`
- [ ] Last output lost → ended with the picture frozen, run summary (reason `last-output-lost`), `OutputLost` + note
- [ ] Tests — `tests/DualPlayer.Core.Tests/Playback/PlaybackSessionDeviceLossTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Lost output was not the slowest | PD unchanged; only the note and the event (AC-15) |
| Offset key for a lost output | ignored |
| Loss during a picture-delay move | retargets the move to the new PD |

## Definition of Done

- [ ] `PlaybackSessionDeviceLossTests` pass for AC-15 and AC-15b
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
