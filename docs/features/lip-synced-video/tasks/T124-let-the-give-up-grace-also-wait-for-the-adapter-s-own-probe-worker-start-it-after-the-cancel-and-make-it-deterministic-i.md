---
id: T124
title: "Let the give-up grace also wait for the adapter's own probe worker, start it after the cancel, and make it deterministic in the tests"
layer: "app"
deps: ["T123"]
blocks: []
acs: ["AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Fakes/CountingTimeProvider.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-23 (Y2, Y3)"
---

<!-- Follow-up task from review-2026-11-23. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23.md) · repo `CLAUDE.md`. Do not guess. -->

# T124 — Let the give-up grace also wait for the adapter's own probe worker, start it after the cancel, and make it deterministic in the tests

## Why

- **Y2**. The X10 grace (`AwaitGrace`, `PlaybackSession.cs:788-805`) waits only on the session's wrapper in `_workers`. `FfmpegPictureProbe.Probe` (`src/DualPlayer.Media/Video/FfmpegPictureProbe.cs:79-102`) returns as soon as its limit runs out or the caller cancels, without waiting for its `ProbeCore` worker, which stays in `_running`, so `IsBusy(path)` stays true until FFmpeg's next interrupt check. A queued second Page Down then steps past the healthy cut file as `skipped … unreadable` (`PlaybackSession.cs:826-831`) and plays the one after it. The test passes only because its fake keeps the wrapper alive and leaves `IsBusy` false.
- **Y3**. The X10 test (`PlaybackSessionNeighbourMemoryTests.cs:251-267`) failed 2 of 10 parallel runs ("should be Episode-03.mkv but was Episode-04.mkv"): `graceEnd` is computed before `cancel.Cancel()` (`PlaybackSession.cs:962` vs `:775`), and the rig keeps advancing fake time while the fake's `Waiting()` scope is open (`EngineRig.cs:199-205`, `CountingTimeProvider.cs:106-110`), so the grace can be spent before the cancel. `AwaitGrace` also busy-spins a core for up to 50 ms.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [ ] The grace starts after `cancel.Cancel()` and waits, bounded by the same end, until both the session's worker has ended and `_pictureProbe.IsBusy(path)` is false (for a listing worker, the worker alone); it polls with a blocking wait (e.g. `worker.Wait(1)`), not a busy spin.
- [ ] Test with an adapter-shaped fake: it returns `video.probe_timeout` at the cut limit and keeps `IsBusy` true for a short while after the cancel (like `StayBusyAfterTimeout`); two presses with no wait between them → the second press plays the healthy file, no `skipped` line for it. Fails if the `IsBusy` wait is removed.
- [ ] In the test fakes the `Waiting` scope ends from a `cancel.Register` callback, so fake time stops moving inside `Cancel()` and the grace is deterministic; the X10 test and the new test pass 20 parallel runs in a row.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
