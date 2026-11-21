---
id: T74
title: "Log the speeds a picture-delay move really applies"
layer: "app"
deps: ["T73"]
blocks: []
acs: ["AC-14", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Sync/DriftController.cs", "src/DualPlayer.Core/Sync/OutputSync.cs", "src/DualPlayer.Core/Sync/PictureSync.cs", "src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md", "tests/"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21d (R4)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T74 — Log the speeds a picture-delay move really applies

## Why

R4 (rest of S12): `PictureDelayMoved` speeds are recomputed from the session's own inputs — `PicturePpm` from a duration defined as 2000 ppm for the largest change, `RampPpm` clamped to ±2000 (`PlaybackSession.cs:240-247, 268-283, 409-411`). T23 L3 "|PicturePpm|, |RampPpm| ≤ 2000" (`t23-acceptance-run-guide.md:64`) therefore cannot fail, and the total output speed (drift correction + ramp) is still not logged (`DriftController.cs:24, 27`; `OutputSync.cs:69`; `AudioEngine.cs:989, 1044`; mpv `speed` in `MpvVideoRenderer.cs:400-415`). `PicturePpm` is positive when the delay grows (picture slower), the opposite of cli.md's "picture speed change" (`PlaybackSession.cs:774-776`; `PictureSync.cs:338`; `cli.md:100-101`). `FromMs` is the previous estimate, not the in-flight delay, so a press that retargets a running move logs From/To/Duration that do not match.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-14, AC-17.

## Definition of Done

- [ ] Each output's applied ramp ppm and total speed (drift correction + ramp) come from the engine/drift controller as applied, without a clamp in the logging path, and are logged (engine trace next to `correction=…ppm` and/or in `PictureDelayMoved`).
- [ ] The speed actually set on mpv during the move is logged (from the renderer's applied value).
- [ ] cli.md §3 states the sign of each speed field (e.g. positive = faster than real time) and the code matches it.
- [ ] `FromMs` is taken from the in-flight picture delay (`sync.PictureDelay`); a retargeted move logs consistent From/To/Duration.
- [ ] T23 L3 in the run guide judges on the applied speeds.
- [ ] Unit tests: a move logs speeds equal to what the fake engine/renderer applied (a ramp of 2500 ppm would show 2500); sign test; retarget test.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
