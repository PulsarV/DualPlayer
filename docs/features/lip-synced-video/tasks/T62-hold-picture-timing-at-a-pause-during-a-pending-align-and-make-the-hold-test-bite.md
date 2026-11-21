---
id: T62
title: "Hold picture timing at a pause during a pending align and make the hold test bite"
layer: "app"
deps: ["T61"]
blocks: []
acs: ["AC-04", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionRecoveryBudgetTests.cs"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21c (S3, S5)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T62 — Hold picture timing at a pause during a pending align and make the hold test bite

## Why

S3 (rest of Q6): `PauseCore` measures `sync.Measure()` (`PlaybackSession.cs:802`) before it clears `_pendingAlign` (`:819`) and publishes the result directly (`:832-835`), skipping the hold `ReportTiming` has. Space, Space, Space within the picture delay logs `PictureTimingReported` with an error of ~+150 ms while the picture is in step; T23 L2 reads these records.

S5: the T50 hold test (`PlaybackSessionRecoveryBudgetTests.cs:62-77`) does `foreach (var t in timings)`, which passes when nothing is published.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04, AC-17.

## Definition of Done

- [ ] `PauseCore` skips the at-key measurement (or routes it through the `ReportTiming` hold) when `_pendingAlign` is set.
- [ ] Test: pause → resume → pause within the picture delay publishes no `PictureTimingReported` with |error| > 20 ms (fails on the current code).
- [ ] The T50 hold test renders past the picture delay and asserts at least one `PictureTimingReported` after the align and that all of them are within 20 ms.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
