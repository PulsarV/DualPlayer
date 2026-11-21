---
id: T57
title: "Make the exit and pending-align assertions bite and test Space through the real toggle"
layer: "test"
deps: ["T50"]
blocks: []
acs: ["AC-04", "AC-06"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionExitTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPendingAlignTests.cs", "tests/DualPlayer.App.Tests/E2E/PlayEndToEndTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21b (Q8, Q10)"
status: "blocked"
step: "GATE — no gate result (red phase false_pass, gate not run)"
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T57 — Make the exit and pending-align assertions bite and test Space through the real toggle

## Why

Q8: the T39 exit tests' `AudioStartedCount` check (`PlaybackSessionExitTests.cs:47, 63, 91`) no longer catches a return run, which publishes no `AudioStarted` since T41; the pending-align test's `foreach` over `LipSyncedAgain("resume")` (`PlaybackSessionPendingAlignTests.cs:56-59`) passes when no record exists.

Q10: Space → pause → Space → resume is never tested through the real `PlaybackControls.TogglePause` (`PlaybackControls.cs:30-31`); the window test uses a mock, the session tests call `Pause()`/`Resume()` directly. Test-plan row 50 is unchecked.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04, AC-06.

## Definition of Done

- [ ] Exit tests assert `rig.EngineRunFiles.Count` (or equivalent) is unchanged after exit.
- [ ] The pending-align test asserts the resume record exists before checking it.
- [ ] An E2E test calls the real `PlaybackControls.TogglePause` twice on the real session (Paused, then Playing) and once after the natural end (stays Ended).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
