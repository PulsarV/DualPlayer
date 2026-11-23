---
id: T127
title: "Keep the rig's fake time from running past a fake's own deadline while its thread waits for the CPU, and steady the late-frame count test"
layer: "test"
deps: ["T125"]
blocks: []
acs: ["AC-08", "AC-17"]
files_hint: ["tests/DualPlayer.Core.Tests/Fakes/CountingTimeProvider.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Fakes/RigLoopsTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionLateFrameTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "implement run T122-T124 (flaky neighbour and late-frame tests under parallel load)"
---

<!-- Follow-up task found during the T122–T124 run. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [review](../_review/review-2026-11-23.md) · repo `CLAUDE.md`. Do not guess. -->

# T127 — Keep the rig's fake time from running past a fake's own deadline while its thread waits for the CPU, and steady the late-frame count test

## Why

- With 3 test processes in parallel, `PlaybackSessionNeighbourMemoryTests.A_file_skipped_for_its_picture_keeps_that_reason_and_is_not_probed_again_on_the_next_press_AC_08` fails about 1 run in 12 ("a file already judged was probed again"; also seen before T122). Diagnosis: the fake `ScriptedProbe.NotHardwareDecodableAfterLimit` spends its 2 s limit with `CountingTimeProvider.SpinUntil` (`tests/DualPlayer.Core.Tests/Fakes/CountingTimeProvider.cs` ~:100-115) while the rig keeps advancing fake time in 10 ms steps as long as a `Waiting()` scope is open (`EngineRig.cs` ~:199-205). When the fake's thread waits for the CPU, the rig runs fake time more than the 50 ms slack past the fake's deadline; the session's probe wait (`PlaybackSession.cs`, `elapsed(entered) >= probeLimit + ProbeTimingSlack`) then gives up on a probe that is about to answer, so E2 is remembered as timed out instead of judged not decodable, and the next press probes it again.
- `PlaybackSessionLateFrameTests.Frames_presented_30_ms_after_due_are_each_counted_late_once_AC_17` fails about 1 full Core run in 5 (`Late` = 28 against 22–27).
- Both are test-rig timing, not production code; they can turn CI red at random.

Owner decision: fix now, before the docs task T126.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-17.

## Definition of Done

- [ ] The rig never advances fake time past the earliest deadline of a fake that is spinning on it (`CountingTimeProvider.SpinUntil(end, …)`) until that fake has seen the deadline and left its waiting scope: e.g. `SpinUntil` registers its `end`, and the rig's advance clamps to the minimum registered deadline. A rig self-test: a fake spinning to a deadline whose thread is held for 200 ms of real time while the rig keeps advancing → fake time, when the fake returns, is at most one rig step past its deadline.
- [ ] `PlaybackSessionNeighbourMemoryTests.A_file_skipped_for_its_picture_keeps_that_reason_and_is_not_probed_again_on_the_next_press_AC_08` no longer depends on scheduling: the session never sees elapsed ≥ limit + slack before a fake that answers at its limit has returned.
- [ ] `PlaybackSessionLateFrameTests.Frames_presented_30_ms_after_due_are_each_counted_late_once_AC_17` (Late = 28 against 22–27 about 1 run in 5): the cause is found and fixed in the rig or the test (not by widening the range unless the spec tolerance justifies it; say which).
- [ ] All Core neighbour classes (`--filter "FullyQualifiedName~Neighbour"`) pass 12 of 12 runs at 3 processes in parallel (4 rounds), and the full Core suite passes 5 runs in a row.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
