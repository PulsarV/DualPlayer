---
id: T28
title: "Hold the last frame at the end and on a seek past it, and return into the film without a restart"
layer: "integration"
deps: ["T27"]
blocks: []
acs: ["AC-05", "AC-06", "AC-06b", "AC-08"]
files_hint: ["src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/IVideoRenderer.cs", "tests/DualPlayer.Core.Tests/Fakes/FakeVideoRenderer.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Media.Tests/Video/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-20 (F5, F6)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T28 — Hold the last frame at the end and on a seek past it, and return into the film without a restart

## Why

F5 (stage-1, AC-06/AC-06b/AC-08, SCR-02): mpv runs with `keep-open=no` and the session ignores `EndOfFile` (`MpvVideoRenderer.cs:211-221, 868-871`; `PlaybackSession.cs:82-84, 837-855`). At the end the video output is torn down instead of holding the last frame, and notes after the end have nothing to draw on.
F6 (stage-1, AC-05/AC-06): a seek past the end freezes on the current frame instead of the last one. A backward seek at the end restarts fully: frame 0 is shown, a new pre-roll runs, the start lines print again and the run summary resets (`PlaybackSession.cs:570-583, 606-612`). The test checks only the state.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-06, AC-06b, AC-08.

## Definition of Done

- [x] mpv keeps the last frame on screen at the natural end and after a seek past the end (`keep-open` that pauses on the last frame); notes still draw over it.
- [x] A seek past the end shows the last frame, not the current one.
- [x] A backward seek at the end returns into the film at the target as a seek does (picture and sound together), without showing frame 0, without a second set of start lines and without resetting the run summary — or, if the engine cannot reopen a finished run without a new run, the restart shows no frame 0 and the summary carries over; record the choice in the task note.
- [x] Tests assert the frame shown and the events (no second `StartTimings`, one summary), not only the state; a Media test (hardware-gated if needed) checks `keep-open`.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

The engine cannot reopen a run that has ended and has no start position, so a backward seek at the end starts a new engine run and seeks it to the target at once (the fallback this DoD allows). mpv keeps the file loaded and its picture paused, so no frame 0 is shown and no start lines or latency lines repeat. Two limits remain for re-review: the outputs can play the first few tens of ms of frame-0 audio between the new run's clock start and the seek, and the returned run ends with its own run summary (the one printed at the end stays). A start position in the engine (`EngineOptions`) would remove both; that is a step-3 engine change, not done here.
