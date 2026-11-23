---
id: T136
title: "Decide the exit code, write the error line and store it under one lock, and pin Esc and the OS close during a running start"
layer: "wiring"
deps: []
blocks: ["T138", "T23"]
acs: ["AC-02", "AC-06"]
files_hint: ["src/DualPlayer.App/App.axaml.cs", "src/DualPlayer.App/Startup/StartErrors.cs", "tests/DualPlayer.App.Tests/Startup/AppEntryTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23d (V1, V2)"
---

<!-- Follow-up task from review-2026-11-23d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23d.md) · repo `CLAUDE.md`. Do not guess. -->

# T136 — Decide the exit code, write the error line and store it under one lock, and pin Esc and the OS close during a running start

## Why

- **V1.** Two branches of T134's start-path wiring have no test: Esc during a start that succeeds (`App.axaml.cs:200`, `|| exitRequested`) and the OS closing the window during a start (`:186-191`, `windowClosing`). Mutants `close = code is not null` (the window never closes; `PlaybackControls.ExitNow` runs once, so another Esc does nothing) and `windowClosing = closing && false` (an error line with exit 0) each pass all 162 App tests. The existing Esc and Closing tests (`AppEntryTests.cs:91-136, 220-264`) never raise `VideoHandleCreated`, so no start runs; T134 DoD box 4 claimed cover that did not exist.
- **V2.** The worker reads `closing` under `gate`, prints the error line in `StartErrors.AfterSessionStart` outside the lock, then writes `exitCode` under a second lock (`App.axaml.cs:185-201`). A `Closed` (`:138-152`) in that gap reads `exitCode == 0` and calls `Shutdown(0)` with the line already printed — against cli.md §4 (:120). Reproduced 10 of 10 in review (scratch test: stderr write posts `window.Close()` and waits for `Closed`); moving `AfterSessionStart` into the lock fixed it with all 8 AppEntryTests green. The stderr write does not need the UI thread, so it cannot deadlock.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02, AC-06.

## Definition of Done

- [x] The start worker reads `closing`, calls `StartErrors.AfterSessionStart` (the error line) and writes `exitCode` / `startRunning` / `close` inside one `lock (gate)`, so `Closed` sees either the OS close first (exit 0, no error line) or the written code (2 / 4 with the line).
- [x] Test in AppEntryTests (the V2 repro): missing device, the stderr write posts `window.Close()` and waits for `Closed` → `Shutdown(0)` is never received with an `error` line on stderr; it fails on the current two-lock code (10 of 10 in review) and passes after the fix. *(Test `A_window_closed_while_the_error_line_is_written_still_exits_2_with_the_line_AC_02_AC_06`: failed 10 of 10 before the fix with `Shutdown(0)` received. After the fix its writer's 2 s wait for `Closed` times out by design, because `Closed` waits on the lock.)*
- [x] Test in AppEntryTests: valid devices, `controls.Exit()` while the start is held by `HoldingObserver`, then release → `Received(1).Shutdown(0)`, `run-summary … reason exit` printed, empty stderr; fails with `close = code is not null`. *(That mutant does not compile, CS0219, so `close = code is not null || (exitRequested & false)` was used. The test needed an `afterStart` hold until `Exited` has been handled; with the mutant it fails after about 10 s.)*
- [x] Test in AppEntryTests: missing device, `window.Close()` while the start is held, then release → `Received(1).Shutdown(0)`, no `error` line on stderr, the "window closed during the start" log entry; fails with `windowClosing = closing && false`.
- [x] AppEntryTests pass 30 runs with 3 processes in parallel. *(30/30 × 3.)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
