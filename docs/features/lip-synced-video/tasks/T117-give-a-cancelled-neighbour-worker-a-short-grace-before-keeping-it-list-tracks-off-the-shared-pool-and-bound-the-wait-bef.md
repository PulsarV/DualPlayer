---
id: T117
title: "Give a cancelled neighbour worker a short grace before keeping it, list tracks off the shared pool, and bound the wait before the probe worker starts"
layer: "app"
deps: ["T116"]
blocks: []
acs: ["AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourWorkerTimingTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22f (X10, X11, X12)"
---

<!-- Follow-up task from review-2026-11-22f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22f.md) · repo `CLAUDE.md`. Do not guess. -->

# T117 — Give a cancelled neighbour worker a short grace before keeping it, list tracks off the shared pool, and bound the wait before the probe worker starts

## Why

- **X10**. After the session gives up on a cut worker it cancels it and keeps it in `_workers` at once (`PlaybackSession.cs:818-826, 891-894`). If the next press is already queued (a quick double Page Down), it sees a live worker and steps past the healthy file as `skipped … unreadable` (`:784-791`), then plays the one after it. spec.md:249: a file cut only because earlier files spent the budget is neither remembered nor reported as skipped. The same race made `A_healthy_neighbour_whose_check_the_press_budget_cut_is_checked_again_and_plays_on_the_next_press_AC_08` (`PlaybackSessionNeighbourMemoryTests.cs:35-52`) fail once ("should be Episode-03.mkv but was Episode-04.mkv").
- **X11**. The track listing still runs with `Task.Run` on the shared pool (`:817`), though T109 moved the probe wrapper off it (`:860-870`). Abandoned listings kept by the step-past memory (one per dead file, blocked in an OS read) hold pool threads; after about ProcessorCount presses over a dead share the next listing waits for a pool thread while the budget runs from `scanAt`, so the first file can be cut unread and remembered as timed out, and the pool-based picture-sync and engine sync timers (`:494-496`, `AudioEngine.cs:512`) are delayed for the film still playing.
- **X12**. The give-up check runs only once the probe wrapper has set `entered` (`:871-884`). Until the wrapper thread runs its first line, the session thread (and every queued key, Alt+F4 included) loops with no backstop; a cut limit computed before the thread starts is counted from `entered`, so the press can overrun 3 s by the thread's start delay.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [x] After cancelling a listing or probe worker it gave up on, the session waits a short bounded grace (`ProbeTimingSlack`, fake-time-testable) for it to end before keeping it in `_workers`; a worker that ends inside the grace is not kept, so the next press checks that file normally. Test: a cut healthy neighbour whose fake honours cancellation, next press queued immediately → that press plays it, no `skipped` line. Fails if reverted.
- [x] The neighbour tests wait for `_workers` to drain between presses where a press relies on a worker having ended (no real-time sleeps); `A_healthy_neighbour_whose_check_the_press_budget_cut_is_checked_again_and_plays_on_the_next_press_AC_08` passes 20 runs in a row.
- [x] The track listing starts with `TaskCreationOptions.LongRunning` (off the shared pool), like the probe wrapper. Test: the listing fake asserts `!Thread.CurrentThread.IsThreadPoolThread`.
- [x] The probe wait also gives up when `elapsed(scanAt) >= NeighbourScanBudget + ProbeTimingSlack`, even if the wrapper has not entered yet, and `entered` is read once more after the loop when setting `probeStart`. Test: a wrapper whose start is held back past the budget → the press ends within budget + slack of fake time.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
