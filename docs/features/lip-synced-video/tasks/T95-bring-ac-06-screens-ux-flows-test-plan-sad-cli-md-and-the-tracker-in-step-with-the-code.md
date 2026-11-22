---
id: T95
title: "Bring AC-06, screens, ux-flows, test-plan, sad, cli.md and the tracker in step with the code"
layer: "docs"
deps: ["T88", "T89", "T90", "T91", "T92", "T93", "T94"]
blocks: []
acs: ["AC-06", "AC-04", "AC-06b", "AC-07", "AC-09b", "AC-17"]
files_hint: ["docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/ux-flows.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/contracts/cli.md", "tests/DualPlayer.Core.Tests/Playback/"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22b (S5, S13)"
---

<!-- Follow-up task from review-2026-11-22b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22b.md) · repo `CLAUDE.md`. Do not guess. -->

# T95 — Bring AC-06, screens, ux-flows, test-plan, sad, cli.md and the tracker in step with the code

## Why

**S5.** After a natural end, the last output lost, a picture failure or a failed file change, the run has already stopped and printed its summary, so Esc prints nothing more (`PlaybackSession.cs:1660`). screens.md SCR-02 "closed" (`:77`), the Ended → Esc edge in ux-flows.md (`:54-55, :77`) and AC-06 ("at every … exit") say Esc prints a run summary. Owner decision: keep the code, fix the docs, pin the behaviour with a test.

**S13.** Docs drift T87 missed:
- test-plan.md: no rows for the T80/T82/T84/T85/T86 tests (`PlaybackSessionPictureFailureReloadTests`, the lost-output cases, `PlaybackSessionAbandonedWorkerTests`, Media `AbandonedWorkerTests`, `PlaybackSessionKeyArrivalTests`, `PlaybackControlsKeyArrivalTests`, `MpvSpeedReplyTests`) nor for T88–T94's; row :136 omits the queue wait; the Levels table (:24) says e2e-through-UI uses the real session, but :149 and the tests use a substitute `IPlaybackControls`.
- sad.md §5 (:128, :136) says the session runs on the engine control loop; §8 and the code give it its own `EngineControlLoop(…, "PlaybackSession")`.
- sad.md §2 (:48) and §11 (:598) omit `Error.Fields`, the T54 flush and test seam in `JsonFileStateStore`, and the new Core registrations (`IOutputLagControl`, `OffsetBook`, `PlaybackSession`).
- cli.md examples pad columns (`:39-40, :87-89`), but the code separates fields by two spaces (`TerminalPrinter.cs:121, 124, 155`).

Owner decision: one docs task at the end; cli.md says fields are separated by two spaces and the examples' alignment is illustrative.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06, AC-04, AC-06b, AC-07, AC-09b, AC-17.

## Definition of Done

- [ ] AC-06, screens.md SCR-02 "closed" and the ux-flows Ended → Esc edge say Esc after the end adds nothing (the end's summary already covers the file); a Core test pins that Esc after Ended publishes no second run summary.
- [ ] test-plan.md has rows for every T80–T94 test, row :136 counts the queue wait, and the Levels table describes the e2e-through-UI substitute truthfully; every test name in test-plan.md exists.
- [ ] sad.md §5 describes the session's own control loop; sad.md §2 and §11 list `Error.Fields`, the `JsonFileStateStore` flush/test seam and the new Core registrations, plus any Core change T88–T94 made.
- [ ] cli.md states fields are separated by two spaces and the examples' alignment is illustrative.
- [ ] Every DoD box of T88–T94 that holds is ticked.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
