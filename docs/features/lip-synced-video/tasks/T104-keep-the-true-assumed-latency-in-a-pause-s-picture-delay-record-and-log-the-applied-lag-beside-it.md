---
id: T104
title: "Keep the true assumed latency in a pause's picture-delay record and log the applied lag beside it"
layer: "app"
deps: ["T103"]
blocks: []
acs: ["AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/SessionEvent.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionOffsetTests.cs", "tests/DualPlayer.App.Tests/Startup/SessionEventLogWriterTests.cs", "docs/features/lip-synced-video/contracts/cli.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22d (V6)"
---

<!-- Follow-up task from review-2026-11-22d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22d.md) · repo `CLAUDE.md`. Do not guess. -->

# T104 — Keep the true assumed latency in a pause's picture-delay record and log the applied lag beside it

## Why

- **V6** (all three reviewers). On pause, `WithAppliedLag` (`PlaybackSession.cs:1348-1361`) sets `AssumedMs = picture delay − applied lag`, while the record keeps `ReportedMs`, `DefaultMs` and `OffsetMs` (`SessionEventLogWriter.cs:161-173`). cli.md:52 defines assumed as reported + default − offset. In the T99 rig, +10 ms on the slowest output and a pause mid-ramp at delay 238 logs assumed 238 instead of 230, while `LatencyEstimated` and the offset line say 230. The AC-17 split no longer adds up. `PlaybackSessionOffsetTests.cs:35-51` checks only `lag = PD − assumed`.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17.

## Definition of Done

- [ ] Every `PictureDelayChanged` record (every cause, including `pause` mid-ramp) carries `AssumedMs == ReportedMs + TypeDefaultMs − OffsetMs`; the pause record still carries the lag the engine applies, so a mid-ramp pause logs the real applied lag without rewriting assumed.
- [ ] cli.md §1.1 (:52-53) and §3 `PictureDelayChanged` (:104) say what a mid-ramp `pause` record carries (the applied lag, so lag ≠ delay − assumed there).
- [ ] Tests: the T99 case asserts both the applied lag (vs `Engine.CurrentLag`) and `AssumedMs == ReportedMs + TypeDefaultMs − OffsetMs` on every record; fails on the current code.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
