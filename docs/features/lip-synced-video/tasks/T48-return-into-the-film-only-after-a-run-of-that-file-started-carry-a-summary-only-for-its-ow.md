---
id: T48
title: "Return into the film only after a run of that file started, carry a summary only for its own file, and bump the film token before Load"
layer: "app"
deps: []
blocks: []
acs: ["AC-01", "AC-05", "AC-06", "AC-06b", "AC-07"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21b (Q1, Q2, Q7)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T48 — Return into the film only after a run of that file started, carry a summary only for its own file, and bump the film token before Load

## Why

Q1: `_filmLoaded` turns true when mpv loads the new file, before `_engine.StartRun` succeeds (`PlaybackSession.cs:340-356`). If the engine start fails (e.g. `outputs.device_access_denied`), the session ends with `_request` on the new file and ← starts it with `ContinuesPreviousRun = true`: the engine merges the old film's `_lastSummary` (`AudioEngine.cs:210-213, 1178`) into the new file's summary, and there is no `started` line and no `StartTimings`. The rarer `outputs.device_not_found` race (`:526-539`) leaves the old film loaded with `_endPosition = 0` after `next <target>` printed.

Q2: the T40 test "a stale RunSummary does not move `_endPosition`" (`PlaybackSessionEndedRecoveryTests.cs:68-80`) asserts over an empty `EngineSeekTargets` list; removing the token bump at `PlaybackSession.cs:505` fails no test.

Q7: the film token is bumped at `PlaybackSession.cs:506` but the old film keeps playing in mpv until `_renderer.Load` (`:340`), after device listing and latency probing; a `DecodeFailed` from the old film in that window carries the new token and ends the new film.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-05, AC-06, AC-06b, AC-07.

## Definition of Done

- [x] ← at Ended is a return only when a run of `_request.File` actually started (a flag set after `_engine.StartRun` succeeds, cleared on a file change); otherwise ← is a no-op (or a fresh start) and never carries another file's summary.
- [x] The engine carries `_lastSummary` over only when its file (or run id) matches the new run's file.
- [x] The film token is bumped just before `_renderer.Load` in the not-returning branch, so an old film's `DecodeFailed` raised during device listing/probing is stale.
- [x] `SessionRig` records `EngineOptions.StartPosition`; the stale-summary test uses a failure that leaves the old film loaded, asserts `ShouldNotBeEmpty` before `ShouldAllBe`, and fails if the token bump is removed.
- [x] Tests: engine start fails after `Load` on the next file, then ←, then Esc — no return run of the new file carries the old summary (exit summary played time < 1 s or no run); an old-film `DecodeFailed` posted between the file-change key and `Load` does not end the new film.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

"No return" option chosen: `_filmLoaded` is cleared when a file change begins and set only after `_engine.StartRun` succeeds, so ← at Ended after a failed start is a no-op. When a change fails before `Load` (`outputs.device_not_found`), the old film stays loaded and ← returns into it from the end position 0. The gate passed on reruns; the intermittent failures of `AudioEngineRunTests.Audio_started_is_published_once…` and `AudioEngineSyncRecordTests.An_injected_step_error…` already occur before this task (seen at `d4e23c3`) and are tracked by T59.
