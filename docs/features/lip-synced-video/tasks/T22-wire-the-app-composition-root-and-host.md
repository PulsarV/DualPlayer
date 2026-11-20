---
id: T22
title: "Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code"
layer: "wiring"
deps: ["T10", "T16", "T18", "T20", "T21"]
blocks: ["T23"]
acs: ["AC-01", "AC-17"]
files_hint: ["src/DualPlayer.App/CompositionRoot.cs", "src/DualPlayer.App/Program.cs", "src/DualPlayer.App/App.axaml.cs", "src/DualPlayer.App/Startup/AvaloniaScreenCatalog.cs", "tests/DualPlayer.App.Tests/CompositionRootSmokeTests.cs"]
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

# T22 — Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code

## Place in the sequence

- **Blocked by:** T10 — Report each device's connection kind, form factor and hidden latency on Windows and the dev Mac, T16 — Keep playing when an output's device is lost, and end the film when the last one is lost, T18 — Skip damaged frames, end on an unrecoverable picture, and log picture timing and frame counters, T20 — Build the fullscreen playback window hosting mpv's video, mapping the fixed keys and styling notes from tokens, T21 — Print start lines, warnings, running lines and run summaries to the terminal and the log
- **Blocks:** T23 — Measure lip-sync per output on the target PC with a click-and-flash video and record the acceptance evidence
- **Wave:** 10 — after its deepest dependency finishes (wave 9).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task joins everything into the one executable the Developer runs: validation first, then the window and the session, with every event in the log.

## Inlined context

> **Hard rule:** **Module wiring:** each module exposes `IServiceCollection Add<Module>(this IServiceCollection)`; only `src/DualPlayer.App/CompositionRoot.cs` and `src/DualPlayer.Console/ConsoleCompositionRoot.cs` (the only two composition roots) call them.
> **Platform selection:** runtime `OperatingSystem.IsWindows()` / `IsMacOS()` in the composition root.
>
> — `CLAUDE.md §Conventions, module wiring + platform selection, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> `CompositionRoot.cs` — *changed*: registers session, probes, renderer; platform probe by OS
>
> — `sad.md §5, internal decomposition, App/CompositionRoot.cs, verbatim` · full text: [sad.md](../sad.md)

> Logging | `ILogger<T>` everywhere; Serilog rolling file under app-data `logs/`. New structured events from the `SessionEvent` stream: latency estimated (per output: device, type taken, reported part, type default, offset, assumed latency, lag), picture delay changed, picture timing (presented vs due), per-minute frame counters (late, dropped, skipped damaged). The App host writes engine and session events to the log and prints the run summary to the terminal
>
> — `sad.md §8, Logging, verbatim` · full text: [sad.md](../sad.md)

| Code | Meaning | Source |
|---|---|---|
| `0` | The window was closed with Esc (or the window was closed) after a film played, whatever ended it | AC-06, AC-15b |
| `1` | Unexpected failure (a bug); logged with its exception | `CLAUDE.md` Errors |
| `2` | Refused before start: usage, unreadable file, no picture, not hardware-decodable, track, device, too many outputs, screen not found, video renderer missing | AC-02, AC-03b, step-3 §3 |
| `4` | Refused before start: the OS denied access to the file or a device | AC-03, step-3 AC-04 |

— `contracts/cli.md §4, exit codes, verbatim` · full text: [cli.md](../contracts/cli.md)

> **Hard rule:** The playback session, picture sync and lag ramps run on the step-3 engine control loop. mpv property and end events arrive on mpv's event thread; the adapter publishes immutable snapshots without locks the control loop could wait on. Render threads are never blocked. UI updates marshal through `Dispatcher.UIThread`. The picture probe runs off the UI thread with a time limit
>
> — `sad.md §8, Threading, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Uses `contracts/cli.md` §1.1, §4 as implemented by T19/T21; adds no new surface.

## Acceptance criteria

### AC-01 — happy path

> **Given** a video file with a picture and at least two audio tracks, and the Developer's start command names the file, a track and a device for each Viewer, and a screen
> **When** the start command is given
> **Then** the film opens fullscreen on the named screen (on the main screen when none is named), the picture and every output's sound start together within the start budget (§6), and each Viewer hears their own track; on a device whose offset was set in an earlier run (AC-12) the sound is in step with the lips within the lip-sync budget (§6), on a device's first use the error is only measured (§7)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-17 — happy path

> **Given** a film is playing
> **When** the picture delay or any output's latency or offset changes, or a seek, pause, resume or file change happens
> **Then** the log records the master-clock position, the new picture delay, each output's assumed latency split into reported part and type default, and its offset, and how far behind or ahead the picture was presented compared with when it was due
>
> — `spec.md §5, AC-17, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Register `PlaybackViewModel`, `StartCommand`, `TerminalPrinter`, `IScreenCatalog` → `AvaloniaScreenCatalog`; platform `IDeviceLatencyProbe` by OS — `src/DualPlayer.App/CompositionRoot.cs`
- [ ] `AvaloniaScreenCatalog`: numbered screens, resolution, main flag, without showing a window — `src/DualPlayer.App/Startup/AvaloniaScreenCatalog.cs`
- [ ] `Program.Main`: `play` → parse/validate → on error print + exit code; else start Avalonia, open `PlaybackWindow` on the screen, hand the `VideoHost` handle to the renderer, start the session, write engine + session events to the log, exit 0 when the window closes; unexpected exception → log + exit 1 — `src/DualPlayer.App/Program.cs`, `src/DualPlayer.App/App.axaml.cs`
- [ ] Update the smoke test — `tests/DualPlayer.App.Tests/CompositionRootSmokeTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| No `play` argument | `cli.usage`, exit 2, no window |
| Validation fails | no window ever created (AC-02, AC-03b) |
| Window closed by the OS / Alt+F4 | same as Esc: run summary, exit 0 |
| Keys after the native child window is shown | still reach the Avalonia window (sad §11 focus risk — check manually) |

## Definition of Done

- [ ] `CompositionRootSmokeTests` pass
- [ ] manual dev-Mac run plays a sample and exits 0 with a run summary
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
