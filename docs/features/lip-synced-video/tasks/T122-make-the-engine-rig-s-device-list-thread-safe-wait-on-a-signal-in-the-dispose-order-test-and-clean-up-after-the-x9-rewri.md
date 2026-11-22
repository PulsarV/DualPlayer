---
id: T122
title: "Make the engine rig's device list thread-safe, wait on a signal in the dispose-order test, and clean up after the X9 rewrite"
layer: "test"
deps: []
blocks: []
acs: ["AC-09b"]
files_hint: ["tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Fakes/RigLoopsTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23 (Y4, Y5, Y10)"
---

<!-- Follow-up task from review-2026-11-23. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23.md) · repo `CLAUDE.md`. Do not guess. -->

# T122 — Make the engine rig's device list thread-safe, wait on a signal in the dispose-order test, and clean up after the X9 rewrite

## Why

- **Y4**. `EngineRig._devices` is enumerated in `Fill` on the test thread (`tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs:419-431`) while `CapturingBackend.Open` clears and adds to it on the engine loop during a file change (`:478-483`). "Collection was modified" was seen in 3 of 22 parallel runs (the X13 test `PlaybackSessionNeighbourMemoryTests.cs:246`, the X10 test `:263`, and App `PlayEndToEndTests.Page_Down_prints_the_next_file_line…`).
- **Y5**. `RigLoopsTests.cs:142-149` assumes after `Thread.Sleep(500)` that the session has reached `_engine.StartRun`. On a slow CI runner `StartRun` can throw `ObjectDisposedException` on the raw `stuck` thread, where nothing catches it, ending the whole Core test process.
- **Y10** (code part). `PlaybackSessionNeighbourRecoveryTests.cs:283`'s name describes a late pool start the test no longer simulates; `:5` has an unused `using Microsoft.Extensions.Time.Testing;`; `AfterListing` (`:317, :398`) is never assigned.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b.

## Definition of Done

- [ ] `_devices` is guarded by the rig's lock (or copy-on-write): the Add/Clear in `CapturingBackend.Open` and every enumeration (`Fill`, `PaceToDecoder`, `WaitForDecoder`, `Devices`) work on a consistent snapshot. A rig self-test enumerates while another thread re-opens the outputs and does not throw.
- [ ] The dispose-order test waits on a deterministic signal that the session has reached the engine start (e.g. the engine start waiting on fake time) instead of `Thread.Sleep(500)`, and the `stuck` thread body catches and records any exception, so a failure fails the test rather than the process.
- [ ] The X9 test is renamed (e.g. `The_probe_wrapper_runs_off_the_shared_thread_pool_and_the_chosen_file_starts_AC_09b`); the unused `using` and the dead `AfterListing` member are removed.
- [ ] The neighbour, rig and App end-to-end tests (`--filter "FullyQualifiedName~Neighbour|FullyQualifiedName~RigLoops"` in Core, `FullyQualifiedName~PlayEndToEnd` in App) pass 10 parallel runs with no "Collection was modified" and no host crash (use `--blame-hang-timeout 120s`).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
