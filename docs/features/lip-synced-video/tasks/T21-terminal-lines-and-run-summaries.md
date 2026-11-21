---
id: T21
title: "Print start lines, warnings, running lines and run summaries to the terminal and the log"
layer: "ports"
deps: ["T13", "T19"]
blocks: ["T22"]
acs: ["AC-06", "AC-12", "AC-13"]
files_hint: ["src/DualPlayer.App/Startup/TerminalPrinter.cs", "src/DualPlayer.App/Startup/RunSummaryLines.cs", "tests/DualPlayer.App.Tests/Startup/TerminalPrinterTests.cs"]
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

# T21 — Print start lines, warnings, running lines and run summaries to the terminal and the log

## Place in the sequence

- **Blocked by:** T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events, T19 — Parse and validate the play start command in the App executable before any window opens
- **Blocks:** T22 — Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code
- **Wave:** 7 — after its deepest dependency finishes (wave 6).
- **Lane:** own lane.

## Why (user story)

> **As a** Developer
> **I want** to measure, per output, how far the sound is from the picture with a click-and-flash test video filmed by a phone, and see in the log which latency, offset and picture delay the player used
> **So that** I can accept this step on measurements of what the Viewer actually hears, not only on the player's own numbers
>
> — `spec.md §4, US-07, verbatim` · full text: [spec.md](../spec.md)

This task gives the Developer the terminal record — latency used, offsets, and a run summary at every end — that the acceptance evidence is paired with.

## Inlined context

> Normal output goes to standard output; errors go to standard error as `error <code>: <plain-language message>`, with the same `code` as the Core `Error(Code, Message)` record (`src/DualPlayer.Core/Result.cs`). Every printed line is also written to the log.
>
> — `contracts/cli.md §Conventions, output, verbatim` · full text: [cli.md](../contracts/cli.md)

> Times are master-clock positions formatted `hh:mm:ss.fff`; latency, offset and picture delay are integer milliseconds with a sign; a positive offset makes that output's sound heard later (`CONTEXT.md` Offset).
>
> — `contracts/cli.md §Conventions, units, verbatim` · full text: [cli.md](../contracts/cli.md)

> Run-summary printing reuses the step-3 summary builder; the console renderer for it is shared or copied into App, a choice left to `tasks`.
>
> — `adr/0001 §Consequences, neutral, verbatim` · full text: [ADR-0001](../adr/0001-host-the-start-command-in-the-app-executable.md)

**Decision (this breakdown):** copy the step-3 run-summary formatting into `App/Startup/RunSummaryLines.cs`; `DualPlayer.Console` stays untouched (ADR-0001 positive consequence) and `Core/Diagnostics/` is not edited (sad §5).

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

```
output 1  track 1  device Test Headset A  type bluetooth-headphones  reported 0  default 200  offset +40  assumed 160  lag 40
output 2  track 2  device Test Headset B  type bluetooth-headphones  reported 0  default 200  offset 0    assumed 200  lag 0
picture   delay 200  screen 1  1920x1080
started   clock 00:00:00.000  first-frame 1.9 s  first-audio 1.9 s 1.9 s
```

If the remembered-offsets file cannot be read, the App prints `warning state.unreadable: remembered offsets could not be read, every output starts at offset 0` (or `state.invalid` / `state.too-new`) and continues (spec §6.1). Out-of-range or off-grid values print `warning offsets.clamped: device {…} offset 900 -> 500` per value.

```
run-summary  file <file name>  reason end|exit|file-change|last-output-lost|picture-failed
```

```
skipped  <file name>  reason track-missing  output 2  track 2
next     <file name>
device-lost      output 2  device {…0002}  clock 00:40:12.300  others continue  picture-delay 200 -> 0
device-lost      output 1  device {…0001}  clock 00:41:00.000  last output, film ended
picture-failed   clock 01:10:05.120  the picture could not be decoded further, film ended
```

— `contracts/cli.md §1.1, §2.1–§2.3, abridged` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

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

- [ ] `TerminalPrinter`: subscribe to the merged engine + session event streams, format each line per `cli.md`, write stdout (stderr for errors) and the log; never block the control loop (queue like the step-3 `ConsoleLineQueue`) — `src/DualPlayer.App/Startup/TerminalPrinter.cs`
- [ ] `RunSummaryLines`: copy of the step-3 run-summary formatting, preceded by the `run-summary` header — `src/DualPlayer.App/Startup/RunSummaryLines.cs`
- [ ] Tests from recorded events to exact lines — `tests/DualPlayer.App.Tests/Startup/TerminalPrinterTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Terminal write fails (closed pipe) | printing stops, playback and log continue (as step 3) |
| Offset 0 | printed without sign (`offset 0`), others signed |
| Run summary at a file change | covers the file's playback since it was opened (AC-06) |

## Definition of Done

- [x] `TerminalPrinterTests` pass for every line form above
- [ ] `DualPlayer.Console` unchanged
- [x] every Hard Rule inlined above still holds
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
