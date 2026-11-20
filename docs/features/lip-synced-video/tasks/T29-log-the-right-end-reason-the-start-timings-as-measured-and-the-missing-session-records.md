---
id: T29
title: "Log the right end reason, the start timings as measured, and the missing session records"
layer: "app"
deps: ["T28"]
blocks: []
acs: ["AC-01", "AC-06", "AC-06b", "AC-07", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Diagnostics/", "src/DualPlayer.Core/Sync/LatencyModel.cs", "src/DualPlayer.App/Startup/RunSummaryLines.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "src/DualPlayer.App/App.axaml.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/Startup/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-20 (F9, F12, F13, F16)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T29 — Log the right end reason, the start timings as measured, and the missing session records

## Why

F9 (cli.md §2.1): the run-summary `reason` is never `file-change` or `picture-failed`; both paths stop with `RunEndReason.Stop`, printed `exit` (`RunSummaryLines.cs:37-42`; `PlaybackSession.cs:292, 740`).
F12 (AC-17, cli.md §3, sad flows 4/5/7): `PictureDelayChanged(FileChange)` is never emitted, nothing records "lip-synced again" after a command, `OffsetChanged.SavedAt` is always null, `DamagedStretchSkipped` is not written (`PlaybackSession.cs:298-305, 393`; `SessionEventLogWriter.cs:28-75`).
F13 (AC-01, §6 start budget): `StartTimings` logs the plan — first-frame and every first-audio equal `clockStart + PD`; the command time is taken inside `Session.Start`, after the probe and window creation (`PlaybackSession.cs:103, 186-188`).
F16 (ADR-0004): `DeviceLatencyReport.Source` is dropped before logging, and a failed latency probe silently becomes 0 with no log (`PlaybackSession.cs:116-120`; `LatencyModel.cs:2-10`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-06, AC-06b, AC-07, AC-17.

## Definition of Done

- [ ] File change and picture failure end the run with reasons that print `file-change` / `picture-failed` (cli.md §2.1); the engine's own reasons are unchanged.
- [ ] Emitted and logged: `PictureDelayChanged` with cause `FileChange`; a "lip-synced again" record once the picture is within 20 ms after a command (from the T25 tick); `OffsetChanged.SavedAt` set when the debounced save lands; `DamagedStretchSkipped` written by the log writer.
- [ ] `StartTimings` carries the command time from `Program`/App (before validation) and the measured first presented frame and first audio per output (engine `AudioStarted` / renderer), not the plan.
- [ ] The latency log line carries the report's `Source`; a failed probe logs a warning with the error and the fallback used.
- [ ] Tests for each record, asserting the measured values differ from the plan when the fakes say so.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
