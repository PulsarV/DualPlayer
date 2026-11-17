---
id: T74
title: "Render at each release step while a moved output still awaits its start target"
layer: "app"
deps: ["T73"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r3: F3"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-17-r3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T74 — Render at each release step while a moved output still awaits its start target

## Why

- F3: the T71 guard (`AudioEngineStartAndHangTests.cs:177`, `:500`) counts any hook render with `TryRelease` on the stack — frequency reads before the reset, waiting polls — so `Renders > 0` holds almost always. Old reset order + the `TimestampFrequency` read moved before the reset loop keeps the race test green 5/5: the guard would not notice the scenario going stale.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [x] `AudioEngine.TryRelease` exposes an internal test seam called on the control loop after each start-target change, with the number of moved outputs still awaiting their heard target.
- [x] The race test renders every device at each such step while a moved output still awaits its target, asserts that happened at least once, and that every device first hears film frame 0 at the clock start.
- [x] Mutation: resetting every output before `StartWhenHeardAt` (the old order) turns the race test red, independent of where the engine reads the timestamp frequency.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
