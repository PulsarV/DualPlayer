---
id: T44
title: "Make reclaiming a stalled ring exclusive so skipped frames are never counted twice"
layer: "app"
deps: ["T43"]
blocks: []
acs: ["AC-07", "AC-11"]
files_hint: ["src/DualPlayer.Core/Outputs/AudioRingBuffer.cs", "src/DualPlayer.Core/Playback/DecodePump.cs", "tests/DualPlayer.Core.Tests/Outputs/", "tests/DualPlayer.Core.Tests/Outputs/OutputRenderSourceTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12-r3 (S1, S8, S11)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T44 — Make reclaiming a stalled ring exclusive so skipped frames are never counted twice

## Why

S1, S8, S11 (review-2026-11-12-r3): S1: writer (`TryReclaimOldest`) and reader can both advance past the same frames; the version check only catches slot reuse; the floor frame carries the cumulative skip, so a racing read counts the whole stall twice — a permanent content-position error the engine then trusts (heap dump: `_skippedConsumed` 90,864 vs 45,941). S8: when reclaim returns false the pump still drops the whole block. S11: the stalled-ring unit test was loosened to `>= 1_536` and its comments describe drop-newest.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-11.

## Definition of Done

- [ ] Reclaim and read are mutually exclusive without the render path waiting: e.g. one owner flag (reader CAS on entry, returns 0 frames on contention; writer CAS in reclaim, returns false; the owner advances `_head`), or absolute content frame per slot — whichever keeps ADR-0002's real-time rules; remove the version/retry machinery if it becomes unnecessary.
- [ ] Two-thread stress unit test hammering reclaim against reads asserts `sample == framesRead + SkippedFramesConsumed` with 0 violations (fails on the current code).
- [ ] `DecodePump`: reclaim returning false re-checks free space instead of dropping the block.
- [ ] Stalled-ring test pins `FramesAvailable.ShouldBe(2_000)` and its comments describe keep-newest.
- [ ] T36/T37 engine tests stay green; hung-output recovery tests run 15× without a hang or failure.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
