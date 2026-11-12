---
id: T29
title: "Console: one output writer off the control loop that logs every line, keeps the summary on end of input and Ctrl+C, and numbers outputs from 1"
layer: "ports"
deps: ["T26", "T27"]
blocks: []
acs: ["AC-06", "AC-08", "AC-09", "AC-10", "AC-13"]
files_hint: ["src/DualPlayer.Console/Rendering/EventPrinter.cs", "src/DualPlayer.Console/Rendering/", "src/DualPlayer.Console/Commands/RunCommandLoop.cs", "src/DualPlayer.Console/Commands/PlayCommand.cs", "src/DualPlayer.Console/ShutdownSignals.cs", "src/DualPlayer.Console/Program.cs", "tests/DualPlayer.Console.Tests/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F1, F8, F11, F19)"
status: "todo"
step: "—"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T29 — Console: one output writer off the control loop that logs every line, keeps the summary on end of input and Ctrl+C, and numbers outputs from 1

## Why (review findings)

### F1 — stage-1, high

> End of stdin (and likely Ctrl+C on Windows) ends the run with no console summary: `RunCommandLoop` returns 0 on null `ReadLine`, `PlayCommand` disposes the `EventPrinter`, and the summary is published later by `AudioEngine.Dispose` to no subscriber.
>
> **Where:** `src/DualPlayer.Console/Commands/RunCommandLoop.cs:50`, `src/DualPlayer.Console/Commands/PlayCommand.cs:19-20,45-46`, `src/DualPlayer.Console/Program.cs:41`, `src/DualPlayer.Core/Playback/AudioEngine.cs:95-101`
>
> **Suggested:** On end of input call `engine.Stop(Interrupt)` while the printer is subscribed and wait on `RunEnded`; add a test with stdin returning null mid-run.
>
> — `_review/review-2026-11-12.md, F1, verbatim`

### F8 — stage-2, medium

> `device-lost` prints the 0-based `OutputIndex` while start lines, summary and cli.md are 1-based; tests build 1-based events the engine never produces.
>
> **Where:** `src/DualPlayer.Console/Rendering/EventPrinter.cs:48`, `src/DualPlayer.Core/Playback/AudioEngine.cs:132,664`
>
> **Suggested:** Print `OutputIndex + 1` (or make the event 1-based); add an engine-level printer test.
>
> — `_review/review-2026-11-12.md, F8, verbatim`

### F11 — stage-1, medium

> Only `EventPrinter` mirrors console lines to the log; start lines, paused/resumed/seek confirmed, warnings and unknown-command go to stdout only — T22 cannot locate recovery windows or the output→track→device map in the log.
>
> **Where:** `src/DualPlayer.Console/Commands/PlayCommand.cs:32-36`, `src/DualPlayer.Console/Commands/RunCommandLoop.cs:29,32,45,86,92,96,100`
>
> **Suggested:** Route every console line through one writer that also logs it.
>
> — `_review/review-2026-11-12.md, F11, verbatim`

### F19 — stage-2, medium

> `EventPrinter.OnNext` writes and flushes stdout synchronously on the engine-control loop; a stalled stdout (pipe, QuickEdit selection) freezes correction, device-lost handling and stop.
>
> **Where:** `src/DualPlayer.Console/Rendering/EventPrinter.cs:31-61,80-89`
>
> **Suggested:** Queue lines to a dedicated writer thread; flush before exit.
>
> — `_review/review-2026-11-12.md, F19, verbatim`

## Acceptance criteria

> ### AC-06 (US-05) — domain invariant
> 
> **Given** two audio outputs are playing
> **When** the second output's device disappears (switched off, unplugged, out of range)
> **Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically. With three or more outputs, each further loss behaves the same way
>
> — `spec.md §5, AC-06, verbatim`

> ### AC-08 (US-04) — happy path
> 
> **Given** two audio outputs are playing
> **When** the Developer pauses and later resumes playback
> **Then** on pause (and on stop) all outputs fall silent within 10 ms of each other, judged by each device's reported playback position, and the master clock stops; on resume all outputs continue from the same master-clock position and are back within the drift tolerance within the recovery time (§6)
>
> — `spec.md §5, AC-08, verbatim`

> ### AC-09 (US-04) — happy path
> 
> **Given** two audio outputs are playing
> **When** the Developer seeks 10 or 60 seconds forward or backward
> **Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console; the console prints that confirmation only after every output has discarded the audio it had queued from the old position
>
> — `spec.md §5, AC-09, verbatim`

> ### AC-10 (US-04) — error
> 
> **Given** two audio outputs are playing
> **When** the Developer seeks to a point before the start or past the end of the file
> **Then** a seek before the start plays from the beginning; a seek past the end moves the position to the end of the file at once, all outputs fall silent and the run ends with the run summary (AC-13); in both cases the console tells the Developer the target was outside the file
>
> — `spec.md §5, AC-10, verbatim`

> ### AC-13 (US-08) — happy path
> 
> **Given** a run has been playing
> **When** playback stops because the file ended, the Developer gave the stop command, pressed Ctrl+C, or closed the console window (a crash or a killed process need not produce a summary; the AC-11 records up to that point stay in the log)
> **Then** the console prints and the log records, for every audio output: its device, played duration, largest steady-state drift, estimated device clock mismatch in parts per million, number of corrections, number of underruns and any device-lost events. The clock mismatch is the device clock's rate relative to the master clock; a correction (CONTEXT) is counted only when the output's position is re-aligned in one step, so a steady run is expected to count 0, while the continuous rate adjustment is what AC-11 reports as current correction, in parts per million
>
> — `spec.md §5, AC-13, verbatim`

## Hard rules (unchanged)

- Real-time code (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws — `sad.md §8`.
- No `DateTime.Now` / `Stopwatch` in Core; time from `TimeProvider` or the master clock. Expected failures return `Result` / domain events, never thrown across module boundaries — repo `CLAUDE.md`.
- Never weaken an existing test to make it pass; update a test only where it encoded the bug named above.

## Definition of Done

- [ ] end of standard input during a run stops the run and the run summary is printed to the console before the process exits (test with stdin returning null mid-run); the Ctrl+C path and end of input cannot race to drop the summary; engine events are written by a dedicated writer (a blocked writer does not block the publishing thread — test); every console line during a run (start lines, paused/resumed/seek confirmed, warnings, unknown command) is also written to the log; `device-lost` prints the same 1-based output number as the start lines
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
