---
id: T72
title: "Report the last measured picture error when giving up after a correcting seek"
layer: "app"
deps: ["T71"]
blocks: []
acs: ["AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Sync/PictureSync.cs", "tests/DualPlayer.Core.Tests/Playback/"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21d (R9)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T72 — Report the last measured picture error when giving up after a correcting seek

## Why

R9: `NotLipSynced` reports `ErrorMs = null` although frames were measured. After every correcting seek `PictureSync.Tick()` sets `_commandedAt = now` and rejects older snapshots until mpv presents a frame after the seek (`PictureSync.cs:169-173, 202`), so most 5 ms ticks return null. When the picture stays >100 ms off, the 1 s give-up usually lands in `GiveUpWithoutPicture` (`PlaybackSession.cs:1181-1185, 1266-1282`) and logs null exactly when T23 needs the error. cli.md §3 allows null only when no frame was measurable after the command.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17.

## Definition of Done

- [ ] The session remembers the last picture timing measured after `_resync.At`; `NotLipSynced` publishes its error, and null only when no timing was measured after the command.
- [ ] Test: a picture that stays 150 ms off with correcting seeks in between → `NotLipSynced` with `ErrorMs ≈ 150`, not null.
- [ ] The T63 test for no measurable frame still publishes null.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
