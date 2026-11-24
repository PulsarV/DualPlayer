---
id: T89
title: "Mark outputs lost mid-film as lost in every picture-delay record"
layer: "app"
deps: ["T88"]
blocks: []
acs: ["AC-17", "AC-15"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/Startup/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22b (S2)"
---

<!-- Follow-up task from review-2026-11-22b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22b.md) · repo `CLAUDE.md`. Do not guess. -->

# T89 — Mark outputs lost mid-film as lost in every picture-delay record

## Why

**S2** (all three reviewers). T82 marks lost outputs only in `StartCore`. After a loss mid-film, `OnDeviceLost` stores `Merge(estimate, updated)` (`PlaybackSession.cs:1749`), and `Merge` keeps the lost output's old entry with `IsLost` false (`:924-925`). So every later `PictureDelayChanged` (pause, resume, seek, return; `PublishDelay` at `:1242-1246`) logs it as e.g. `Output=1 … AssumedMs=200 LagMs=0 PictureDelayMs=0`, which breaks cli.md §1.1 (lag = picture delay − assumed) and §3 ("a lost output: `Lost=true` only") and feeds stale numbers into the AC-16 pairing. The DeviceLost and Offset records (`:1784`, `:828`) leave the lost output out entirely.

Owner decision: fix now — in `Merge`, mark outputs missing from the playing set `IsLost = true`.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17, AC-15.

## Definition of Done

- [x] `Merge` (or `OnDeviceLost`) marks every output in `_lost` `IsLost = true`, so every `PictureDelayChanged` after a mid-film loss carries it as lost and the log writes `Lost=true` with no `AssumedMs`/`LagMs`.
- [x] Test: lose output 1 mid-film, press Space (and a seek), assert `PictureDelayChanged(...).Outputs[0].IsLost` and the log line `Lost=true`; the playing output's lag equals picture delay − assumed.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
