---
id: T115
title: "Stop the late-pool probe test from hanging the Core suite, and dispose the engine before the session in the rig"
layer: "test"
deps: []
blocks: []
acs: ["AC-09b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/SessionRig.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Fakes/CountingTimeProvider.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22f (X9)"
---

<!-- Follow-up task from review-2026-11-22f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22f.md) · repo `CLAUDE.md`. Do not guess. -->

# T115 — Stop the late-pool probe test from hanging the Core suite, and dispose the engine before the session in the rig

## Why

- **X9** (observed: 2 of 11 parallel runs of the Core neighbour tests hung; blame-hang named `A_probe_wrapper_the_thread_pool_starts_late_is_not_given_up_on_before_the_adapter_answers_AC_09b`). The test (`PlaybackSessionNeighbourRecoveryTests.cs:251-300`) fills the process's whole thread pool and moves the rig's fake clock by 2.1 s from a free-running thread (`:289-296`), outside the `Call` gate. The jump lands during E2's engine start or after the test body; the session thread sits in `Monitor.Wait`, the engine control loop polls fake time, and `SessionRig.Dispose` (`SessionRig.cs:190-195`, session before engine) waits in `Thread.Join` forever. The only assertion is `FileChanged`, published before `StartCore`. Filling the pool also slowed overlapping neighbour tests from 0.1–0.3 s to 1.3–2.5 s. In CI the job would hang until the Actions timeout.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b.

## Definition of Done

- [x] The late-pool test no longer fills the thread pool and no longer moves fake time from a free-running thread: fake time moves only under `Call` (e.g. from the fake's own hook), and any thread the test starts is joined before it asserts.
- [x] The test proves the probe wrapper runs off the pool by asserting `!Thread.CurrentThread.IsThreadPoolThread` inside the fake probe, and asserts that the new film actually started (`AudioStarted` or state Playing), not only `FileChanged`. It fails if the wrapper goes back to `Task.Run`.
- [x] `SessionRig.Dispose` disposes the engine before the session (or otherwise bounds the join), so a stalled command fails the test instead of hanging the run; a rig self-test pins that dispose returns within a bounded real time when a command is stuck.
- [x] The neighbour, rig and reload test classes (`dotnet test tests/DualPlayer.Core.Tests --filter "FullyQualifiedName~Neighbour|FullyQualifiedName~RigLoops|FullyQualifiedName~PictureFailureReload|FullyQualifiedName~AbandonedWorker"`) pass 10 runs in a row with parallel classes, none hanging (use `--blame-hang-timeout 120s`).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
