---
id: T133
title: "Pin that a cut first-file wrapper that never answers is remembered only under the whole-budget rule"
layer: "test"
deps: ["T132"]
blocks: ["T134", "T135", "T23"]
acs: ["AC-08"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourWorkerTimingTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23c (W3)"
---

<!-- Follow-up task from review-2026-11-23c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23c.md) · repo `CLAUDE.md`. Do not guess. -->

# T133 — Pin that a cut first-file wrapper that never answers is remembered only under the whole-budget rule

## Why

- **W3.** T129's rule at `PlaybackSession.cs:976-978` (`&& (!cut || wholeBudget)`) is not pinned: changed to `&& !cut`, all 59 `*Neighbour*` tests still pass. `PlaybackSessionNeighbourWorkerTimingTests.cs:339` (`A_first_file_whose_listing_cut_the_probe_limit_…_AC_08`) checks only "no FileSkipped" and "timed out".

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08.

## Definition of Done

- [x] The test adds a healthy Episode-03 and a second press after `WaitForNeighbourWorkers`; it asserts E2 is checked last and printed `skipped … unreadable` (remembered) and E3 plays.
- [x] The test fails with `&& (!cut || wholeBudget)` changed to `&& !cut`, and passes as is.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
