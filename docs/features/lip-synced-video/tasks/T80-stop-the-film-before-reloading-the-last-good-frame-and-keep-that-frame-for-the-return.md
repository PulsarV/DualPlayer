---
id: T80
title: "Stop the film before reloading the last good frame and keep that frame for the return"
layer: "app"
deps: []
blocks: []
acs: ["AC-06b", "AC-05"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/IVideoRenderer.cs", "src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Media.Tests/Video/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22 (P1, P3)"
---

<!-- Follow-up task from review-2026-11-22. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22.md) · repo `CLAUDE.md`. Do not guess. -->

# T80 — Stop the film before reloading the last good frame and keep that frame for the return

## Why

P1 (rest of R2): on a picture failure the session calls `ReloadLastFrame` → `_renderer.Load` (blocking, up to `LoadTimeout` = 15 s, `MpvVideoRenderer.cs:65, 309`) **before** it publishes `PictureFailed` and before `_engine.Stop(RunEndReason.PictureFailed)` (`PlaybackSession.cs:1476-1485, 1491-1508`). While mpv reopens the file every output keeps playing with no picture, the session thread answers no keys (spec §6.1: no key unanswered > 3 s), and the run summary's end position (`:1634`) lands later than the failure position, so a later ← counts from that later point. The T76 test (`PlaybackSessionPictureHealthTests.cs:43-58`) checks neither the call order nor when the sound stops.

P3: after `ReloadLastFrame` mpv already holds the last good frame, but `ReturnIntoFilm` (`PlaybackSession.cs:302-334`) still runs an unconditional `Load` when `_pictureFailed`; a load restarts at position 0 paused (`MpvVideoRenderer.cs:844-888`), so frame 0 is shown for the whole pre-roll (~2 s with the MOTIF headset). Contradicts the method's doc comment and the T23 checklist item "без кадру 0".

Owner decision: stop the engine and publish `PictureFailed` first, then reload with a bounded wait (≤ 3 s), then show the note; remember whether the reload succeeded and, if so, skip the second reload on return.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-05.

## Definition of Done

- [x] On a picture failure the order is: engine stopped (`RunEndReason.PictureFailed`) and `PictureFailed` published → `Load(paused)` → `Seek(last presented position)` → `ShowNote`; a fake-renderer test records the call order and fails on the old order.
- [x] The run summary's end position equals the failure position (test with a reload that takes time on the fake clock).
- [x] The reload wait is bounded at ≤ 3 s; a reload that does not finish in time leaves the note on the empty window, nothing throws, and the session answers the next key within the budget (test).
- [x] When the reload succeeded, `ReturnIntoFilm` does not load the file again (only bumps `_filmToken`); when it failed, it still loads. Tests for both; the stale doc comment is fixed.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
