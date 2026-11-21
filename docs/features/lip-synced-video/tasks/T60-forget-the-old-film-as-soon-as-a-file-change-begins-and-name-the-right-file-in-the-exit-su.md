---
id: T60
title: "Forget the old film as soon as a file change begins and name the right file in the exit summary"
layer: "app"
deps: []
blocks: []
acs: ["AC-05", "AC-06"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.App/Startup/TerminalPrinter.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/Startup/"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21c (S1, S4)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T60 — Forget the old film as soon as a file change begins and name the right file in the exit summary

## Why

S1 (rest of Q1): `_filmLoaded` is cleared only in `StartRun`, not when a file change begins (`PlaybackSession.cs:99-100, 357`). Page Down, then a device leaves the list before `Load` → `outputs.device_not_found` (`:216-219`); `ChangeFile` sets `_endPosition = 0` and Ended (`:544-556`) while the old film stays returnable. ← then restarts the old film from 00:00 as a continuation, and at Esc the terminal prints `run-summary file <new file>` with the old film's played time (`TerminalPrinter.cs:80-83, 116-120`, `_file` set by `FileChanged`).

S4: the T48 session test (`PlaybackSessionReturnGuardTests.cs:44-58`) loops over `summaries.Skip(1)`, which is empty, so moving `_filmLoaded = true` back to right after `Load` keeps it green.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-06.

## Definition of Done

- [ ] `ChangeFile` clears `_filmLoaded` right after `_engine.Stop(FileChange)`, so after any failed file change (before or after `Load`) ← is a no-op that leaves the session Ended.
- [ ] The exit summary names the file its run played: either `RunSummary` carries its file and the printer uses it, or the printer's file goes back to the playing file when a change fails — no `run-summary file <new file>` with the old film's figures.
- [ ] The Q2 stale-summary test in `PlaybackSessionEndedRecoveryTests.cs:68-84`, which relies on returning into the old film after a failed change, is rewritten to keep its intent (a stale `RunSummary` does not move `_endPosition`) without that return.
- [ ] The T48 session test asserts after ← that `EngineRunFiles.Count` is unchanged, the state is `Ended` and `summaries.Count == 1` (it fails if `_filmLoaded = true` moves back to after `Load`).
- [ ] Tests: Page Down with a device unlisted before `Load`, then ← → no engine run, Ended; then Esc → summary line names the old file (printer test).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
