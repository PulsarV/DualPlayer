---
id: T27
title: "Keep the excursion peak signed, measure played and excursion durations on running time, and ignore positions before the first render"
layer: "domain"
deps: ["T23"]
blocks: []
acs: ["AC-07", "AC-13"]
files_hint: ["src/DualPlayer.Core/Diagnostics/ExcursionTracker.cs", "src/DualPlayer.Core/Diagnostics/RunSummaryBuilder.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "src/DualPlayer.Core/Sync/ClockMismatchEstimator.cs", "src/DualPlayer.Console/Rendering/EventPrinter.cs", "tests/DualPlayer.Core.Tests/Diagnostics/", "tests/DualPlayer.Core.Tests/Outputs/OutputRenderSourceTests.cs", "tests/DualPlayer.Console.Tests/EventPrinterTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F9, F10, F22)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T27 — Keep the excursion peak signed, measure played and excursion durations on running time, and ignore positions before the first render

## Why (review findings)

### F9 — stage-1, low

> Excursion peak keeps `Math.Abs`, so a behind output prints `peak_ms +14.2` (cli.md: + = ahead).
>
> **Where:** `src/DualPlayer.Core/Diagnostics/ExcursionTracker.cs:19-20`, `src/DualPlayer.Console/Rendering/EventPrinter.cs:43`
>
> **Suggested:** Keep the signed drift of largest magnitude.
>
> — `_review/review-2026-11-12.md, F9, verbatim`

### F10 — stage-1, low

> Played duration and excursion duration use master-clock positions, so seeks inflate/negate them; an open excursion survives a seek.
>
> **Where:** `src/DualPlayer.Core/Diagnostics/RunSummaryBuilder.cs:57`, `src/DualPlayer.Core/Diagnostics/ExcursionTracker.cs:11-14,36`
>
> **Suggested:** Sum clock-running intervals on the `TimeProvider` base; close open excursions on seek.
>
> — `_review/review-2026-11-12.md, F10, verbatim`

### F22 — stage-2, low

> `TryGetLatestPosition` returns (0,0) before the first render, so a sample then anchors the clock-mismatch estimate at timestamp 0 (≈ −1,000,000 ppm).
>
> **Where:** `src/DualPlayer.Core/Outputs/OutputRenderSource.cs:51-57`, `src/DualPlayer.Core/Playback/AudioEngine.cs:584-587`, `src/DualPlayer.Core/Sync/ClockMismatchEstimator.cs:32-39`
>
> **Suggested:** Return false until the first render.
>
> — `_review/review-2026-11-12.md, F22, verbatim`

## Acceptance criteria

> ### AC-07 (US-03) — happy path (long run)
> 
> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim`

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

- [ ] a behind output's excursion prints a negative `peak_ms`; played duration after `seek +60` equals the real running time; an excursion open at a seek is closed with its pre-seek end and a non-negative duration; `TryGetLatestPosition` returns false before the first render and the clock-mismatch estimate is never anchored at timestamp 0
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
