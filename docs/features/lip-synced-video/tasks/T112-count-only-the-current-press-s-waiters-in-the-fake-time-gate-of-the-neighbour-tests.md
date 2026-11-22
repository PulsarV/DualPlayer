---
id: T112
title: "Count only the current press's waiters in the fake-time gate of the neighbour tests"
layer: "test"
deps: ["T109"]
blocks: []
acs: ["AC-08", "AC-09b"]
files_hint: ["tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Fakes/CountingTimeProvider.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRememberedFilesTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourWorkerTimingTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22e (W8)"
---

<!-- Follow-up task from review-2026-11-22e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22e.md) · repo `CLAUDE.md`. Do not guess. -->

# T112 — Count only the current press's waiters in the fake-time gate of the neighbour tests

## Why

- **W8**. `Waiters` in the fake-time gate is global (`EngineRig.cs:198-203`, `CountingTimeProvider.cs:56, 66`). A Block/Never fake from an earlier press that is still inside its `Waiting()` scope (`PlaybackSessionNeighbourRecoveryTests.cs:276, 367`, `PlaybackSessionNeighbourRememberedFilesTests.cs:152`) keeps `Waiters=1`, so on the next press the gate advances 10 ms per loop from the moment the command thread starts, and fake time is spent at a rate set by real scheduling. In RecoveryTests test 2, press 2, whether E2 gets its full 2 s probe limit or is cut is non-deterministic. This is the V9 failure mode; the tests pass only because their assertions are coarse.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [ ] The gate moves fake time only for waiters that entered during the current `Call` (a generation captured at `Call` start), or the Block/Never fakes leave their `Waiting()` scope once the session cancels their token; an abandoned waiter from press N never moves time in press N+1. No real-time cap.
- [ ] A rig self-test pins that: an abandoned waiter from press N, then press N+1 with no fake waiting → fake time does not move during the folder work of press N+1. Fails if the generation check is removed.
- [ ] The neighbour test classes pass 10 runs in a row (`--no-build`), and each still takes well under 1 s.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
