---
id: T83
title: "Pin the budget-expiry line, the listing deadline and the run summary's own file with tests"
layer: "test"
deps: ["T82"]
blocks: []
acs: ["AC-08", "AC-09b", "AC-06", "AC-07"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourBudgetTests.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/Startup/TerminalPrinterTests.cs", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/tasks/T75-keep-page-down-and-page-up-honest-when-the-time-budget-runs-out.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22 (P5, P6)"
---

<!-- Follow-up task from review-2026-11-22. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22.md) · repo `CLAUDE.md`. Do not guess. -->

# T83 — Pin the budget-expiry line, the listing deadline and the run summary's own file with tests

## Why

P5: no test mentions `NeighbourSearchTimedOut` (its `Next` / `Checked`) or the terminal line `next-file-timeout  checked N` / `previous-file-timeout  checked N` (`TerminalPrinter.cs:116-118`, `PlaybackSession.cs:527`), though T75 DoD :36 is ticked for it; test-plan.md:139 lists only the window note. Residual R1: no Core test asserts that the engine's `RunSummary.File` (`AudioEngine.cs:1177`) is the stopped run's own file; printer tests only echo what they are fed.

P6: the slow-listing test (`PlaybackSessionNeighbourBudgetTests.cs:68-96`) lists for 2.5 s, inside the 3 s budget, so it covers only the limit re-derived after the listing (`PlaybackSession.cs:651-659`). Reverting the `Task.Run` deadline around `ListTracks` (`:624-634`) to a synchronous call leaves every test green.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b, AC-06, AC-07.

## Definition of Done

- [x] The budget test asserts exactly one `NeighbourSearchTimedOut(Next: true, Checked: n)` with the right count; a Page Up case asserts `Next: false`.
- [x] `TerminalPrinterTests` covers `next-file-timeout  checked N` and `previous-file-timeout  checked N` (exact spacing per cli.md §2.2).
- [x] A Core test asserts `RunSummary.File` is the stopped run's file across a file change (fails if the engine took the file from anywhere else).
- [x] A case where `ListTracks` blocks > 3 s: the press returns within budget + 250 ms with `NeighbourSearchTimedOut(checked 0)` and no `FileSkipped`; it fails if the listing deadline is reverted.
- [x] test-plan.md lists the new tests.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
