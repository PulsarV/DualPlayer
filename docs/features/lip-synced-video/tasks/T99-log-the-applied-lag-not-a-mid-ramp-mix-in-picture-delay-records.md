---
id: T99
title: "Log the applied lag, not a mid-ramp mix, in picture-delay records"
layer: "app"
deps: ["T98"]
blocks: []
acs: ["AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22c (U7)"
---

<!-- Follow-up task from review-2026-11-22c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22c.md) · repo `CLAUDE.md`. Do not guess. -->

# T99 — Log the applied lag, not a mid-ramp mix, in picture-delay records

## Why

**U7.** Pause publishes the in-flight `sync.PictureDelay` together with the new `AssumedMs` (`PlaybackSession.cs:1045, 1071, 1304-1310`). Example: +50 ms on the slowest output and Space 5 s into a 25 s ramp logs `PictureDelayMs=210` and `LagMs=-40`, while the engine applies 0. The error is up to 500 ms. The T89 test (`tests/DualPlayer.Core.Tests/Playback/PlaybackSessionDeviceLossTests.cs:62`) re-asserts the code's own formula, so it cannot catch this.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17.

## Definition of Done

- [x] Every `PictureDelayChanged` publishes, for each playing output, a lag equal to the lag the engine actually applies at that moment (e.g. `_lags.CurrentLag(deviceId)`), and `AssumedMs` consistent with the published picture delay and that lag; or Pause publishes the target delay and target lags as Resume and Seek do — whichever keeps cli.md §1.1 (lag = picture delay − assumed) true.
- [x] Test: change an offset, press Space mid-ramp, and assert each published lag equals the engine's applied lag (read from the fake engine/rig, not recomputed by the test's own formula) and is never negative.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
