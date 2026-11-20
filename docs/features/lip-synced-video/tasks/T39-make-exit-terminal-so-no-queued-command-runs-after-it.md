---
id: T39
title: "Make exit terminal so no queued command runs after it"
layer: "app"
deps: []
blocks: []
acs: ["AC-06"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.App/ViewModels/PlaybackControls.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/ViewModels/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21 (R4)"
status: "todo"
step: "—"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T39 — Make exit terminal so no queued command runs after it

## Why

R4 (stage-2, AC-06, T34 DoD): `ExitCore` only sets Ended (`PlaybackSession.cs:1194-1212`); `_request` stays and no command checks for an exited session. Commands queued behind Esc, or still queued when Alt+F4 calls `ExitNow` (UI thread, skips the queue), still run after `ExitCore`. A queued ← at the end runs `ReturnIntoFilm` → `StartRun` → `_engine.StartRun`, so audio plays with no window until process exit; a queued Page Down scans the folder and probes; an offset key still runs. `PlaybackControls.cs:31-57` keeps posting after `_exited`.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06.

## Definition of Done

- [ ] Exit is terminal: an `_exited` flag set by `ExitCore`, checked first by every session command (returns `playback.no_run`, no engine/renderer/folder call).
- [ ] `PlaybackControls` drops posts once exited.
- [ ] Tests: Exit then Seek(-10 s) at the end starts no engine run and publishes no `AudioStarted`; Exit then NextFile scans nothing; a key posted after Alt+F4 `ExitNow` does not run.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
