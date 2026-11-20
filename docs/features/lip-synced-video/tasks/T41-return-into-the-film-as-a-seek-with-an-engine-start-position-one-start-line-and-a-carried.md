---
id: T41
title: "Return into the film as a seek with an engine start position, one start line and a carried-over summary"
layer: "app"
deps: ["T40"]
blocks: []
acs: ["AC-05", "AC-06"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Playback/EngineOptions.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.App/Startup/TerminalPrinter.cs", "src/DualPlayer.App/Startup/RunSummaryLines.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/E2E/PlayEndToEndTests.cs", "tests/DualPlayer.App.Tests/Startup/"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "L"
origin: "review-2026-11-21 (R1 (and F6 remainder))"
status: "todo"
step: "—"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T41 — Return into the film as a seek with an engine start position, one start line and a carried-over summary

## Why

R1 (stage-1, AC-05/AC-06, cli.md §1.1/§2.1): a backward seek at the end (`PlaybackSession.cs:277-301, 838-840`) starts a new engine run from position 0, releases it, then seeks. Effects: (a) the outputs play the first tens of ms of the film's opening audio before the seek (T28 note; T23 guide §6); (b) the new run's `RunSummary` covers only the stretch after the return, against AC-06 "each covering the file's playback since it was opened"; (c) the new run publishes `AudioStarted(StartClockPosition = 0)` (`AudioEngine.cs:1084-1087`) with no matching `StartTimings`, so at the next summary `FlushPendingStarted` (`TerminalPrinter.cs:164-189, 202-215`) prints a stray `started   clock 00:00:00.000` line — start lines repeat only for a new file. The T28 test checks only session events.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-06.

## Definition of Done

- [ ] The engine can start a run at a given position (`EngineOptions` start position or equivalent), so a return into the film pre-rolls and starts at the target — no audio from position 0.
- [ ] The run summary printed/logged at the next end covers the file's playback since it was opened (carried over across the return), per AC-06 and cli.md §2.1.
- [ ] No second `started` line for a return (no stray `AudioStarted` reaches the printer, or the printer ignores it for a returning run).
- [ ] Tests: engine starts at a position (first rendered sample is at that position); session return asserts no frame-0 audio and one summary; a `PlayEndToEndTests` case: seek past the end, seek back, Esc → exactly one `started` line and one summary covering the whole file.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
