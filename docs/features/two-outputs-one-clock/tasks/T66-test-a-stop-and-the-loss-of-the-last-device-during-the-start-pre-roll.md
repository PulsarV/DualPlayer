---
id: T66
title: "Test a stop and the loss of the last device during the start pre-roll"
layer: "app"
deps: ["T65"]
blocks: []
acs: ["AC-08", "AC-06b", "AC-13"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-17: S3"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-17. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17.md) · repo `CLAUDE.md`. Do not guess. -->

# T66 — Test a stop and the loss of the last device during the start pre-roll

## Why

- S3: nothing tests a stop, or the loss of the last device, before the clock starts. With the `run.Starting` branch of `EndRun` (`AudioEngine.cs:929-935`) deleted, all Core tests but the B1 flake stayed green, although `test-plan.md:83` lists "pause, seek, stop … while the run is still starting".
- After the release but before the clock starts, the slowest output already has up to `L_max` of film queued in the device while faster outputs have only silence. `SetSilenceFrom(0)` + `StopDeviceFrame = 0` relies on `Close` discarding that queue; otherwise AC-08 "fall silent within 10 ms of each other on stop" does not hold.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-06b, AC-13; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [x] Tests: stop before the release, stop after the release but before the clock start, last device lost before the release, last device lost after the release; each asserts a run summary is produced (AC-13) and no film is heard after the stop / loss.
- [x] Mutation check: deleting the `run.Starting` branch of `EndRun` turns at least one of them red.
- [x] The reliance on `Close` discarding queued device audio is either handled in code or documented at the stop path (and in sad.md if it is an assumption on the Windows backend).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
