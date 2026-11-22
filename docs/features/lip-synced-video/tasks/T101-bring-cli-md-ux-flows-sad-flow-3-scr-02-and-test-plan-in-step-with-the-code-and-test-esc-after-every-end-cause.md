---
id: T101
title: "Bring cli.md, ux-flows, sad flow 3, SCR-02 and test-plan in step with the code, and test Esc after every end cause"
layer: "docs"
deps: ["T96", "T97", "T98", "T99", "T100"]
blocks: []
acs: ["AC-01", "AC-05", "AC-06", "AC-07", "AC-08"]
files_hint: ["docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/ux-flows.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/test-plan.md", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionExitTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22c (U12, U13, U14, U15)"
---

<!-- Follow-up task from review-2026-11-22c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22c.md) · repo `CLAUDE.md`. Do not guess. -->

# T101 — Bring cli.md, ux-flows, sad flow 3, SCR-02 and test-plan in step with the code, and test Esc after every end cause

## Why

Docs drift and one over-claimed test row.

- **U12.** The rule in cli.md:17 ("fields separated by exactly two spaces, the code does not pad columns") is false: `next`/`previous`, `picture`, `started` and the run-summary rows are padded (`TerminalPrinter.cs:111, 181, 245`; `RunSummaryLines.cs:68`), so `previous <file>` has one space.
- **U13.** ux-flows US-03 and sad flow 3 have no branch for AC-08's second outcome (budget runs out: `next-file-timeout`); ux-flows has no edges for the SCR-03 notes "Could not play <file>" and "Could not go back" (`ux-flows.md:84-98, 152`; `sad.md:288-313, 507`; `screens.md:109, 114-115`).
- **U14.** cli.md §2.1 (:74) and SCR-02 `ended` (`screens.md:76`) leave out the failed file change (reason `file-change`), which AC-06 counts as an end after which Esc prints nothing.
- **U15.** test-plan.md:146 says the Esc-after-end test covers five end causes; `PlaybackSessionExitTests.cs:54-71` drives only a seek past the end.

Owner decision: fix now, one docs task last.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-05, AC-06, AC-07, AC-08.

## Definition of Done

- [ ] cli.md:17 lists the fixed-width labels (`next`/`previous`, `picture`, `started`) and the padded run-summary rows; the rest of the rule matches `TerminalPrinter`/`RunSummaryLines`.
- [ ] ux-flows US-03 and sad flow 3 have a budget-ran-out branch (`next-file-timeout`); US-03 has a new-file-fails-to-start → SCR-03 "Could not play" → SCR-02 ended edge; US-02 has a return-fails → SCR-03 "Could not go back" edge; the AC-08 coverage rows in both files are updated.
- [ ] cli.md §2.1 and SCR-02 `ended` include the failed file change (reason `file-change`).
- [ ] The Esc-after-end test is a theory over the five end causes (natural end, seek past end, last output lost, picture failure, failed file change), each asserting exactly one run summary before and after Esc; test-plan.md:146 matches it, and rows exist for every test T96–T100 added.
- [ ] Every DoD box of T96–T100 that holds is ticked; the tracker's T23 row is blocked by T22, T24–T101.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
