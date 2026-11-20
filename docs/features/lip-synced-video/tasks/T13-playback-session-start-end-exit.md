---
id: T13
title: "Start a lip-synced playback session, end it at the natural end or on exit, and emit session events"
layer: "app"
deps: ["T3", "T6", "T7", "T12"]
blocks: ["T14", "T17", "T18", "T21"]
acs: ["AC-01", "AC-06", "AC-12", "AC-13"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/SessionEvent.cs", "src/DualPlayer.Core/Playback/StartRequest.cs", "src/DualPlayer.Core/CoreServiceCollectionExtensions.cs", "tests/DualPlayer.Core.Tests/Fakes/SessionRig.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionStartTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "M"
status: "blocked"
step: "GATE red after escalation: AC_01 test has wrong picture-start moment"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events

## Place in the sequence

- **Blocked by:** T3 — Steer the picture to clock minus picture delay with a Core picture-sync loop, T6 — Model device types, type defaults, assumed latency, picture delay and lags behind a latency-probe port, T7 — Persist per-device offsets in device-offsets.json and keep them in an offset book with clamp, step and debounced save, T12 — Give the audio engine per-output start lags in the pre-roll and lag targets that ramp or jump
- **Blocks:** T14 — Pause, resume and seek picture and every output together in the session, T17 — Move to the next or previous playable file in the folder, skipping files that cannot play, T18 — Skip damaged frames, end on an unrecoverable picture, and log picture timing and frame counters, T21 — Print start lines, warnings, running lines and run summaries to the terminal and the log
- **Wave:** 6 — after its deepest dependency finishes (wave 5).
- **Lane:** shares `src/DualPlayer.Core/CoreServiceCollectionExtensions.cs` with T12 — serialized; shares `src/DualPlayer.Core/Playback/PlaybackSession.cs` with T14, T15, T16, T17, T18 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

> **As a** Viewer
> **I want** the player to estimate each device's latency from what it knows about the device
> **So that** the first film with a new device is already close to lip-synced before I touch anything
>
> — `spec.md §4, US-05, verbatim` · full text: [spec.md](../spec.md)

This task builds the session that starts a film with picture and every output lip-synced and ends it cleanly — the spine every later key and event hangs on.

## Inlined context

> The session runs on the step-3 engine control loop (two-outputs-one-clock ADR-0007), so transport commands, offset changes, device loss and picture-sync ticks are serialized on one thread and never touch a render thread or the UI thread. New session events travel on their own `SessionEvent` stream in `Core/Playback/`, merged with the engine events by the host, so `Core/Diagnostics/` (step 6) is not edited.
>
> — `sad.md §5, intro, abridged` · full text: [sad.md](../sad.md)

> `PlaybackSession.cs` — use case: start, pause/resume, seek, next/previous file, offset key, exit; drives IAudioEngine + IVideoRenderer on the engine control loop
> `SessionEvent.cs` — session event stream (picture delay, latency estimate, picture timing, skipped frames, notes) merged with the engine events — Core/Diagnostics untouched
> `StartRequest.cs` — file, outputs (track → device), screen; validation results
>
> — `sad.md §5, internal decomposition, Playback/, verbatim` · full text: [sad.md](../sad.md)

> Flow 1, Core part: probe reported latency, connection kind and form factor per device → load remembered offsets → assumed latency per output, picture delay, lag per output, logged → open fullscreen on the chosen screen → load the file paused at the start position → silent pre-roll on every output, then release with each output's lag → slowest output hears the first frame → master clock starts → play the picture at clock time 0 plus picture delay (negative: ahead, timed from the pre-roll measurement) → picture timing against due time, logged.
> Flow 6: film reaches its end → picture stays on its last frame, every output falls silent together → state ended → run summary printed and logged for every output; exit key → every output falls silent together, renderer closed → run summary.
>
> — `sad.md §6, flows 1 and 6, abridged` · full text: [sad.md](../sad.md)

> Timeline used by every flow (ADR-0004, ADR-0005): output *i* has assumed latency `Aᵢ`, the picture delay is `PD = max Aᵢ`, and output *i* follows `clock − Dᵢ` with `Dᵢ = PD − Aᵢ`. Content position *p* is then heard on every output, and shown on screen, at master-clock time `p + PD`.
>
> — `sad.md §6, intro, abridged` · full text: [sad.md](../sad.md)

| Event | Fields | When |
|---|---|---|
| `LatencyEstimated` | per output: device id, type, reported, default, offset, assumed, lag; picture delay | every start and file change (AC-13, AC-12) |
| `PictureDelayChanged` | clock, cause (`offset`, `device-lost`, `seek`, `pause`, `resume`, `file-change`), old and new picture delay, per output assumed and lag | AC-17 |
| `OffsetChanged` | output, device id, old and new offset, saved-at | AC-10, AC-11 |
| `PictureTiming` | clock, frame due time, presented time, error ms | every event above and once per second (AC-17) |
| `FrameCounters` | late (> 20 ms after due), dropped, skipped-damaged, per minute | spec §6 picture presentation, AC-06b |
| `StartTimings` | start command, first frame presented, first audio heard per output | spec §6 start budget |

— `contracts/cli.md §3, log records, verbatim` · full text: [cli.md](../contracts/cli.md)

| Aspect | Target | Measurement |
|---|---|---|
| Start command to picture and sound | ≤ 3 s, picture and every output together (step 3 budget, ADR-0008); "together" means that from the first frame on, the picture is presented within 20 ms of due and every output is within 10 ms of its due position | timestamps in the log: start command, first frame presented, first audio heard per output (logged as the moment the first sound is given to the device plus its assumed latency; what is actually heard is proven by AC-16) |

— `spec.md §6, NFR row start, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule:** The playback session, picture sync and lag ramps run on the step-3 engine control loop. mpv property and end events arrive on mpv's event thread; the adapter publishes immutable snapshots without locks the control loop could wait on. Render threads are never blocked. UI updates marshal through `Dispatcher.UIThread`. The picture probe runs off the UI thread with a time limit
>
> — `sad.md §8, Threading, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** No `DateTime.Now` / `Stopwatch` in Core — time comes from an injected `TimeProvider` or the audio master clock, so picture and latency logic is testable with a fake clock.
>
> — `sad.md §2, Conventions, time, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface of its own; this task defines the `SessionEvent` records behind `contracts/cli.md §3` (all six above, plus the note, skipped-file, device-lost and picture-failed events later tasks raise) so T21 can print them.

## Acceptance criteria

### AC-01 — happy path

> **Given** a video file with a picture and at least two audio tracks, and the Developer's start command names the file, a track and a device for each Viewer, and a screen
> **When** the start command is given
> **Then** the film opens fullscreen on the named screen (on the main screen when none is named), the picture and every output's sound start together within the start budget (§6), and each Viewer hears their own track; on a device whose offset was set in an earlier run (AC-12) the sound is in step with the lips within the lip-sync budget (§6), on a device's first use the error is only measured (§7)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-06 — happy path

> **Given** a film is playing
> **When** it reaches its end, or a Viewer presses the exit key
> **Then** the picture stops on its last frame at the end of the film, or the window closes on exit, every output falls silent together, and the run summary of step 3 is printed and logged for every output; at the end of the film the pause key does nothing (a backward seek or a file change continues), and a run summary is printed at every natural end, exit and file change, each covering the file's playback since it was opened
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

### AC-12 — cross-context

> **Given** a device had an offset remembered in an earlier run
> **When** a film is started with that device on any output
> **Then** the output starts with the remembered offset, and the Developer's log shows the device, the remembered offset and the latency used; only the offset is remembered — the latency is worked out again at every start (reported part plus the current type default, AC-13), so a revised type default (§8) also shifts devices that already have a remembered offset; a type default is revised only together with an acceptance measurement (§7), the Viewers then re-correct each device of that type once, and AC-01 and the §7 offset-corrections KPI count from the last revision
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

### AC-13 — happy path

> **Given** a device that has never been used by the player
> **When** a film is started on it
> **Then** its output's assumed latency is the reported part — the latency the operating system reports for the device beyond the device's own reported playback position, which the master clock already accounts for (0 ms when the operating system reports nothing beyond it) — plus the default for its device type (Bluetooth headphones, Bluetooth speaker, wired, HDMI, other — taken from the connection kind and form factor the operating system reports, never from the device name; any Bluetooth connection counts as Bluetooth headphones unless reported as a speaker, USB counts as wired, audio to a display over HDMI or DisplayPort counts as HDMI, anything not recognised as other), its offset is 0, and the Developer's log shows the reported part, the type default, the type it was taken as, and the resulting picture delay
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `StartRequest` (file, outputs track → device id, optional screen) — `src/DualPlayer.Core/Playback/StartRequest.cs`
- [ ] `SessionEvent` records: the six log records of `cli.md §3` plus `NoteShown`, `FileSkipped`, `FileChanged`, `NoNeighbour`, `OutputLost`, `PictureFailed` — `src/DualPlayer.Core/Playback/SessionEvent.cs`
- [ ] `PlaybackSession.Start`: probe each device, load offsets, compute the model, emit `LatencyEstimated`, load the renderer paused, start the engine with initial lags, align the picture through `PictureSync` (negative PD from the pre-roll instant), emit `StartTimings`; every command runs on the engine control loop — `src/DualPlayer.Core/Playback/PlaybackSession.cs`
- [ ] States playing / paused / ended; natural end (engine end + renderer end of file) → ended, last frame held; `Exit()` → stop engine (run summary) and renderer; pause ignored while ended
- [ ] Register the session in `AddCore` — `src/DualPlayer.Core/CoreServiceCollectionExtensions.cs`
- [ ] `SessionRig` + tests — `tests/DualPlayer.Core.Tests/Fakes/SessionRig.cs`, `tests/DualPlayer.Core.Tests/Playback/PlaybackSessionStartTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Remembered offsets unreadable | start with offsets 0 and pass the `state.*` warning on (T7); start continues |
| Renderer load fails after validation | `Result` error, engine not started, nothing plays |
| Device's first use | offset 0, latency = reported + type default (AC-13) |
| Pause key while ended | does nothing (AC-06) |

## Definition of Done

- [ ] `PlaybackSessionStartTests` pass for each AC branch listed above
- [ ] no file in `Core/Diagnostics/` or `Core/Outputs/` changed
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
