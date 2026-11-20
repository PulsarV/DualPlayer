---
id: T25
title: "Tick the picture-sync loop in production and measure picture error at the snapshot's own time"
layer: "app"
deps: ["T24"]
blocks: []
acs: ["AC-01", "AC-04", "AC-05", "AC-14", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Sync/PictureSync.cs", "tests/DualPlayer.Core.Tests/Fakes/FakeVideoRenderer.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Sync/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-20 (F1, F11)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T25 — Tick the picture-sync loop in production and measure picture error at the snapshot's own time

## Why

F1 (stage-1, ADR-0002): nothing in production calls `PictureSync.Tick()` (`PlaybackSession.cs:183-204, 744-796`; `PictureSync.cs:136`). The session never speed-steers the picture and never seeks it back after a large error; the D1 spike measured the ticked path, not the shipped one. `FakeVideoRenderer` stamps every snapshot "now", which hides this.
F11 (stage-1, AC-17/AC-04): `PictureTiming` in `PublishTiming` computes due at `now` but compares it with `presented.Position`, which belongs to `presented.Timestamp` — the error is skewed by the snapshot's age (up to ~40 ms; predictions can be stamped in the future). The pause freeze (`PlaybackSession.cs:477`) uses the same stale position.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-04, AC-05, AC-14, AC-17.

## Definition of Done

- [x] The session ticks `PictureSync` on the serialized session thread (T24) at the D1-spike cadence while playing; the logged `PictureTimingReported` comes from the tick's `PictureTiming` (due at `presented.Timestamp`), not a separate now-based computation.
- [x] The pause freeze projects the presented position to now (`presented.Position + (now − presented.Timestamp) × speed`) before computing the remaining run-out.
- [x] `FakeVideoRenderer` can return aged and future-stamped snapshots and a picture that drifts or lags; a session test shows a late/drifting renderer steered back within 20 ms in ≤ 1 s, and a > 100 ms error corrected by a seek — both fail on the current code.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
