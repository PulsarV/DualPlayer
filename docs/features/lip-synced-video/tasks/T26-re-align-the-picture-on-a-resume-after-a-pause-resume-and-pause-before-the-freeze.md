---
id: T26
title: "Re-align the picture on a resume after a pause, resume and pause before the freeze"
layer: "app"
deps: ["T25"]
blocks: []
acs: ["AC-04"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-20 (F2)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T26 — Re-align the picture on a resume after a pause, resume and pause before the freeze

## Why

F2 (stage-1, AC-04): Pause → Resume → Pause before the freeze timer fires (`PlaybackSession.cs:478-489, 536-545, 665-673`). The second Pause disposes the timer but leaves `_pendingAlign` set; the new freeze then starts the picture while the engine is paused, and the next Resume sees `_pictureFrozen == false` and never re-aligns it.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04.

## Definition of Done

- [x] A Pause clears any pending align; Pause → Resume → Pause → (freeze) → Resume leaves the picture paused through the second pause and aligned with the sound after the last resume.
- [x] Test for that exact sequence with the fake clock and renderer; it fails on the current code.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
