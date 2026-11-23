---
id: T128
title: "Let fake time move during the give-up grace, pin the 50 ms grace from both sides, steady the X10 test and pin the fake renderer's anchor rule"
layer: "test"
deps: []
blocks: ["T129", "T131", "T23"]
acs: ["AC-08", "AC-17"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Fakes/CountingTimeProvider.cs", "tests/DualPlayer.Core.Tests/Fakes/FakeVideoRenderer.cs", "tests/DualPlayer.Core.Tests/Fakes/FakeVideoRendererTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-23b (Z3, Z4, Z6)"
---

<!-- Follow-up task from review-2026-11-23b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23b.md) · repo `CLAUDE.md`. Do not guess. -->

# T128 — Let fake time move during the give-up grace, pin the 50 ms grace from both sides, steady the X10 test and pin the fake renderer's anchor rule

## Why

- **Z3.** During `AwaitGrace` no fake holds a waiting scope (T124 ends it inside `Cancel()`), so the rig stops fake time and `_time.GetTimestamp() >= graceEnd` never becomes true: the grace lasts as long as the fake's 200 ms real-time wind-down, capped by `MaxBusyPolls`. Both grace tests pass with any grace above zero, so spec.md:249's ~50 ms is not pinned, and a wind-down longer than 50 ms (an FFmpeg step the interrupt cannot break) brings back the X10 symptom unseen. A fake that stays busy forever (`StayBusyAfterTimeout`, `PlaybackSessionNeighbourRecoveryTests.cs:305`) costs 500 × `Sleep(1)` per give-up: 755 ms on macOS, up to ~8 s on windows-latest. Cites: `PlaybackSession.cs:63, 800, 805-822`; `PlaybackSessionNeighbourMemoryTests.cs:252, 270, 376, 388`; `Fakes/CountingTimeProvider.cs:140`; `Fakes/EngineRig.cs:202-217`.
- **Z4** (rest of Y3). The X10 test (`PlaybackSessionNeighbourMemoryTests.cs:252-267`, fake `:382-393`) still fails 2 of 60 parallel runs: "FileChanged should have single item but had 2". `SlowPastItsLimit` spins toward its own 1.5 s end; the rig stops fake time only at a fake's own end, not at the session's give-up (limit + 50 ms ≈ 1.04 s, `PlaybackSession.cs:935`). When the session thread waits for the CPU, fake time runs to 1.5 s, the fake answers success, the first press plays E3 and the second plays E4.
- **Z6.** T127's fix in `Fakes/FakeVideoRenderer.cs:146-153` (`at > _anchorTimestamp`) is pinned only through `PlaybackSessionLateFrameTests.cs:44`, which failed about 1 run in 5 before the fix; a revert would usually still pass.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-17.

## Definition of Done

- [ ] The wind-down fakes (the X10 fake and the adapter-shaped busy fake) spend their wind-down as fake time inside a waiting scope, so the rig moves the clock during the grace and production's 50 ms is what the tests exercise.
- [ ] Test: a wind-down shorter than the 50 ms grace → the queued second press plays E3. Test: a wind-down longer than the grace → E3 is stepped past, neither judged nor remembered (the next press probes it again). Each fails if the grace length is changed past its side.
- [ ] A give-up on a fake that stays busy forever costs a bounded, small real time (no 500 × `Sleep(1)` per give-up on a frozen clock).
- [ ] The X10 fake stops at its time limit + slack (registered with the rig as its end time) and then waits in real time for the cancel, so fake time cannot run past the session's give-up while the session thread waits for the CPU. The neighbour and rig tests (`--filter "FullyQualifiedName~Neighbour|FullyQualifiedName~RigLoops"`) pass 20 runs with 3 processes in parallel.
- [ ] `FakeVideoRendererTests` case: `FrameDuration` 40 ms, re-anchor, read `Presented` twice in the same instant (and once with `SnapshotAge > 0`) → one grid frame start. Fails if `at > _anchorTimestamp` is reverted.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
