---
id: T76
title: "Show the picture-failed note on the last good frame"
layer: "integration"
deps: ["T75"]
blocks: []
acs: ["AC-06b", "AC-06"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/IVideoRenderer.cs", "src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "tests/DualPlayer.Core.Tests/", "tests/DualPlayer.Media.Tests/Video/", "tests/Shared/", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21d (R2)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T76 — Show the picture-failed note on the last good frame

## Why

R2 (rest of S13): on an error end-file (`DecodeFailed`) mpv unloads the file — the session itself says "mpv holds no file" (`PlaybackSession.cs:323`). `force-window=yes` (`MpvVideoRenderer.cs:233-235`) keeps an empty window, not the last good frame, so the picture-failed note is likely drawn on black; SCR-02 and sad.md require the last good frame (`screens.md:76, 93`; `sad.md:436, 541`). The session reloads nothing on failure (`PlaybackSession.cs:1399-1425`). The macOS-only hardware test (`MpvVideoRendererTests.cs:551-584`) accepts a plain end of file, where `keep-open` already holds the frame, and asserts only the option read-back and `vo-configured`. Owner decision: on `DecodeFailed`, reload the file paused at the last presented position, then show the note.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-06.

## Definition of Done

- [x] On `DecodeFailed` the session reloads the file paused at the last presented position, then shows the picture-failed note (fake renderer test: order Load(paused, position) → ShowNote; audio stays stopped per AC-06b).
- [x] If that reload itself fails, the note is still shown (on the empty window) and nothing throws.
- [ ] The hardware test uses a sample that really raises an error end-file (or drives `DecodeFailed` via a corrupted stream) and asserts a frame is shown (`video-params` non-null) after the note; it fails without the reload.
- [x] The T23 checklist item for a truncated file checks the last good frame.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
