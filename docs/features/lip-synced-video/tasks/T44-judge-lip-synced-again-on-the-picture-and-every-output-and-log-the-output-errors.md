---
id: T44
title: "Judge lip-synced again on the picture and every output and log the output errors"
layer: "app"
deps: ["T43"]
blocks: []
acs: ["AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/SessionEvent.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Playback/IOutputLagControl.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/Startup/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21 (R10 (F12 remainder))"
status: "todo"
step: "—"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T44 — Judge lip-synced again on the picture and every output and log the output errors

## Why

R10 (stage-1, AC-17, spec §6 recovery row: picture within 20 ms and every output within 10 ms; log each output's position error): `LipSyncedAgain` is published as soon as the picture is within 20 ms (`PlaybackSession.cs:1025-1029`) and carries no output errors (`SessionEvent.cs:90`), so the record can claim recovery while an output is still re-aligning.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17.

## Definition of Done

- [ ] `LipSyncedAgain` is published only when the picture is within 20 ms and every live output's position error is within 10 ms (from the engine's sync records / lag control).
- [ ] The record carries each output's position error; `SessionEventLogWriter` writes them per cli.md §3.
- [ ] Tests: an output that recovers slowly delays `LipSyncedAgain` until it is within 10 ms; the log record contains the per-output errors.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
