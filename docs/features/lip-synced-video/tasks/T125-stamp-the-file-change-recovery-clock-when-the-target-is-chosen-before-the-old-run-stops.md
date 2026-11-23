---
id: T125
title: "Stamp the file-change recovery clock when the target is chosen, before the old run stops"
layer: "app"
deps: ["T124"]
blocks: []
acs: ["AC-07", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23 (Y7 (code))"
---

<!-- Follow-up task from review-2026-11-23. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23.md) · repo `CLAUDE.md`. Do not guess. -->

# T125 — Stamp the file-change recovery clock when the target is chosen, before the old run stops

## Why

- **Y7** (code part). `chosenAt` is taken after `_engine.Stop(RunEndReason.FileChange)` (`PlaybackSession.cs:646` vs `:668`), and the stop can wait up to `StopTimeout` = 2 s (`AudioEngine.cs:1196-1208`), so the old run's stop falls outside the 3 s recovery that spec §6 counts "from the moment the target is chosen". The 2900 ms test cannot see it because the rig's stop takes 0 fake time. Owner decision: stamp `chosenAt` right after the scan picks the target, before `CancelFreeze` / `_engine.Stop`.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-09.

## Definition of Done

- [x] `chosenAt` is read right after the scan picks the target, before `CancelFreeze` and `_engine.Stop`; `_resync` uses it; `_readingsAfter` stays at or after the stop.
- [x] Test: the rig's stop takes non-zero fake time (e.g. 1 s), the new film's lip-sync is reached 2.5 s after the stop ends → `NotLipSynced(file-change)` is published (3.5 s from the choice); with the stop inside the budget → `LipSyncedAgain` with `AfterCommandMs` counted from the choice. Fails if reverted.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
