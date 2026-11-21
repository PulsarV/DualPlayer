---
id: T63
title: "Give up with a not-lip-synced record when no frame becomes measurable after a command"
layer: "app"
deps: ["T62"]
blocks: []
acs: ["AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionRecoveryBudgetTests.cs", "docs/features/lip-synced-video/contracts/cli.md"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21c (S8)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T63 — Give up with a not-lip-synced record when no frame becomes measurable after a command

## Why

S8: the recovery budget is checked only inside `OnSyncTick` after `Tick()` returned a timing (`PlaybackSession.cs:1116-1119, 1142-1148`); `PictureSync.MeasureCore` returns null while no frame newer than the command is presented (`PictureSync.cs:202`). If mpv presents no new frame after a seek or start (stalled decoder, slow disk, no `DecodeFailed`), `NotLipSynced` is never written.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17.

## Definition of Done

- [ ] When a lip-synced-again record is owed and there is nothing to measure, the budget is checked against `_time.GetTimestamp()`; past it, `NotLipSynced` is published once with a null (or last-known) picture error and the output errors, and polling stops.
- [ ] cli.md §3 says what the `NotLipSynced` record holds when no frame was measurable.
- [ ] Test: a seek after which the fake renderer presents no new frame → exactly one `NotLipSynced` after the budget, none before.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
