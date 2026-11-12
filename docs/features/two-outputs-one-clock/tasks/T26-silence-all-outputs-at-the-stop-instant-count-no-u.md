---
id: T26
title: "Silence all outputs at the stop instant, count no underrun at file end, and warn before the summary on a seek past the end"
layer: "app"
deps: ["T25", "T23", "T24"]
blocks: []
acs: ["AC-08", "AC-10", "AC-13"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "src/DualPlayer.Core/Playback/EngineEvents.cs", "src/DualPlayer.Console/Commands/RunCommandLoop.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineTransportTests.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs", "tests/DualPlayer.Console.Tests/RunCommandLoopTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F2, F6, F13)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T26 — Silence all outputs at the stop instant, count no underrun at file end, and warn before the summary on a seek past the end

## Why (review findings)

### F2 — stage-1, medium

> `stop` / file end closes devices at once (`EndRun` → `Teardown` → `Close`) instead of silencing every output at the stop instant; stop while paused moves `SilenceFrom` past the pause point and plays ~40 ms.
>
> **Where:** `src/DualPlayer.Core/Playback/AudioEngine.cs:350-365,676-700`
>
> **Suggested:** Gate close on every device passing the stop instant (as `WaitForSeek`); never move `SilenceFrom` later when paused; add a stop variant of the pause-alignment test.
>
> — `_review/review-2026-11-12.md, F2, verbatim`

### F6 — stage-2 (correctness), medium

> The render that reads the last frames returns a partial buffer before the run ends, so every run to end of file counts ≥ 1 underrun per output.
>
> **Where:** `src/DualPlayer.Core/Outputs/OutputRenderSource.cs:168-173`, `src/DualPlayer.Core/Playback/AudioEngine.cs:465-468`
>
> **Suggested:** Stop counting underruns once the pump ended the epoch and the ring is drained; assert `Underruns == 0` on `FileEnd`.
>
> — `_review/review-2026-11-12.md, F6, verbatim`

### F13 — stage-1, low

> Seek past the end prints the run summary before `warning: target was outside the file` (the summary is published inside `engine.Seek`).
>
> **Where:** `src/DualPlayer.Console/Commands/RunCommandLoop.cs:84-87`, `src/DualPlayer.Core/Playback/AudioEngine.cs:303`
>
> **Suggested:** Publish/print the out-of-range notice before `EndRun`.
>
> — `_review/review-2026-11-12.md, F13, verbatim`

## Acceptance criteria

> ### AC-08 (US-04) — happy path
> 
> **Given** two audio outputs are playing
> **When** the Developer pauses and later resumes playback
> **Then** on pause (and on stop) all outputs fall silent within 10 ms of each other, judged by each device's reported playback position, and the master clock stops; on resume all outputs continue from the same master-clock position and are back within the drift tolerance within the recovery time (§6)
>
> — `spec.md §5, AC-08, verbatim`

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

- [ ] on `stop` every output falls silent at the chosen stop instant within 10 ms of each other by device position before devices close (test mirrors the pause-alignment test); stop while paused plays no audio after the pause instant; a run to end of file reports `Underruns == 0` per output; on a seek past the end the console prints the out-of-file warning before the run summary
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
