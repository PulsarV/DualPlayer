---
id: T106
title: "Gate the neighbour tests' fake time on the fakes themselves, with no real-time cap"
layer: "test"
deps: ["T102"]
blocks: []
acs: ["AC-08", "AC-09b"]
files_hint: ["tests/DualPlayer.Core.Tests/Fakes/", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourWorkerTimingTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22d (V9)"
---

<!-- Follow-up task from review-2026-11-22d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22d.md) · repo `CLAUDE.md`. Do not guess. -->

# T106 — Gate the neighbour tests' fake time on the fakes themselves, with no real-time cap

## Why

- **V9**. The T100 DoD is not met. The `TimedDecoder`, `TimedProbe` and `ScriptedProbe` fakes spin on fake time and do not advance it. The `Call` gate (`tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs:174-182`) opens on any new timer from any thread, or after 2 s of `DateTime.UtcNow`. A neighbour scan creates no timer, so the gate is a 2 s real-time wait per press (9 tests take 23 s), and the playing film's DecodePump re-arming `Task.Delay(WaitSlice, _time)` (`src/DualPlayer.Core/Playback/DecodePump.cs:366`) opens it early. Under pool starvation or an early open, E2's listing starts late, its probe is cut, and `A_file_skipped_for_its_picture_keeps_that_reason…` (`PlaybackSessionNeighbourMemoryTests.cs:118`) fails.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [x] Fake time in the neighbour tests moves only on an explicit signal from the fakes (e.g. the scripted decoder/probe advance `FakeTimeProvider` by their scripted cost when entered), never on an unrelated timer and never on a real-time cap.
- [x] No neighbour test waits on real time: the neighbour and remembered-file test classes run in well under 2 s each, and `PlaybackSessionNeighbourWorkerTimingTests` no longer relies on `Thread.Sleep`.
- [x] A rig self-test proves that a DecodePump timer does not move fake time during a neighbour scan.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
