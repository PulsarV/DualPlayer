---
id: T85
title: "Time recovery from the key's arrival and judge outputs on readings after the engine command"
layer: "app"
deps: ["T84"]
blocks: []
acs: ["AC-04", "AC-05", "AC-17"]
files_hint: ["src/DualPlayer.App/ViewModels/PlaybackControls.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/", "src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22 (P8, P9)"
---

<!-- Follow-up task from review-2026-11-22. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22.md) · repo `CLAUDE.md`. Do not guess. -->

# T85 — Time recovery from the key's arrival and judge outputs on readings after the engine command

## Why

P9: keys wait in `CommandQueue` (`PlaybackControls.cs:33, 35, 62-63`) and `commandAt` is taken inside the command (`PlaybackSession.cs:473, 953, 1016`). A key pressed while an earlier command runs (e.g. a 3 s Page Down scan) leaves its queue wait out of `AfterCommandMs` and the give-up budget, though cli.md:106, 108 say "ms since the key press"; T23 L2 can pass a key that took more than 1 s from the press.

P8: T71 moved `_resync.At` to the key press, but the same `At` is also the freshness cut-off for output readings (`reading.Timestamp < commandAt`, `PlaybackSession.cs:1270-1271, 1381`), and the engine never resets `LatestErrorAt` on a seek (`AudioEngine.cs:983-984`). A reading taken after the key but before the engine started the seek passes as fresh, so `LipSyncedAgain` can judge the outputs on audio from before the seek.

Owner decision: stamp `TimeProvider.GetTimestamp()` in `PlaybackControls.Post` when the key arrives and pass it through to `Seek`/`Resume`/`NextFile`/`PreviousFile` (as `StartRequest.CommandTimestamp` does for a start); keep `At` (key time) for elapsed time and add a second stamp taken after `_engine.Seek`/`_engine.Resume` returns for the reading filters.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04, AC-05, AC-17.

## Definition of Done

- [x] Every key command carries the timestamp of the key's arrival from `PlaybackControls`; the session uses it as `_resync.At`. Test: a seek queued behind a 2 s command reports `AfterCommandMs` ≥ the queue wait and gives up per its budget counted from the key.
- [x] Output readings count as fresh only when taken after the engine command returned (a second stamp); `At` stays the elapsed-time origin. Test: a reading stamped between key and engine seek does not let `LipSyncedAgain` fire; fails on the old filter.
- [x] cli.md §3 / spec §6 wording matches ("since the key press").
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
