---
id: T71
title: "Assert the release-race hook rendered inside the release"
layer: "app"
deps: ["T70"]
blocks: []
acs: ["AC-01"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r2: R4"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-17-r2. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T71 — Assert the release-race hook rendered inside the release

## Why

- R4: the race test (`AudioEngineStartAndHangTests.cs:154-176, 444-457`) depends on the `EngineControlLoop` thread name and on `TryRelease` reading `TimestampFrequency` between the reset loop and the target loop (`AudioEngine.cs:470`); if either changes, the test passes without exercising the window.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [x] The rig counts hook renders made from inside the release; the race test asserts the count is > 0.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
