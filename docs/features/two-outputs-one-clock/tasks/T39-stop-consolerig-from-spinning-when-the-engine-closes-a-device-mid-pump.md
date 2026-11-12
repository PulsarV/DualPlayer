---
id: T39
title: "Stop ConsoleRig from spinning when the engine closes a device mid-pump"
layer: "test"
deps: []
blocks: []
acs: ["AC-13"]
files_hint: ["tests/DualPlayer.Console.Tests/E2E/ConsoleRig.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r2 (R6)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T39 — Stop ConsoleRig from spinning when the engine closes a device mid-pump

## Why

R6 (review-2026-11-12-r2): `ConsoleRig.Advance` checks `IsClosed` once, then loops `while (WrittenFrames < PlayedFramesNow + 2*chunk) PumpRender(chunk)`. If the engine closes the device in between, `PumpRender` is a no-op, fake time does not move and the loop never ends; the 30 s deadline is only checked outside, so the test host hangs.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-13.

## Definition of Done

- [ ] The inner loop re-checks closed/lost or breaks when a pump makes no progress; the deadline covers the inner loop.
- [ ] A rig test that closes the device between the check and the pump finishes (fails or returns) instead of hanging.
- [ ] Console.Tests green in 10 repeated runs.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
