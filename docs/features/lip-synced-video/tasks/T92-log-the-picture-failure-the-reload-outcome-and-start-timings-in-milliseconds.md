---
id: T92
title: "Log the picture failure, the reload outcome and start timings in milliseconds"
layer: "app"
deps: ["T90"]
blocks: []
acs: ["AC-06b", "AC-01", "AC-07", "AC-17"]
files_hint: ["src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/", "tests/DualPlayer.App.Tests/Startup/", "tests/DualPlayer.Core.Tests/Playback/", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22b (S6, S7)"
---

<!-- Follow-up task from review-2026-11-22b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22b.md) · repo `CLAUDE.md`. Do not guess. -->

# T92 — Log the picture failure, the reload outcome and start timings in milliseconds

## Why

**S6.** The T23 guide (`evidence/t23-acceptance-run-guide.md:80`) asks the owner to find `video.decode_failed` and the paused reload in the log, but neither is written: the renderer raises the error without logging it (`MpvVideoRenderer.cs:919`), `SessionEventLogWriter` has no `PictureFailed` case, and `ReloadLastFrame` logs nothing (`PlaybackSession.cs:1572-1593`).

**S7.** `StartTimings` logs raw `TimeProvider` ticks (`CommandTimestamp`, `FirstFramePresented`, `FirstAudioHeard`; `SessionEventLogWriter.cs:91-97`) and the tick frequency is logged nowhere, yet the T23 guide L1, L4 and §7 (`:62, :65, :90`) read the start times from this record.

Owner decision: fix the code (not just the guide).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-01, AC-07, AC-17.

## Definition of Done

- [x] The session log writes a `PictureFailed` record with the error code and reason, and the reload outcome (reloaded at position / failed / timed out).
- [x] `StartTimings` logs elapsed milliseconds from the command to the first frame and to the first sound; cli.md §3 states the fields and the unit.
- [x] The T23 guide items (:62, :65, :80, :90) name the exact record and fields to read.
- [x] Tests for both log lines.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
