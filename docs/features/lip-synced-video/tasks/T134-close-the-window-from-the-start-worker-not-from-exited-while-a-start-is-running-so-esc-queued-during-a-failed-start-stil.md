---
id: T134
title: "Close the window from the start worker, not from Exited, while a start is running, so Esc queued during a failed start still reports its error"
layer: "wiring"
deps: ["T133"]
blocks: ["T135", "T23"]
acs: ["AC-02", "AC-06"]
files_hint: ["src/DualPlayer.App/App.axaml.cs", "tests/DualPlayer.App.Tests/Startup/AppEntryTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23c (W7)"
---

<!-- Follow-up task from review-2026-11-23c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23c.md) · repo `CLAUDE.md`. Do not guess. -->

# T134 — Close the window from the start worker, not from Exited, while a start is running, so Esc queued during a failed start still reports its error

## Why

- **W7.** Esc queued during a start that fails races the window close. The queued `Exit` runs right after the failed `Start` (`PlaybackSession.cs:1979`); `Exited` posts `closing = true; window.Close()` (`App.axaml.cs:111-117`) and `Closed` calls `desktop.Shutdown(exitCode)` (`:126`), while the worker still reads `closing` and writes `exitCode` (`:148-150`). The run can end with exit 0, with or without the error line, against cli.md:120, ux-flows.md:61 and screens.md:73. No test queues Esc before a failing start (`AppEntryTests.cs:140-164` fails the start without Esc; `StartErrorsTests` passes a fixed flag).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02, AC-06.

## Definition of Done

- [x] While the session start is running, `Exited` does not close the window; once the start returns, the worker sets `exitCode` (if the start failed) and then closes the window, or, if the start succeeded and Exit was already raised, closes it with exit 0. A window closed by the OS during the start still exits 0 with no error line.
- [x] `exitCode` and `closing` are read and written with visibility guarantees across the worker and UI threads.
- [x] Test in AppEntryTests: `controls.Exit()` before `RaiseVideoHandleCreated`, with a missing device → the error line is printed and `Shutdown(2)` is called. Fails if reverted (run it repeatedly on the old code to show the race, or make the race deterministic with a hook). *(Without a hook the test false-passed 60/60, so `App.Play` got an internal `afterStart` seam, called right after `session.Start` returns; before the fix the test failed with `Shutdown(0)` received instead of `Shutdown(2)`.)*
- [x] Existing AppEntryTests for Alt+F4 / OS close during the start and Esc after a successful start still pass.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
