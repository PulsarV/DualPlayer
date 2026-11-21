---
id: T49
title: "Let a return into the film retry every device after all outputs are lost and reload the picture after a picture failure"
layer: "app"
deps: ["T48"]
blocks: []
acs: ["AC-05", "AC-06b", "AC-15b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/", "docs/features/lip-synced-video/tasks/T40-keep-a-usable-ended-session-when-a-file-change-or-a-return-into-the-film-fails.md"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21b (Q3, Q4)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T49 — Let a return into the film retry every device after all outputs are lost and reload the picture after a picture failure

## Why

Q3: `ReturnIntoFilm` always drops devices in `_lost` (`PlaybackSession.cs:296-300`), so after AC-15b ← answers "Every output has been lost" even when the headset is back, while Page Down retries every device. This contradicts `screens.md:77` (backward seek still works after the last output is lost) and the ticked T40 DoD item.

Q4: after `picture-failed` (`EndOnPictureFailure`, `PlaybackSession.cs:1217-1243`) the film-loaded state stays true while mpv no longer holds a file (`MpvVideoRenderer.cs:880-904`), so ← (`:873`) starts sound with no picture.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-06b, AC-15b.

## Definition of Done

- [ ] When every output is lost, a return into the film resolves `_request.Outputs` again against the current device list (as `ChangeFile` does with `keepLost: false`) and plays on every device that is back; if none is back it fails to Ended with the note and a log entry.
- [ ] A `DecodeFailed` clears the film-loaded/started state; `ReturnIntoFilm` then reloads the file paused before starting audio, and if the reload fails it goes back to Ended with a note and a log entry — no audio without a picture.
- [ ] T40's DoD box and its note are corrected to match the behaviour.
- [ ] Tests: last output lost, the device comes back, then ← plays from the end position on it; picture failure, then ← reloads the picture before any engine run (or ends with a note when the reload fails).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
