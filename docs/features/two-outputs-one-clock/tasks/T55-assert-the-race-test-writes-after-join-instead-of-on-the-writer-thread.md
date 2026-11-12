---
id: T55
title: "Assert the race test writes after Join instead of on the writer thread"
layer: "test"
deps: []
blocks: []
acs: ["AC-07"]
files_hint: ["tests/DualPlayer.Core.Tests/Outputs/AudioRingBufferReclaimRaceTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r4 (T8)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 4. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r4.md) · repo `CLAUDE.md`. Do not guess. -->

# T55 — Assert the race test writes after Join instead of on the writer thread

## Why

T8: `ring.Write(...).ShouldBe(Piece)` runs on a raw `Thread` (`AudioRingBufferReclaimRaceTests.cs:54`); a failure would throw an unhandled exception and crash the test host instead of failing the test.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07.

## Definition of Done

- [ ] The writer thread counts short writes (and any exception) in locals; the test asserts them after `Join`, like `violations`.
- [ ] The test still fails on the pre-T44 ring behaviour (no weakening) and still finishes in about 0.5 s.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
