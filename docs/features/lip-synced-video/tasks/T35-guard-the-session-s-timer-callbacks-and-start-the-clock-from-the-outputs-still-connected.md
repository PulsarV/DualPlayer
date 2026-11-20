---
id: T35
title: "Guard the session's timer callbacks and start the clock from the outputs still connected"
layer: "app"
deps: ["T27"]
blocks: []
acs: ["AC-01", "AC-15b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Sync/PictureSync.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-20 (F21, F24)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T35 — Guard the session's timer callbacks and start the clock from the outputs still connected

## Why

F21 (CLAUDE.md Errors): the session's timer callbacks (`OnHealthTick`, `FreezePicture`, `PictureSync.FinishMove`) have no top-level try/catch with a log entry (`PlaybackSession.cs:204, 487`; `PictureSync.cs:200`).
F24 (stage-2): the session's clock start takes the largest lag over all estimated outputs; the engine uses only outputs still connected, so losing an output during pre-roll shifts the picture by up to ~200 ms (`PlaybackSession.cs:177-179`; `AudioEngine.cs:547`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-15b.

## Definition of Done

- [ ] Every timer/background callback in the session and picture sync catches, logs the exception at the top and keeps the session usable (if T24 routes them through a loop that already does this, a test proves it).
- [ ] The clock start uses the same lag the engine used (connected outputs only — exposed by the engine, not recomputed); a test loses an output during pre-roll and checks the picture start.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
