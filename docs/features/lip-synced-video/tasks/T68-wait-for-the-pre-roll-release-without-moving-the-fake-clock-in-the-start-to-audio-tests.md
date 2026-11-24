---
id: T68
title: "Wait for the pre-roll release without moving the fake clock in the start-to-audio tests"
layer: "test"
deps: []
blocks: []
acs: []
files_hint: ["tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21c (S6)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T68 — Wait for the pre-roll release without moving the fake clock in the start-to-audio tests

## Why

S6: T59 moved the start-to-audio flake instead of removing it. `RenderUntil` advances the fake clock 10 ms per spin while waiting for the engine thread to release the pre-roll (`AudioEngineRunTests.cs:107-121`); the release computes `clockStart = Max(now, …) + StartMargin` (`AudioEngine.cs:549`), so every late step adds 10 ms to `StartToAudio` and the ≤ 360 ms bound fails under parallel load. The SlowStartDecoder test (`:493-519`) spends its delay in `WaitForStartLevel` (`AudioEngine.cs:199, 716-737`), before the first render, and duplicates the existing test.

## Acceptance criteria

No AC of its own: none (gate reliability / docs).

## Definition of Done

- [x] The start-to-audio tests wait for the release (`((IOutputLagControl)Engine).ClockStartTimestamp != null`, or settle the loop after each step as `RenderSettled` does) without advancing the fake clock, then render until `AudioStarted`; the bound is unchanged.
- [x] The SlowStartDecoder test is reworked so its delay falls after the first renders, or removed as a duplicate.
- [x] No production code changes unless a real engine race is found (then fixed with a test).
- [x] `dotnet test tests/DualPlayer.Core.Tests` passes 10 runs in a row, with the count recorded in the task note.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note

Stress run: `dotnet test tests/DualPlayer.Core.Tests` passed 10 of 10 runs in a row on the dev Mac (513 passed each). The SlowStartDecoder test was removed as a duplicate: `StartRun` waits for the pre-roll level, so a slow first read always lands before the first render. Mutation check: `StartMargin` 30 → 120 ms fails the start-to-audio bound.
