---
id: T132
title: "Hold the grace and listing-budget fakes on real-time gates past the session's deadline, and count polls instead of wall time"
layer: "test"
deps: []
blocks: ["T133", "T135", "T23"]
acs: ["AC-08", "AC-09b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourBudgetTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Fakes/CountingTimeProvider.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-23c (W1, W2, W4)"
---

<!-- Follow-up task from review-2026-11-23c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23c.md) · repo `CLAUDE.md`. Do not guess. -->

# T132 — Hold the grace and listing-budget fakes on real-time gates past the session's deadline, and count polls instead of wall time

## Why

- **W1.** During the grace the wind-down fakes register their own +300 ms / +20 ms deadline, and the rig moves fake time straight to it, past the session's `graceEnd` (+50 ms). The result depends on when the session's 1 ms waits wake: the SlowPastItsLimit "outlasts" test failed 1 of 120 runs on macOS and 32 of 32 with the session's waits set to 16 ms (the Windows default timer). The wrapper "inside" test still passes with a 5–10 ms grace, so it does not pin the lower side; only the adapter twin does. Cites: `PlaybackSessionNeighbourMemoryTests.cs:287-324, 343-362, 498, 526-529`; `Fakes/EngineRig.cs:202-210`; `PlaybackSession.cs:811-829, 839-849`.
- **W2.** `PlaybackSessionNeighbourRecoveryTests.cs:365-369` asserts the give-up takes < 300 ms of wall time. On a frozen fake clock the grace is `MaxBusyPolls` (100) × `Thread.Sleep(1)`; at the Windows default 15.6 ms timer that is about 1.5 s. The comment "about 100-150 ms" (`PlaybackSession.cs:62-63`) holds only on macOS.
- **W4.** `PlaybackSessionNeighbourBudgetTests.cs:137-147` (`A_track_listing_that_blocks_past_the_budget…`) failed 5 of 36 and 6 of 57 runs (3 processes in parallel) at 3.3–3.7 s against a 3.25 s bound. `BeforeList` spins toward a +10 s deadline with no cancel, so the rig moves fake time past the 3 s budget while the session thread sleeps (same mechanism as Z4).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [ ] Both "outlasts the grace" fakes (wrapper SlowPastItsLimit and the adapter twin) spin fake time only to about grace + 10 ms (registered with the rig as their end), then hold the worker on a real-time gate the test releases after the second press; they keep failing if the grace is raised past their wind-down and pass regardless of how late the session thread wakes (check with the session's 1 ms waits temporarily set to 16 ms on a scratch copy: 0 failures).
- [ ] The wrapper "inside the grace" test pins the lower side: it fails with the grace set to 10 ms (and to 0), e.g. the 20 ms wind-down ends only after the session has read the clock at or after that instant, or the second press runs while the first press's worker is still kept.
- [ ] The bounded give-up test counts the fake's `IsBusy` calls and asserts ≤ `MaxBusyPolls` + a few instead of wall time; the comment at `PlaybackSession.cs:62-63` is OS-neutral (polls, not milliseconds).
- [ ] The listing fake in `A_track_listing_that_blocks_past_the_budget…` spins fake time to budget + slack (registered as its deadline), then waits on a real-time gate the test releases.
- [ ] The neighbour and rig tests (`--filter "FullyQualifiedName~Neighbour|FullyQualifiedName~RigLoops"`) pass 20 runs with 3 processes in parallel, each process using its own build output.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
