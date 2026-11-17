---
id: T76
title: "Time the pre-roll stop from the output lines and bound its log check to the run"
layer: "app"
deps: []
blocks: []
acs: ["AC-08", "AC-13"]
files_hint: ["docs/features/two-outputs-one-clock/evidence/target-pc-run-guide.md", "docs/features/two-outputs-one-clock/tasks/T22-record-target-pc-evidence-run.md", "docs/features/two-outputs-one-clock/test-plan.md", "docs/features/two-outputs-one-clock/sad.md"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r5: F1, F2"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-17-r5. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r5.md) · repo `CLAUDE.md`. Do not guess. -->

# T76 — Time the pre-roll stop from the output lines and bound its log check to the run

## Why

- F1: the pre-roll stop check (`evidence/target-pc-run-guide.md:127` at HEAD, `tasks/T22-record-target-pc-evidence-run.md:108`, `test-plan.md:103`) times the stop "~1.5 s after `play`". The release-to-clock-start window is counted from the device start (`AudioEngine.cs:447-469`), and before that come process start-up, FFmpeg load, track listing, device open and `WaitForStartLevel` (`AudioEngine.cs:120-189`). The console's `output …` lines print when `StartRun` returns, i.e. when the silent pre-roll starts (`PlayCommand.cs:32-44`, `AudioEngine.cs:202-203`), so they are the right reference point.
- F2: the count condition "`Film released on …` before `Run ended (Stop)`" (guide, T22, test-plan, `sad.md:539`) is ambiguous. All runs of a day share one log file (`src/DualPlayer.Console/Program.cs:22-23`, `RollingInterval.Day`), so a previous run's `Film released` line can sit above this run's `Run ended (Stop)`. `Run started with …` is logged when the devices start (`AudioEngine.cs:202`), before the release (`:487`). A stop before the clock start logs `Run ended (Stop) at master clock 0.000 s` (`AudioEngine.cs:1004`, `MasterClock.cs:81-82`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-13; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md); [test-plan.md](../test-plan.md) NFR validation; `sad.md` §11.

## Definition of Done

- [x] The pre-roll stop check (guide §5, T22, test-plan.md) times the stop ~1.5 s after the console's `output …` lines, not after `play`.
- [x] The count condition in guide §5, T22, test-plan.md and sad.md §11 reads the lines of this run only: `Film released on …` between this run's `Run started with …` and its `Run ended (Stop)`, and that `Run ended (Stop)` shows `at master clock 0.000 s`; the retry rule says stop later when the release is missing and earlier when `started …` printed or the clock is above 0.
- [x] The author's uncommitted run-guide change (§3.1/§3.2) stays out of this task's commit.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
