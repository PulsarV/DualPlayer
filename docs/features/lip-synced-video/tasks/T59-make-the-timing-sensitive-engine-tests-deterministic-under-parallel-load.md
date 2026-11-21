---
id: T59
title: "Make the timing-sensitive engine tests deterministic under parallel load"
layer: "test"
deps: []
blocks: []
acs: []
files_hint: ["tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineSyncRecordTests.cs", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "implement run 2026-11-21 (flaky engine tests)"
status: "todo"
step: ""
---

# T59 — Make the timing-sensitive engine tests deterministic under parallel load

## Why

During the T48–T58 run, two engine tests failed intermittently in full `dotnet test` runs (about 1 in 3–5 runs at `d4e23c3` and `e0964e9`; 0 in 5 at `ea94ec2`), which blocks the per-task gate:

- `AudioEngineRunTests.Audio_started_is_published_once_when_the_last_output_renders_its_first_audio_with_that_start_to_audio` (`AudioEngineRunTests.cs:431-452`): `rig.WaitFor(rig.Events.OfType<AudioStarted>().Any())` was false after 10 s. The test renders 5 steps and then waits without rendering; if the engine has not left its pre-roll by then, nothing pumps the devices again.
- `AudioEngineSyncRecordTests.An_injected_step_error_logs_the_one_step_re_alignment_and_the_raw_readings_of_every_output_around_it` (`AudioEngineSyncRecordTests.cs:488`): the "other" output's reading around the step had `Measured=False`.

## Acceptance criteria

n/a (test reliability; the gate of every task depends on it).

## Definition of Done

- [ ] `AudioEngineRunTests.Audio_started_is_published_once_when_the_last_output_renders_its_first_audio_with_that_start_to_audio` no longer depends on the engine having left its pre-roll before a fixed number of `PumpRender` calls: it waits for an observable ready signal (or keeps rendering while waiting) and still asserts the start-to-audio bound it is meant to check.
- [ ] `AudioEngineSyncRecordTests.An_injected_step_error_logs_the_one_step_re_alignment_and_the_raw_readings_of_every_output_around_it` no longer fails when the reading around the step is not yet measured (`Measured=False`) under load: it waits for measured readings, without weakening what it asserts.
- [ ] Any other engine test that pumps a fixed number of steps and then waits without rendering is fixed the same way.
- [ ] No production code is changed unless a real engine race is found (then it is fixed with a test).
- [ ] `dotnet test tests/DualPlayer.Core.Tests` passes 10 runs in a row on the dev Mac (record the count in the task note).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
