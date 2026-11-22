---
id: T100
title: "Make the interrupt and neighbour-memory tests independent of wall-clock and thread-pool timing"
layer: "test"
deps: ["T96", "T97"]
blocks: []
acs: ["AC-07", "AC-08", "AC-09b"]
files_hint: ["tests/DualPlayer.Media.Tests/AbandonedWorkerTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22c (U10, U11)"
---

<!-- Follow-up task from review-2026-11-22c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22c.md) · repo `CLAUDE.md`. Do not guess. -->

# T100 — Make the interrupt and neighbour-memory tests independent of wall-clock and thread-pool timing

## Why

Two test-timing problems.

- **U10.** `AbandonedWorkerTests` (`tests/DualPlayer.Media.Tests/AbandonedWorkerTests.cs:65`) compares Stopwatch time to a `Task.Wait(500 ms)` that counts with `Environment.TickCount`. On windows-latest, where this test now runs, the wait can end a few ms early, so it fails at random.
- **U11.** The T88 tests depend on thread-pool timing against a free-running fake clock (`PlaybackSessionNeighbourMemoryTests.cs:42-51, 89-108, 118-130`; `Fakes/EngineRig.cs:174-179`). E2's `Task.Run` listing must finish while `Call` holds fake time; if the pool starts it about 300 ms late under parallel load, the probe is cut and three tests fail (the same class as S12).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08, AC-09b.

## Definition of Done

- [ ] `AbandonedWorkerTests` no longer compares Stopwatch time against a TickCount-based wait without slack: it allows a slack (e.g. `limit - 50 ms`) or proves "still reading" another way (e.g. `ReaderClosed` not set just before the limit).
- [ ] The neighbour-memory tests (including any T97 added) do not depend on when the thread pool starts the listing worker: the scripted decoder and probe advance the fake clock themselves, and the command runs without free-running time (or time advance is gated until the worker has run).
- [ ] The Core and Media test projects pass 10 runs in a row under load (e.g. three full-solution runs in parallel) with no failure in these tests.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
