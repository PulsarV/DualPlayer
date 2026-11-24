---
id: T155
title: "Pin the spec \u00a76 lip-sync, late-frame and recovery bounds just inside and just outside their edges"
layer: "test"
deps: []
blocks: ["T23", "T161"]
acs: ["AC-04", "AC-05", "AC-07", "AC-17"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionBoundEdgeTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23i (AA2)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T155 — Pin the spec §6 lip-sync, late-frame and recovery bounds just inside and just outside their edges

## Why

- **AA2.** `LipSyncedMs = 30`, `OutputLipSyncedMs = 15`, `LateMs = 25` and `RecoveryBudget = 1.3 s` (`PlaybackSession.cs:79, 82, 88, 93`) each pass Core 626/626: the tests use an output 30 → 4 ms off, frames 30 / 15 ms late, picture errors ≥ 150 ms and a 1500 ms queue wait. spec.md:235 (picture within 20 ms, every output within 10 ms, ≤ 1 s) and spec.md:237 (late = more than 20 ms after due) are what T23 L1/L2 judge.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04, AC-05, AC-07, AC-17.

## Definition of Done

- [x] Output bound: an output 11 ms off gives no `LipSyncedAgain`; 9 ms gives one. *(Output 11/-11 ms gives no LipSyncedAgain, 9 ms gives one (new test, passes))
- [x] Picture bound: a picture 21 ms off gives no `LipSyncedAgain`; 19 ms gives one. *(Picture 21/-21 ms gives none, 19 ms gives one (new test, passes))
- [x] Late frame: a frame 21 ms after due counts as late in `FrameCounters`; 19 ms does not. *(Frame 21 ms late counts as late, 19 ms does not (new test, passes))
- [x] Recovery budget: a seek whose picture first qualifies at 1.1 s gives `NotLipSynced`; one that qualifies at 0.9 s gives `LipSyncedAgain`. *(Seek qualifying at 1.1 s gives NotLipSynced, 0.9 s gives LipSyncedAgain (new tests, pass))
- [x] Mutants on a scratch copy, each failing at least one new test: `LipSyncedMs = 30`; `OutputLipSyncedMs = 15`; `LateMs = 25`; `RecoveryBudget = 1.3 s`. *(4 mutants (applied to the real constants 20/10/20/1 s) each fail a new test; all reverted)
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(build 0 warnings, test all green, format clean)
