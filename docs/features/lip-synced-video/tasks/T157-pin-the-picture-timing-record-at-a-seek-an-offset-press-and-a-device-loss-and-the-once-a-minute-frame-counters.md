---
id: T157
title: "Pin the picture-timing record at a seek, an offset press and a device loss, and the once-a-minute frame counters"
layer: "test"
deps: []
blocks: ["T23", "T161"]
acs: ["AC-17", "AC-06b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPictureHealthTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23i (AA4)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T157 — Pin the picture-timing record at a seek, an offset press and a device loss, and the once-a-minute frame counters

## Why

- **AA4.** Deleting any of the three `ReportTiming(sync)` calls (`PlaybackSession.cs:1133, 1479, 2132`) passes Core 626/626 — the record then comes only from the next 1 s tick; only pause and resume are pinned (`PlaybackSessionPictureHealthTests.cs:143`). `TicksPerCounterReport = 30` (:120) passes because the counter test accepts 1–2 reports in 60 s (:125-126). cli.md:107 says `PictureTiming` is logged at every event above and once per second; cli.md:108 says `FrameCounters` per minute.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17, AC-06b.

## Definition of Done

- [ ] A seek while playing, an offset press while playing and a loss that moves the picture delay each yield a `PictureTimingReported` within a few 5 ms ticks, well before the 1 s tick.
- [ ] Exactly one `FrameCountersReported` at 60 s of playback, none before.
- [ ] Mutants on a scratch copy, each failing at least one new test: each of the three `ReportTiming(sync)` calls removed; `TicksPerCounterReport = 30`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
