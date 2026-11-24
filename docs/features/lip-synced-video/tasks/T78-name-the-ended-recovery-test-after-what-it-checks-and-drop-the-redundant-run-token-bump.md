---
id: T78
title: "Name the ended-recovery test after what it checks and drop the redundant run-token bump"
layer: "test"
deps: ["T76"]
blocks: []
acs: ["AC-05"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionEndedRecoveryTests.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21d (R11)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T78 — Name the ended-recovery test after what it checks and drop the redundant run-token bump

## Why

R11: the rewritten stale-summary test (`PlaybackSessionEndedRecoveryTests.cs:68-92`) pins nothing: with the `_runToken` bump in `ChangeFile` (`PlaybackSession.cs:551`) removed, all EndedRecovery and ReturnGuard tests stay green. After T60 `_filmLoaded = false` makes `_endPosition` unobservable after a failed change, and episode 3's own summary overwrites it anyway; the test only checks "← after all outputs lost lands at 0". Owner decision: rename it to what it checks; drop the bump if it is redundant.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05.

## Definition of Done

- [x] The test's name states what it checks (← after every output was lost on the next film lands at 00:00).
- [x] The `_runToken` increment in `ChangeFile` is removed if no test or behaviour depends on it; if something does, a test that fails without it is added instead and the reason is written in the task notes.
- [x] RED for this task: show (in the notes) the full Core suite green with the bump removed before deciding.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
