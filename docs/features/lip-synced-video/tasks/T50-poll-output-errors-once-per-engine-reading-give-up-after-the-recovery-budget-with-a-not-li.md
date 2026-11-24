---
id: T50
title: "Poll output errors once per engine reading, give up after the recovery budget with a not-lip-synced record, and hold picture timing until a pending align"
layer: "app"
deps: ["T49"]
blocks: []
acs: ["AC-04", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Playback/SessionEvent.cs", "src/DualPlayer.Core/Sync/PictureSync.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "docs/features/lip-synced-video/contracts/cli.md", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/Startup/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21b (Q5, Q6)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T50 — Poll output errors once per engine reading, give up after the recovery budget with a not-lip-synced record, and hold picture timing until a pending align

## Why

Q5: while a "lip-synced again" record is owed and the picture is within 20 ms, every 5 ms sync tick makes a blocking `_loop.Execute` call (`PositionErrors`, `PlaybackSession.cs:1101-1152`, `AudioEngine.cs:87-91`), though readings change only every 100 ms. If an output never gets within 10 ms, polling runs until the next command and nothing records that recovery failed.

Q6: while `_pendingAlign` is set, `ResumeCore` (`:851`) and the health tick (`:1289-1292`) still call `sync.Measure()` with stale `_holdUntil`/`_commandedAt` (`PictureSync.cs:192-209`), so an early resume logs `PictureTimingReported` with an error up to the picture delay (e.g. +150 ms) though the picture is in step. T23 scenario L2 reads these records.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04, AC-17.

## Definition of Done

- [x] Output errors are queried from the engine only when its reading time has moved on (about every 100 ms), never on every 5 ms tick.
- [x] After the recovery budget (1 s; 3 s for a file change, per spec §6) a `NotLipSynced`-style record is published with the cause, picture error and per-output errors, and polling stops; cli.md §3 lists the new record and `SessionEventLogWriter` writes it.
- [x] While `_pendingAlign` is set, picture timing is treated as a hold: no `PictureTimingReported` is published until after the align.
- [x] Tests: an output that never recovers → one give-up record after the budget and a bounded number of engine queries; resume 30 ms and 150 ms after a pause with a 200 ms delay → no `PictureTimingReported` with |error| > 20 ms; log writer test for the new record.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
