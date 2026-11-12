---
id: T43
title: "Make Core test rigs fail instead of spinning and bound the stop wait in real time"
layer: "test"
deps: []
blocks: []
acs: ["AC-08"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/AudioEngineTransportTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r3 (S2)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T43 — Make Core test rigs fail instead of spinning and bound the stop wait in real time

## Why

S2 (review-2026-11-12-r3): `Fill` loops in the transport rig and `EngineRig` check `IsClosed` once then pump a device the engine may close (spin at 100% CPU); `EngineRig.WaitForDecoder` waits 10 s real time per step and never fails; `WaitForDevicesToPassStop` has a fake-time-only deadline, so a test that stops rendering waits forever. Four orphan testhosts came from these.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08.

## Definition of Done

- [ ] Fill loops break when a pump makes no progress (as `ConsoleRig` after T39).
- [ ] `WaitForDecoder` fails the test on timeout.
- [ ] `WaitForDevicesToPassStop` is also bounded in real time (a device that stops rendering cannot hold `Stop` forever), with a test.
- [ ] Guard tests: closing/losing a device mid-fill returns; Core.Tests run 5× without a hang.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
