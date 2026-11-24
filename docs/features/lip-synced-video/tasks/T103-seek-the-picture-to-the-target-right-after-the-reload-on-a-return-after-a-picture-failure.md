---
id: T103
title: "Seek the picture to the target right after the reload on a return after a picture failure"
layer: "app"
deps: ["T102"]
blocks: []
acs: ["AC-05", "AC-06b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPictureFailureReloadTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22d (V5)"
---

<!-- Follow-up task from review-2026-11-22d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22d.md) · repo `CLAUDE.md`. Do not guess. -->

# T103 — Seek the picture to the target right after the reload on a return after a picture failure

## Why

- **V5** (two reviewers; a regression from T98). After a picture failure `ReturnIntoFilm` always calls `Load(startPaused: true)` (`PlaybackSession.cs:349-359`), which shows frame 0, and the renderer is not seeked until `sync.AlignAt` (`:443`) after the engine releases its pre-roll. So after a good last-frame reload the viewer sees the last good frame, then the film's opening frame for the whole pre-roll (hundreds of ms on Bluetooth), then the target. The natural-end return guards against this (`PlaybackSessionTransportTests.cs:205-221`, T23 guide :77). The picture-failure return test (`PlaybackSessionPictureFailureReloadTests.cs:92-102`) only counts loads. The XML doc at `:327-329` still says the file is not loaded again.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-06b.

## Definition of Done

- [x] After the paused `Load` in `ReturnIntoFilm`, the renderer is seeked to the return target (as `ReloadLastFrame` does at `:1739`) before the engine run starts, so no frame before the target is presented.
- [x] Test: picture failure, good reload, ← → `LowestShown` (or the presented positions after the load) stays near the target, never 0; fails if the seek is removed.
- [x] The XML doc of `ReturnIntoFilm` says that after a picture failure the file is always loaded again, paused, and seeked to the target before any audio starts.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
