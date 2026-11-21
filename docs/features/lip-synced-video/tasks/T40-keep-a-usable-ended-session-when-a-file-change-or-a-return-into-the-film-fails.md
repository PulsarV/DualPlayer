---
id: T40
title: "Keep a usable ended session when a file change or a return into the film fails"
layer: "app"
deps: ["T39"]
blocks: []
acs: ["AC-05", "AC-07", "AC-15b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21 (R2, R3)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T40 — Keep a usable ended session when a file change or a return into the film fails

## Why

R2 (stage-1, AC-05/AC-07): `StartCore` sets `_request` to the target (`PlaybackSession.cs:233`) before `StartRun` calls `_renderer.Load` (`:303-312`). If `Load` fails, `ChangeFile` sets Ended (`:478-493`) with `_request` naming a file that never loaded; ← then runs `ReturnIntoFilm` → `StartRun(returning: true)` which skips `Load`, so that file's audio plays with no picture. `Load` fails before `Interlocked.Increment(ref _runToken)`, so the old run's `RunSummary(FileChange)` still matches the token and `OnEngineEvent` (`:1235-1257`) overwrites `_endPosition` with the old film's position.

R3 (stage-1, AC-15b/AC-05): `SeekCore` sets `_state = Idle` before `ReturnIntoFilm()` (`:833-840`). When that fails (e.g. after AC-15b every device is in `_lost` → `outputs.all_lost`, `:293-297`) the state stays Idle and `ChangeFile` and every other key return `playback.no_run` silently; only Esc works — breaking "the next-file, previous-file and exit keys still work".

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-07, AC-15b.

## Definition of Done

- [x] After a failed file change the session knows which film the renderer holds: `_request`/`_estimate` are restored to the loaded film (or a 'no film loaded' state makes seeks a no-op while next/previous/Esc still work); ← never starts audio for a file that did not load.
- [x] The run token is bumped before `_engine.Stop(FileChange)` so a stale summary cannot overwrite `_endPosition`.
- [x] A failed `ReturnIntoFilm`/`StartRun` returns the session to Ended with a note and a log entry (as `ChangeFile` does); when all outputs are lost, a return/file change retries every device (return: corrected by T49).
- [x] Tests: Load fails on the next file, then Seek(-10 s) starts no engine run on that file; last output lost, then ←, then Page Down plays the next file; a stale RunSummary does not move `_endPosition`.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

"No film loaded" option chosen: `_filmLoaded` is false from before `Load` until it succeeds; a backward seek at the end with no loaded film is a no-op. The run token is bumped right after the synchronous `_engine.Stop(FileChange)` (the engine stamps the summary during the stop), so the old summary is always stale. Retrying every device applies to a file change (already via `keepLost: false`); a backward return after all outputs are lost also retries every device (T49); if none is back it fails to Ended with a note and a log entry.
