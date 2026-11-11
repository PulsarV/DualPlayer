---
id: T21
title: "Console: print sync records, excursions, device loss and the run summary, also on Ctrl+C and window close"
layer: "ports"
deps: ["T19", "T13"]
blocks: ["T22"]
acs: ["AC-06", "AC-06b", "AC-11", "AC-13"]
files_hint: ["src/DualPlayer.Console/Rendering/EventPrinter.cs", "src/DualPlayer.Console/Rendering/RunSummaryPrinter.cs", "src/DualPlayer.Console/ShutdownSignals.cs", "src/DualPlayer.Console/Program.cs", "tests/DualPlayer.Console.Tests/EventPrinterTests.cs"]
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

# T21 — Console: print sync records, excursions, device loss and the run summary, also on Ctrl+C and window close

## Place in the sequence

- **Blocked by:** T19 — Console: list devices and tracks, and start a run from command-line arguments, T13 — Publish per-device sync records, excursions and one-step corrections once per second
- **Blocks:** T22 — Record the ≥ 3 h evidence run on the target Windows PC and commit its run summary
- **Wave:** 6 — after its deepest dependency finishes (wave 5).
- **Lane:** shares `src/DualPlayer.Console/Program.cs` with T2 — serialized; shares `src/DualPlayer.Console/Program.cs` with T19 — serialized.

## Why (user story)

> **As a** Developer
> **I want** drift, correction and underrun counters recorded separately for every device
> **So that** roadmap steps 4–6 and the crackling investigation work from data instead of guesses
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

> **As a** Developer
> **I want** a per-output summary of the run when playback stops
> **So that** I can record the target-PC measurements in the repository and close this step on evidence
>
> — `spec.md §4, US-08, verbatim` · full text: [spec.md](../spec.md)

This task shows the Developer every engine event live and guarantees the run summary on every normal way a run ends.

## Inlined context

> Flow 9: closing the console window on Windows leaves the process only a few seconds to shut down; writing the run summary must fit in that window — note for `tasks`.
>
> — `sad.md §6, Flags for later stages, verbatim` · full text: [sad.md](../sad.md)

> **Chosen:** option 1 (new `src/DualPlayer.Console` executable with its own composition root calling the same `Add<Module>()` extensions). A real console executable gets native Ctrl+C and close-window signals for AC-13, carries no Avalonia dependency, and keeps the console code out of the couch-UI assembly, while reusing every module registration as-is.
> - A second composition root: the CLAUDE.md rule changes to "`DualPlayer.App` and `DualPlayer.Console` are the only composition roots", and the two can drift in what they register.
> - Drift between the two roots is guarded by a second smoke test (`tests/DualPlayer.Console.Tests/`): the container builds and resolves the console's command loop.
>
> — `adr/0001 §Decision outcome + §Consequences, abridged` · full text: [ADR-0001](../adr/0001-host-the-developer-console-as-a-separate-executable.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

> ```
> record  clock 00:12:34.000  device {…0001}  drift_ms +1.4  correction_ppm -35  underruns 0
> excursion  device {…0002}  start 01:02:03.000  duration_ms 850  peak_ms +14.2
> device-lost  output 2  device {…0002}  clock 00:40:12.300  others continue
> ```
> When it was the last playing output: `device-lost … last output, run ends`, followed by the run summary; exit `3`.
> ```
> run summary  ended file-end  clock 01:52:10.000
> output  device           played     max_drift_ms  clock_mismatch_ppm  corrections  underruns  device_lost
> 1       {…0001}  01:52:10.0  8.7           +41.2               0            0          -
> 2       {…0002}  00:40:12.3  9.1           -18.5               0            0          00:40:12.300
> ```
> `ended` is one of `file-end`, `stop`, `interrupt` (Ctrl+C or window closed), `seek-past-end`, `all-devices-lost`. `max_drift_ms` is the largest steady-state drift outside the recovery windows; `corrections` counts one-step re-alignments only.
> Times are master-clock positions formatted `hh:mm:ss.fff`. Drift is in milliseconds with a sign (`+` = the output is ahead of the master clock). Rates are in parts per million (ppm). Every line printed during a run is also written to the log (spec AC-11, AC-13).
> Ctrl+C and closing the console window behave like `stop` (AC-13). A crash or killed process prints no summary; the records up to that point stay in the log.
>
> — `contracts/cli.md §2.1–2.4 + Conventions + §1.4 last paragraph, abridged` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

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

### AC-11 — cross-context

> **Given** a run is in progress on one or more devices
> **When** playback continues
> **Then** once per second the system publishes, for every device, a record of its current drift, current correction and cumulative underruns, tied to that device's operating-system id and the master-clock position; each record is both raised as an engine event that other parts of the player can subscribe to and written to the log, so roadmap step 6 (self-diagnostics) can subscribe to these records without any change to the engine
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

### AC-13 — happy path

> **Given** a run has been playing
> **When** playback stops because the file ended, the Developer gave the stop command, pressed Ctrl+C, or closed the console window (a crash or a killed process need not produce a summary; the AC-11 records up to that point stay in the log)
> **Then** the console prints and the log records, for every audio output: its device, played duration, largest steady-state drift, estimated device clock mismatch in parts per million, number of corrections, number of underruns and any device-lost events. The clock mismatch is the device clock's rate relative to the master clock; a correction (CONTEXT) is counted only when the output's position is re-aligned in one step, so a steady run is expected to count 0, while the continuous rate adjustment is what AC-11 reports as current correction, in parts per million
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `EventPrinter` subscribes to `IAudioEngine.Events` and prints `record`, `excursion`, `device-lost` lines — `src/DualPlayer.Console/Rendering/EventPrinter.cs`
- [ ] `RunSummaryPrinter` prints the summary table with `ended` reason — `RunSummaryPrinter.cs`
- [ ] `ShutdownSignals`: Ctrl+C (`Console.CancelKeyPress`, cancel the kill) and window close (`PosixSignalRegistration` for `SIGTERM`/`SIGHUP`, which .NET maps from console close on Windows) → `Stop(Interrupt)`; wait for the summary, flush Serilog, exit `0` — `ShutdownSignals.cs`, `Program.cs`
- [ ] Exit `3` when the run ended `AllDevicesLost`
- [ ] Tests — `tests/DualPlayer.Console.Tests/EventPrinterTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Window closed on Windows | summary printed and logged inside the OS shutdown window (a few seconds); no slow work after `Stop` |
| Second Ctrl+C while stopping | ignored until the summary is out |
| Last device lost | `device-lost … last output, run ends`, summary, exit 3 |
| Crash / killed process | no summary; records so far already in the log |

## Definition of Done

- [ ] `EventPrinterTests` pass: record, excursion, device-lost and summary lines match `contracts/cli.md` §2
- [ ] a test sends the interrupt path and asserts the summary is printed with `ended interrupt` and exit `0`
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
