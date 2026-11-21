---
id: T86
title: "Make the mpv speed reply update the set speed under the renderer's lock"
layer: "integration"
deps: []
blocks: []
acs: ["AC-14", "AC-17"]
files_hint: ["src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "tests/DualPlayer.Media.Tests/Video/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22 (P10)"
---

<!-- Follow-up task from review-2026-11-22. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22.md) · repo `CLAUDE.md`. Do not guess. -->

# T86 — Make the mpv speed reply update the set speed under the renderer's lock

## Why

P10: `OnSpeedReply` (`MpvVideoRenderer.cs:1017-1020`) checks `id == _speedIds` and writes `_setSpeed = _confirmedSpeed` without `_gate`, while `SetSpeed` (`:410-428`) writes under it. A `SetSpeed` landing between the check and the write is overwritten, so `PictureDelayMoved.PicturePpm` logs the older speed until the next change. Happens only when mpv refuses a speed.

Owner decision: do the error-branch check and write under `_gate` (or `Interlocked.CompareExchange` keyed on the request id).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-14, AC-17.

## Definition of Done

- [x] The speed-reply error branch checks the request id and writes `_setSpeed` atomically with respect to `SetSpeed` (under `_gate` or CAS keyed on the id).
- [x] A unit-level test (via the existing reply-handling seam, or a deterministic interleaving hook) shows a newer `SetSpeed` is not overwritten by a stale refused reply; if no seam exists without native mpv, the reasoning is stated in the task and covered by a `[HardwareFact]`.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
