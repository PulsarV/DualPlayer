---
id: T30
title: "Count a frame late when it is presented more than 20 ms after its due time"
layer: "integration"
deps: ["T29"]
blocks: []
acs: ["AC-17"]
files_hint: ["src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/IVideoRenderer.cs", "tests/DualPlayer.Media.Tests/Video/", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-20 (F14)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T30 — Count a frame late when it is presented more than 20 ms after its due time

## Why

F14 (AC-17, spec §6 picture presentation, cli.md §3 FrameCounters): the `Late` frame counter is always 0 because `vo-delayed-frame-count` counts only in display-sync mode (`MpvVideoRenderer.cs:157-161, 1033-1034`). The "> 20 ms after due" definition is computed nowhere.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17.

## Definition of Done

- [x] `FrameCounters.Late` counts frames presented more than 20 ms after due per spec §6 (due follows the picture delay; a deliberate ≤ 0.2 % move is not lateness) — computed from the presentation schedule / T25 tick errors, not from `vo-delayed-frame-count`.
- [x] Unit test with the fake renderer and clock: frames > 20 ms late are counted, ≤ 20 ms and during a move are not; fails on the current code.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
