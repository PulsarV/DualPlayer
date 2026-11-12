---
id: T35
title: "Record the clamped seek-past-end jump so the played duration stays true"
layer: "app"
deps: ["T34"]
blocks: []
acs: ["AC-10", "AC-13"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Diagnostics/RunSummaryBuilder.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineTransportTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r2 (R2)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T35 — Record the clamped seek-past-end jump so the played duration stays true

## Why

R2 (review-2026-11-12-r2): `RecordClockJump(target - preSeek)` uses the unclamped target; the clock then jumps back to `end` with no jump recorded, so played = `preSeek - (target - end)` — negative, printed without sign (a 3 s run shows `02:36:37.0`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-10, AC-13.

## Definition of Done

- [ ] The jump recorded for a seek is to the final clock position (clamped `end` on a past-end seek), or the second jump `end - target` is recorded before `JumpTo(end)`.
- [ ] Past-end tests assert `Played ≈ preSeek` (±100 ms) for every output, for both the duration-known and heard-before branches.
- [ ] The summary printer never prints a negative duration silently (a negative value is a bug: assert or clamp + log).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
