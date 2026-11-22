---
id: T98
title: "Always reload on return after a picture failure, and log a reload timeout and a frame that fails again"
layer: "app"
deps: ["T97"]
blocks: []
acs: ["AC-06b", "AC-05"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/IVideoRenderer.cs", "src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/", "tests/DualPlayer.App.Tests/Startup/", "tests/DualPlayer.Media.Tests/Video/", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22c (U6, U8, U9)"
---

<!-- Follow-up task from review-2026-11-22c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22c.md) · repo `CLAUDE.md`. Do not guess. -->

# T98 — Always reload on return after a picture failure, and log a reload timeout and a frame that fails again

## Why

Three gaps in the picture-failure reload.

- **U6** (T90 race). The user presses ← while `EndOnPictureFailure` reloads, so the command waits in the queue. `Load` returns on FILE_LOADED (`_lastFrameReloaded = true`), and mpv's error end-file for the reloaded frame is queued behind the command. `ReturnIntoFilm` increments `_filmToken`, skips `Load` and starts the engine (`PlaybackSession.cs:310-319, 348-360`). The `DecodeFailed` is then dropped as stale (`:1587-1596`). The result is sound with no picture and no note.
- **U8.** The reload outcome tells `timed-out` from `failed` by elapsed time (`:1668-1672`). The renderer returns `mpv.load_failed` for both (`MpvVideoRenderer.cs:310-321`), and `ManualResetEventSlim.Wait` can return a tick early on Windows, so a real timeout can be logged `Outcome=failed`.
- **U9.** When the reloaded frame fails again, the second `DecodeFailed` only clears `_lastFrameReloaded` and logs nothing. The last record is `PictureReload Outcome=reloaded`, which the T23 guide (:80) reads as "the note sits on the last frame", but the window is empty.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-05.

## Definition of Done

- [ ] After a picture failure, `ReturnIntoFilm` always calls `Load` (one paused load) before starting the engine, whatever `_lastFrameReloaded` says; test: a `DecodeFailed` for the reloaded frame queued behind a ← → the return loads again and no engine run starts on an idle renderer.
- [ ] `MpvVideoRenderer.Load` returns a distinct `mpv.load_timeout` when its wait runs out (documented on `IVideoRenderer`); the session maps the reload outcome from the code (`mpv.load_timeout` → `timed-out`, other failures → `failed`), not from elapsed time; tests for both.
- [ ] A `DecodeFailed` for the reloaded frame publishes `PictureReloadFinished` with a new outcome `failed-again`, which the log writer prints as `PictureReload Outcome=failed-again`; test on the Core and log-writer sides.
- [ ] cli.md §3 lists the `failed-again` outcome; the T23 guide (:80) explains it.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
