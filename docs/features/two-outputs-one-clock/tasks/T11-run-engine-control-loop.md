---
id: T11
title: "Run the engine-control loop: start a run, stop it, and end it on file end or device loss"
layer: "app"
deps: ["T4", "T7", "T8", "T9", "T10"]
blocks: ["T12", "T13", "T19"]
acs: ["AC-01", "AC-04", "AC-06", "AC-06b", "AC-13"]
files_hint: ["src/DualPlayer.Core/Playback/IAudioEngine.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Playback/EngineControlLoop.cs", "src/DualPlayer.Core/CoreServiceCollectionExtensions.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
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

# T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss

## Place in the sequence

- **Blocked by:** T4 — Validate output assignments and resolve devices by list number or OS id, T7 — Implement the master clock, drift measurement, PI drift controller and clock-mismatch estimate, T8 — Define engine events, the excursion tracker and the run-summary builder, T9 — Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting, T10 — Fan one decoder out to the output rings with epochs, full-ring dropping and stall detection
- **Blocks:** T12 — Pause, resume and seek every output together with playback epochs, T13 — Publish per-device sync records, excursions and one-step corrections once per second, T19 — Console: list devices and tracks, and start a run from command-line arguments
- **Wave:** 4 — after its deepest dependency finishes (wave 3).
- **Lane:** shares `src/DualPlayer.Core/Playback/IAudioEngine.cs` with T12 — serialized; shares `src/DualPlayer.Core/Playback/EngineControlLoop.cs` with T13 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to start a file with each audio output assigned its own track and its own device
> **So that** each Viewer hears their own track from one shared start
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

> **As a** Viewer
> **I want** to keep hearing my track when the other Viewer's device disappears
> **So that** someone else's device problem never interrupts my viewing
>
> — `spec.md §4, US-05, verbatim` · full text: [spec.md](../spec.md)

This task joins every Core piece into a run that starts all outputs together, survives a lost device, and always ends with a run summary.

## Inlined context

> All commands and backend events enter the engine through one engine-control loop in `DualPlayer.Core` that handles them one at a time, so transport state never races. Every seek or stop bumps a playback **epoch**: frames carry the epoch they were decoded in, and a render source discards frames from an older epoch.
>
> — `sad.md §6, intro, abridged` · full text: [sad.md](../sad.md)

> **Chosen — Playback epochs with streams kept running:** one engine-control loop applies commands one at a time; seek and stop increment an epoch, the decoder tags frames with the epoch they belong to, render sources discard older-epoch frames and play silence until new frames arrive; pause and stop choose an effective instant on the master clock that lies beyond the audio already queued in every device (the largest queued amount among the devices, known from each device's written frames minus its reported position), and every render source switches to silence exactly at the frame that maps to that instant; device streams are never stopped during a run.
>
> — `adr/0006 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0006](../adr/0006-coordinate-pause-and-seek-with-playback-epochs-while-device-streams-keep-running.md)

> | Surviving output during another device's loss | 0 underruns within ±5 s of the device-lost event | fake-device test + manual disconnect on the target PC |
> | Start command to audio on all outputs | ≤ 2 s | timestamps in the log |
>
> — `spec.md §6, NFR rows 4–5, verbatim` · full text: [spec.md](../spec.md)

> | Number of outputs | ≥ 3 outputs simultaneously within the drift tolerance | test with fake devices (no third physical device is required) |
>
> — `spec.md §6, NFR row 7, verbatim` · full text: [spec.md](../spec.md)

> Core test with two fake devices where one raises device lost mid-run, asserting the other's underrun counter does not change within ±5 s
>
> — `sad.md §10, QG-2 How verify (a), verbatim` · full text: [sad.md](../sad.md)

> Buffer defaults, held in an `EngineOptions` record and overridable by console flags without rebuilding: ring buffer 2 s per output; start level 0.5 s in every ring before the master clock starts; requested shared-mode device buffer 40 ms (event-driven, about 10 ms device period).
>
> — `sad.md §7, Scaling thresholds, verbatim` · full text: [sad.md](../sad.md)

```text
Core->>Core: validate assignments (one audio output per device)
alt invalid file, track or device, or device reused
    Core-->>Console: refused with the invalid input and reason
else valid
    Core->>Backend: open each device in shared mode
    alt access denied by the OS
        Backend-->>Core: access denied for device
        Core->>Backend: close devices already opened
        Core-->>Console: refused, names the denied device
    else all devices open
        Backend-->>Core: mix format per device
        Core->>Media: open file, decode chosen tracks into device formats
        Media-->>Core: frames fill each output's ring buffer
        Core->>Core: start master clock when every ring holds its start level
        Backend->>Core: render request with device position
        Core-->>Backend: frames for that device
        Core-->>Console: run started, resolved device ids, clock position
```

— `sad.md §6, Critical flow 1 «start a run», sequence lines, abridged` · full text: [sad.md](../sad.md)

```text
Backend-->>Core: device lost on output B
Core->>Core: stop output B, detach its ring buffer from the decoder
Core->>Core: log device-lost event with device id and clock position
alt other outputs still playing
    Core-->>Console: output B lost, others continue
    Backend->>Core: render requests for output A continue unchanged
else it was the last playing output
    Core->>Core: stop master clock, end the run
    Core-->>Console: run summary for every output
```

— `sad.md §6, Critical flow 3 «one device lost», sequence lines, verbatim` · full text: [sad.md](../sad.md)

```text
alt the file ended
    S->>S: decoder reports end of file, outputs drain their rings
else Developer gives the stop command / Ctrl+C or the console window is closed
    C->>S: stop
S->>S: pick the stop instant beyond the audio queued in every device, new epoch
S-->>X: silence from the stop instant
S->>S: stop the master clock, build the run summary per output
S->>D: write the run summary
S-->>C: run summary
S->>X: close every device stream
```

— `sad.md §6, Flow 9 «end a run», sequence lines, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-01 — happy path

> **Given** the Developer has a media file with at least two tracks and two connected devices
> **When** the Developer starts playback with one track assigned to the first device and another track assigned to the second device
> **Then** both audio outputs start together from the beginning of the file, each Viewer hears the assigned track, and the console shows for every output its track, its device and the current master-clock position. The same track may be assigned to more than one audio output; only a device is exclusive (AC-05)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-04 — authorization

> **Given** one of the chosen devices is held exclusively by another application, or the operating system otherwise refuses the player access to it
> **When** the Developer starts playback
> **Then** no audio output starts and the console names that device and says the operating system denied the player access to it, so the Developer can free the device and retry
>
> — `spec.md §5, AC-04, verbatim` · full text: [spec.md](../spec.md)

### AC-06 — domain invariant

> **Given** two audio outputs are playing
> **When** the second output's device disappears (switched off, unplugged, out of range)
> **Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically. With three or more outputs, each further loss behaves the same way
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

### AC-06b — domain invariant (last device lost)

> **Given** only one audio output is still playing
> **When** its device disappears
> **Then** the run ends: the master clock stops and the console prints and the log records the run summary (AC-13), including every device-lost event of the run
>
> — `spec.md §5, AC-06b, verbatim` · full text: [spec.md](../spec.md)

### AC-13 — happy path

> **Given** a run has been playing
> **When** playback stops because the file ended, the Developer gave the stop command, pressed Ctrl+C, or closed the console window (a crash or a killed process need not produce a summary; the AC-11 records up to that point stay in the log)
> **Then** the console prints and the log records, for every audio output: its device, played duration, largest steady-state drift, estimated device clock mismatch in parts per million, number of corrections, number of underruns and any device-lost events. The clock mismatch is the device clock's rate relative to the master clock; a correction (CONTEXT) is counted only when the output's position is re-aligned in one step, so a steady run is expected to count 0, while the continuous rate adjustment is what AC-11 reports as current correction, in parts per million
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `IAudioEngine` facade: `StartRun(file, assignments, EngineOptions)` → `Result<RunStarted>` (resolved ids, conversion description, clock), `Stop(reason)`, `IObservable<EngineEvent> Events` — `src/DualPlayer.Core/Playback/IAudioEngine.cs`, `AudioEngine.cs`
- [ ] `EngineControlLoop`: one dedicated thread draining a `Channel` of commands and backend events in order; exceptions logged at the top of the loop — `EngineControlLoop.cs`
- [ ] Start: validate (T4) → open each device (on denial close those already opened, refuse naming the device) → open decode session with mix formats → start `DecodePump` → wait for the start level in every ring → start the master clock
- [ ] Log the start-command timestamp and each device's first rendered frame (start-to-audio)
- [ ] Device lost: stop that output, detach its ring, publish `DeviceLost` + log with device id and clock; last output → stop clock, end run `AllDevicesLost`
- [ ] Stop / file end: pick the stop instant beyond the largest device queue, silence at that frame, stop clock, build and publish `RunSummary`, log it, close every device
- [ ] Register the engine in `AddCore` — `src/DualPlayer.Core/CoreServiceCollectionExtensions.cs`
- [ ] Tests with fakes — `tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Second device denies access | first device closed; nothing plays; error names the second device |
| Invalid assignment | refused before any `Open` call |
| Device lost while another plays | survivor's underrun counter unchanged within ±5 s; clock keeps running; no reconnect |
| Last device lost | run ends with `AllDevicesLost` summary including every device-lost event |
| Three outputs | all start together from the same clock position |

## Definition of Done

- [ ] `AudioEngineRunTests` pass for AC-01, AC-04, AC-06, AC-06b and AC-13 with fake devices, including the QG-2 ±5 s underrun assertion and a 3-output start
- [ ] start-to-audio with fakes is logged and ≤ 2 s on the fake clock
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
