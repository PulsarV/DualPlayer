---
id: T90
title: "Load the file again on return when the reloaded last frame failed again"
layer: "app"
deps: ["T89"]
blocks: []
acs: ["AC-06b", "AC-05"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22b (S3)"
---

<!-- Follow-up task from review-2026-11-22b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22b.md) · repo `CLAUDE.md`. Do not guess. -->

# T90 — Load the file again on return when the reloaded last frame failed again

## Why

**S3** (checked in code). `MpvVideoRenderer.Load` reports success on FILE_LOADED, before any frame is decoded (`MpvVideoRenderer.cs:815-822`). If the reloaded file fails again — the AC-06b "cannot be decoded again" case — mpv raises an error end-file (`:917-919`), but `OnPictureFailed` drops the `DecodeFailed` because `_pictureFailed` is already set (`PlaybackSession.cs:1519-1526`), so `_lastFrameReloaded` stays true (`:1559-1563`). The next backward seek at the end then skips `Load` (`:342-355`) and starts audio against an idle mpv: sound with no picture.

Owner decision: fix now — a `DecodeFailed` for the current film while `_pictureFailed` is set clears `_lastFrameReloaded`.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-05.

## Definition of Done

- [x] A renderer `DecodeFailed` for the current film that arrives after the failure (while `_pictureFailed` is set) clears `_lastFrameReloaded`, so the return into the film loads the file again (or refuses without starting audio if that load fails).
- [x] Fake-renderer test: `DecodeFailed` raised after the reload's `Load` returned → the next backward seek calls `Load` again and no engine run starts before it succeeds.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
