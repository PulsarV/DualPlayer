---
id: T65
title: "Hand out film only after the start target is set on every moved output"
layer: "app"
deps: []
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-17: B1"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from review-2026-11-17. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17.md) · repo `CLAUDE.md`. Do not guess. -->

# T65 — Hand out film only after the start target is set on every moved output

## Why

- B1 (blocker): `AudioEngine.cs:457-465` `TryRelease` first calls `StartAtDeviceFrame(long.MinValue)` on **every** non-lost output (clears `_startHeardAt`, sets `_startDeviceFrame = MinValue`), and only in a second loop calls `StartWhenHeardAt` on the outputs that moved. A render thread on a moved output that runs between the two loops (`OutputRenderSource.cs:213-224`) sees no heard target and `startAt <= written`, so `lead = 0` and film frame 0 plays at once, ~`L_max` before the clock starts; the late `StartWhenHeardAt` then inserts a silence gap mid-film. Breaks ADR-0008 "no Viewer hears film early". Reviewer: 2/150 failures of the three-output start scenario on HEAD; a 30 ms sleep between the loops fails 3/9 `AudioEngineStartAndHangTests`. MMCSS render threads on Windows widen the window.
- Suggested: reset with `StartAtDeviceFrame(long.MinValue)` only the outputs **not** in `moved`; for moved outputs `StartWhenHeardAt` alone suffices (the next render sets `_startDeviceFrame` from `MaxValue`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [ ] A moved output never has a window where its start target is `MinValue` without a heard target: only non-moved outputs get `StartAtDeviceFrame(long.MinValue)`.
- [ ] A deterministic test (e.g. a seam/hook that renders between the reset and the target, or a rig that renders a moved output on release) fails against the old order and passes after; assert film frame 0 heard at the clock start (±10 ms) on every output.
- [ ] The three-output start scenario stays green under repetition (state the count run).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
