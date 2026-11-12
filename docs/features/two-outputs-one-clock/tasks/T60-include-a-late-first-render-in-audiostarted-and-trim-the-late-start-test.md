---
id: T60
title: "Include a late first render in AudioStarted and trim the late-start test"
layer: "app"
deps: ["T59"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r5 (U4, U5)"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from re-review 5. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r5.md) · repo `CLAUDE.md`. Do not guess. -->

# T60 — Include a late first render in AudioStarted and trim the late-start test

## Why

U4 (stage-2, AC-01, spec §6 start-to-audio): an output whose first render is > 1 s late counts as stalled (`IsStalled` compares against the pump start) and is not awaited in `LogFirstAudio` (`AudioEngine.cs:741-742,753-757`), so `AudioStarted.StartToAudio` (the console `started` line) covers only the faster output — the r4 T5 under-report remains.
U5 (stage-2, tests): the T54 test `An_output_stalled_at_start_that_recovers_after_audio_started_still_logs_its_start_to_audio` calls `rig.Render(400)` (`AudioEngineRunTests.cs:538`), each step with `Thread.Sleep(1)` — ~6 s wall time on Windows (15.6 ms timer) for steps it does not need.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [ ] Until the start deadline, `LogFirstAudio` keeps awaiting an output that has never rendered (no read yet, e.g. `LastReadTimestamp == 0`), so `AudioStarted` covers it; an output that hangs after rendering, or one that never renders by the deadline, is still not awaited (the run does not wait forever).
- [ ] A test where one device first renders > 1 s (fake time) after start but before the deadline: `AudioStarted.StartToAudio` ≥ that device's delay; it fails on the current code.
- [ ] The T54 late-start test is adjusted to the new rule if needed and its `Render(400)` cut to ~20–30 steps while it still fails on the pre-T54 code.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
