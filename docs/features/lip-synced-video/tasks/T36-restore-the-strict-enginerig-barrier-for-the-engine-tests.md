---
id: T36
title: "Restore the strict EngineRig barrier for the engine tests"
layer: "test"
deps: []
blocks: []
acs: ["AC-01"]
files_hint: ["tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-20 (F22)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T36 — Restore the strict EngineRig barrier for the engine tests

## Why

F22 (stage-2): the shared `EngineRig.Barrier()` was loosened. It returns silently on FileEnd/SeekPastEnd, can leave a phantom zero-output run, and weakens the step-3 engine tests (`EngineRig.cs:109-111, 226-238`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [x] `Barrier()` is strict again for the step-3 engine tests (fails on an unexpected end); the session tests that need the lenient path use an explicit, separately named helper.
- [x] No phantom zero-output run; all step-3 and session tests still pass.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

The barrier is strict again by default (the step-3 form: a refused start, failing once the run has ended). A session rig sets `EndsAllowed`, where the barrier is a round trip through the engine's loop and starts no run. `WaitFor` and every render step also wait for the session to handle what the engine posted to it (since T24 the session handles engine events on its own thread): one session test (`Losing_the_last_output_ends_the_film_with_the_picture_frozen_and_a_note_AC_15b`) raced on that and failed once in the gate.
