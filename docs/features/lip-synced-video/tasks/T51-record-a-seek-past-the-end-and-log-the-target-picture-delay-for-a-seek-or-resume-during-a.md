---
id: T51
title: "Record a seek past the end and log the target picture delay for a seek or resume during a move"
layer: "app"
deps: ["T50"]
blocks: []
acs: ["AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Sync/PictureSync.cs", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21b (Q12)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T51 — Record a seek past the end and log the target picture delay for a seek or resume during a move

## Why

Q12: a seek past the end returns before `PublishDelay` (`PlaybackSession.cs:921-936` vs `:967`), so it leaves no seek record. During a picture-delay move, `SeekTo` and `ResumeCore` (`:825/850`, `:921/958`) read `sync.PictureDelay` (the half-way value) before `AlignAt` jumps it to the target, so the record logs the half-way value as the new picture delay.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17.

## Definition of Done

- [x] A seek past the end publishes the same seek record as any other seek.
- [x] A seek or resume during a move records the delay the film actually aligned at (read after `AlignAt`, e.g. `TargetPictureDelay`).
- [x] Tests: seek past the end → one seek `PictureDelayChanged`; seek and resume during a move → new delay equals the target.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
