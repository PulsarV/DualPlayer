---
id: T69
title: "Retry the release when a moved output's queue read is torn"
layer: "app"
deps: []
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineReleaseTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r2: R5"
status: "todo"
step: "—"
---

<!-- Follow-up task from review-2026-11-17-r2. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T69 — Retry the release when a moved output's queue read is torn

## Why

- R5: past `FirstAudioDeadline` (`AudioEngine.cs:447-456`), a moved output whose `TryGetLatestQueue` read is torn by a concurrent render (`OutputRenderSource.cs:75-82`) is treated as not moved: it is reset to `StartAtDeviceFrame(long.MinValue)` (`:463-466`) and plays film at once, and its lag is left out of `clockStart`, so a one-step correction follows.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [ ] The per-output release reading is a pure decision (moved / wait / not moved) covered by unit tests: a torn read of an output with `LatestFramesPlayed > 0` that is not stalled waits, before and after the deadline; a stalled one does not hold the release.
- [ ] `TryRelease` returns false (retry on the next poll) in that case.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
