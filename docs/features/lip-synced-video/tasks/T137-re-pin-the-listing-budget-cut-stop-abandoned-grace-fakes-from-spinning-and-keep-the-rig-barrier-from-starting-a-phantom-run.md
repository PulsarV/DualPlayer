---
id: T137
title: "Re-pin the listing budget cut, stop abandoned grace fakes from spinning, and keep the rig barrier from starting a phantom run"
layer: "test"
deps: []
blocks: ["T138", "T23"]
acs: ["AC-08", "AC-09b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourBudgetTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Fakes/RigLoopsTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-23d (V3, V5, V6)"
---

<!-- Follow-up task from review-2026-11-23d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23d.md) · repo `CLAUDE.md`. Do not guess. -->

# T137 — Re-pin the listing budget cut, stop abandoned grace fakes from spinning, and keep the rig barrier from starting a phantom run

## Why

- **V3.** After T132 the listing fake in `A_track_listing_that_blocks_past_the_budget…` (`PlaybackSessionNeighbourBudgetTests.cs:142-153`) caps fake time at budget + 50 ms itself and ignores the timeout of `release.Wait(30 s)`, so `elapsed ≤ 3.25 s` cannot fail. With the budget check at `PlaybackSession.cs:879` disabled the test passes (30 s slower); at f11905c it failed. T132 did the right thing for the late-wrapper test at `PlaybackSessionNeighbourWorkerTimingTests.cs:188-199`.
- **V5.** The grace fakes (`PlaybackSessionNeighbourMemoryTests.cs:670-672, 679-681`) ignore `WaitForReads` returning false and call `time.SpinUntil(…, CancellationToken.None)`; after `ReleaseWorkers` nothing moves fake time, so the thread spins for the rest of the process. A grace-0 mutant ended in "timed out after 10 s waiting for the session's neighbour workers" instead of the real assertion.
- **V6.** `The_engine_rig_barrier_fails_once_the_run_has_ended…` (`RigLoopsTests.cs:94`) failed 1 of 60 and 1 of 36 runs under parallel load (`Should.Throw(rig.Barrier)` did not throw); 120 of 120 alone. Likely cause, not confirmed: the strict barrier's `StartRun(string.Empty, [])` probe (`EngineRig.cs:333-335`) can start a zero-output phantom run after the run ended.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [x] In `A_track_listing_that_blocks_past_the_budget…` the listing fake waits on its gate with a short bound (about 5 s) and records `gateTimedOut`; the test asserts it is 0 after the press. With the budget check at `PlaybackSession.cs:879` disabled (scratch copy) the test fails; as is it passes. *(Mutant failure: `gateTimedOut should be 0 but was 1`.)*
- [x] The grace fakes in PlaybackSessionNeighbourMemoryTests return as soon as `WaitForReads` returns false, and their `SpinUntil` calls take a token tied to `_release`; with the grace set to 0 (scratch copy) the test fails on its own assertion, not on the 10 s neighbour-worker timeout. The comments at :303 and :321 stay true. *(Grace 0: both "inside the grace" tests fail in 133–221 ms on `FileChanged` being Episode-04, not Episode-03.)*
- [x] The strict rig barrier checks the run is still active without calling `StartRun(string.Empty, [])` (as the `EndsAllowed` path does), or the final assertion of `The_engine_rig_barrier_fails_once_the_run_has_ended…` no longer depends on a phantom run. *(The strict barrier now asserts `Engine.PositionErrors()` is not empty, which goes through the engine's loop and never starts a run. Cause inferred from code, not reproduced: on the fake decoder, the old probe start succeeds once the run has ended and leaves a phantom run behind.)*
- [x] The neighbour and rig tests (`--filter "FullyQualifiedName~Neighbour|FullyQualifiedName~RigLoops"`) pass 20 runs with 3 processes in parallel, each process using its own build output (0 RigLoops failures). *(0 of 60.)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
