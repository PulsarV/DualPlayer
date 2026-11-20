---
id: T3
title: "Steer the picture to clock minus picture delay with a Core picture-sync loop"
layer: "domain"
deps: ["T1"]
blocks: ["T5", "T13"]
acs: ["AC-01", "AC-14", "AC-17"]
files_hint: ["src/DualPlayer.Core/Sync/PictureSync.cs", "tests/DualPlayer.Core.Tests/Sync/PictureSyncTests.cs"]
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

# T3 — Steer the picture to clock minus picture delay with a Core picture-sync loop

## Place in the sequence

- **Blocked by:** T1 — Define the IVideoRenderer port and a scripted fake renderer in Core
- **Blocks:** T5 — Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict, T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events
- **Wave:** 2 — after its deepest dependency finishes (wave 1).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** the picture to follow smoothly when the slowest output changes — its offset is corrected or its device is lost
> **So that** neither Viewer sees the picture jump or loses lip-sync for longer than a moment
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task builds the Core loop that keeps the presented picture on `clock − PD` and moves it smoothly when the picture delay changes — the part the spike (T5) measures and the session (T13+) reuses.

## Inlined context

> **libmpv, slaved by a Core loop through a renderer port.** mpv decodes in hardware with its audio off. A Core picture-sync loop compares mpv's presented position with `clock − picture delay` and steers `speed`, with seek for large errors. Pause, resume, seek and start are aligned explicitly. A spike on the target PC verifies the precision first.
>
> — `adr/0002 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0002](../adr/0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port.md)

> A new `IVideoRenderer` port exposes load, play-at, pause, seek, speed and the presented position. A Core picture-sync loop reads that position and steers mpv's `speed`, so the sync logic is tested with a fake clock (QG-1, QG-2). The master clock and mpv's own clock both run on the OS monotonic time base, so the loop mostly handles start, pause, resume, seek and the 0.2 % picture-delay moves, not long-run drift.
>
> — `sad.md §4, strategic choice 1, abridged` · full text: [sad.md](../sad.md)

> Timeline used by every flow (ADR-0004, ADR-0005): output *i* has assumed latency `Aᵢ`, the picture delay is `PD = max Aᵢ`, and output *i* follows `clock − Dᵢ` with `Dᵢ = PD − Aᵢ`. Content position *p* is then heard on every output, and shown on screen, at master-clock time `p + PD`.
>
> — `sad.md §6, intro, abridged` · full text: [sad.md](../sad.md)

> Flow 1 (start), picture branch:
> - picture delay zero or positive → play from the start at clock time 0 plus picture delay
> - picture delay negative → play from the start the size of the picture delay before the slowest output hears frame 0, timed from the pre-roll measurement
> - presented position → picture timing against due time, logged
> Flow 2 (picture delay changed): picture speed set to at most 0.2 percent off normal until the new delay is reached
>
> — `sad.md §6, flows 1–2, picture steps, abridged` · full text: [sad.md](../sad.md)

| Aspect | Target | Measurement |
|---|---|---|
| Recovery after pause, resume, seek or file change | picture and every output lip-synced again ≤ 1 s after the command (file change: within the start budget); "lip-synced again" = the picture presented within 20 ms of due and every output within 10 ms of its due position | log: command time, the first picture presentation within 20 ms of due, and each output's position error; unit tests with a fake clock |
| Picture-delay change during playback | picture and unchanged outputs move together at ≤ 0.2 % speed change (about 5 s per 10 ms of change), each within 10 ms of where it was against the picture throughout | log of picture presentation against due time and of playback speed; unit tests with a fake clock |

— `spec.md §6, NFR rows recovery + picture-delay change, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule:** No `DateTime.Now` / `Stopwatch` in Core — time comes from an injected `TimeProvider` or the audio master clock, so picture and latency logic is testable with a fake clock.
>
> — `sad.md §2, Conventions, time, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** Ports and adapters: `DualPlayer.Core` references only the BCL and `Microsoft.Extensions.*.Abstractions` — no OS, UI or native library; OS code lives in `DualPlayer.Platform.Windows` / `.MacOS`, codec and video code in `DualPlayer.Media`.
>
> — `sad.md §2, Technical, ports and adapters, abridged` · full text: [sad.md](../sad.md)

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

### AC-17 — happy path

> **Given** a film is playing
> **When** the picture delay or any output's latency or offset changes, or a seek, pause, resume or file change happens
> **Then** the log records the master-clock position, the new picture delay, each output's assumed latency split into reported part and type default, and its offset, and how far behind or ahead the picture was presented compared with when it was due
>
> — `spec.md §5, AC-17, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `PictureSync` takes the master-clock reading (via an injected clock/`TimeProvider` abstraction), the renderer and the current picture delay — `src/DualPlayer.Core/Sync/PictureSync.cs`
- [ ] `AlignAt(contentPosition, clockTime)` for start/resume/seek: command the renderer so the frame for *p* is presented at `p + PD`; for `PD < 0` the play command is issued `|PD|` before the slowest output hears frame 0 (caller passes that instant)
- [ ] `Tick()`: error = presented − due; small error → steer `speed` within ±0.2 %; error too large for a speed move → seek (same rule as ADR-0002 «seek for large errors»); return a `PictureTiming` value (due, presented, error ms) for the log
- [ ] `MoveTo(newPd)`: run speed at exactly ≤ 0.2 % off normal until the new delay is reached, then back to 1.0; a new target during a move retargets; `SetAtOnce(newPd)` for seek, file change, resume
- [ ] Tests with `FakeTimeProvider` + `FakeVideoRenderer` for each AC branch — `tests/DualPlayer.Core.Tests/Sync/PictureSyncTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| New target while a move is under way | retargets from the current delay; no queueing (AC-10 «further presses … change its target») |
| Seek, file change or resume during a move | target set at once, no ramp (AC-14) |
| Renderer reports no frame yet | no correction; no timing record |
| Picture delay negative | picture presented ahead of the master clock (AC-10: no lower limit) |

## Definition of Done

- [ ] `PictureSyncTests` pass for steady, start, resume, seek, positive and negative PD, a 10 ms move (≈ 5 s, speed within 1 ± 0.002) and a retarget mid-move
- [ ] no `DateTime.Now` / `Stopwatch` in the new code
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
