---
id: T140
title: "Pin exit while paused end to end, and order the OS close before the start through Exited instead of a sleep"
layer: "test"
deps: []
blocks: ["T141", "T23"]
acs: ["AC-06"]
files_hint: ["tests/DualPlayer.App.Tests/E2E/PlayEndToEndTests.cs", "tests/DualPlayer.App.Tests/Startup/AppEntryTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23e (U2, U3)"
---

<!-- Follow-up task from review-2026-11-23e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23e.md) · repo `CLAUDE.md`. Do not guess. -->

# T140 — Pin exit while paused end to end, and order the OS close before the start through Exited instead of a sleep

## Why

- **U2.** AC-06 (spec.md:141) says exit "while the film plays or is paused" prints the run summary; every exit test (`PlayEndToEndTests.cs:60`, `PlaybackSessionStartTests.cs:151`, `AppEntryTests.cs:92`) exits while playing. The behaviour is correct today (a scratch e2e case passed), but the paused branch is not pinned.
- **U3.** The OS-close start-path test (`AppEntryTests.cs:352-357`) orders `Closed` before the start worker takes `gate` only through `Task.Run(() => { Thread.Sleep(300); release.Set(); })`. A UI-thread stall over 300 ms makes the start fail first (exit 2, correct) and the test fail. `Exited` fires inside the `Closed` handler's `controls.ExitNow()` after `closing = true` is set under the lock (`App.axaml.cs:141-148`, `ViewModels/PlaybackControls.cs:50-58`), so waiting for it in `afterStart` orders the close deterministically.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06.

## Definition of Done

- [x] New `PlayEndToEndTests` case: Space, wait for Paused, Esc through the real `PlaybackControls` → exactly one `run-summary file Film.mkv reason exit` and exit 0; it fails with the paused branch of the exit path broken (scratch copy). *(Mutant: no engine stop while paused → the test fails.)*
- [x] `A_window_closed_by_the_OS_during_a_start_that_fails…` (AppEntryTests.cs:330) releases the hold at once, passes `afterStart` that waits (bounded, 10 s) for `controls.Exited`, and has no `Thread.Sleep`; it passes with a 400 ms stall before `window.Close()` (scratch copy) and still fails with `closing` ignored in `AfterSessionStart`. *(Passes with a 400 ms stall before window.Close(); fails with closing ignored (stderr has the error line).)*
- [x] AppEntryTests and PlayEndToEndTests pass 10 runs with 3 processes in parallel. *(30 of 30.)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(Build 0 warnings; tests Core 621, Media 174, Console 151, App 166, Windows 41, MacOS 12 passed; format clean.)*
