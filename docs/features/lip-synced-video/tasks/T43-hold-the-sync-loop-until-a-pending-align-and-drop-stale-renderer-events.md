---
id: T43
title: "Hold the sync loop until a pending align and drop stale renderer events"
layer: "app"
deps: ["T42"]
blocks: []
acs: ["AC-04", "AC-06b", "AC-07"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Sync/PictureSync.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21 (R6, R7)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T43 — Hold the sync loop until a pending align and drop stale renderer events

## Why

R6 (stage-2, AC-04): with `_pendingAlign` set (resume before the freeze timer fired), state is Playing and the clock runs from the freeze point while the picture still runs ~`dt` ahead. `OnSyncTick` (`PlaybackSession.cs:993-1005`) calls `sync.Tick()` anyway; if `dt` > 100 ms `Tick` (`PictureSync.cs:163-168`) seeks the picture back by `dt`, and when the freeze timer fires `FreezePicture` jumps it forward — frames repeat then skip. Happens for Space twice 100 ms … one picture delay apart (BT default 200 ms). For smaller `dt` the same tick can publish `LipSyncedAgain("resume")` (`:1025-1029`) before the picture is aligned. The existing test uses `Render(2)` and never crosses the seek threshold.

R7 (stage-2, AC-06b/AC-07): renderer events are posted to the session loop but carry no run/file token (engine events do, `_runToken`). The old film keeps playing during `ChangeFile`'s folder scan/probes; a `DecodeFailed` raised by it is queued behind the command and runs after the new film started, ending it with `picture-failed` (`:1089-1107`). The `_starting`/`_failureDuringStart` path (`:116-117, 248-270`) is dead code now that events are serialized.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04, AC-06b, AC-07.

## Definition of Done

- [x] While `_pendingAlign` is set, `OnSyncTick` skips `Tick`, resync and late-frame bookkeeping; `LipSyncedAgain("resume")` only after the align.
- [x] Renderer events are stamped with the run token at post time; stale ones are dropped in `OnPictureFailed` (and other renderer handlers).
- [x] The dead `_starting`/`_failureDuringStart` handling is removed.
- [x] Tests: resume 150 ms after a pause with a 200 ms picture delay → the shown picture position never goes below the position at the resume (no repeat/skip); a `DecodeFailed` from the old film raised during a file change does not end the new film.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

Renderer events carry a separate `_filmToken` (bumped in `ChangeFile` right after the stop), not `_runToken`, so a real failure of the new film during its own start is still handled. `PictureSync.AlignAt` with a positive delay now sets `_holdUntil` to the clock start, so a frame held for a start still ahead is not measured or seeked back (affects start, resume and seek; all existing tests pass) — worth a re-review glance. Gate: App.Tests showed intermittent `Call from invalid thread` (Avalonia dispatcher) in `OsdNoteTokensTests` / `CompositionRootSmokeTests`, unrelated to T43; 4 consecutive reruns 96/96. Folded into T46.
