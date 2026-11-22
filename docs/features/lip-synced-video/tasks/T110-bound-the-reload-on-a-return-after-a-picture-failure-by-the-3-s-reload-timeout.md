---
id: T110
title: "Bound the reload on a return after a picture failure by the 3 s reload timeout"
layer: "app"
deps: ["T109"]
blocks: []
acs: ["AC-05", "AC-06b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPictureFailureReloadTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22e (W4)"
---

<!-- Follow-up task from review-2026-11-22e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22e.md) · repo `CLAUDE.md`. Do not guess. -->

# T110 — Bound the reload on a return after a picture failure by the 3 s reload timeout

## Why

- **W4** (checked in code). The return into the film after a picture failure calls `_renderer.Load(request.File, nint.Zero, startPaused: true)` with no timeout (`PlaybackSession.cs:354`), so `MpvVideoRenderer` waits up to its 15 s default (`MpvVideoRenderer.cs:65, 247`). After a reload that ended `timed-out` (a stalling NAS) the viewer presses ←: the session thread blocks up to 15 s, and every queued key waits behind it, Alt+F4 included. The failure path is bounded by `ReloadTimeout` (3 s, `:118`, used at `:1776`) for exactly this reason. spec §6 recovery row: a return gets the 3 s start budget.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-06b.

## Definition of Done

- [ ] The return's `Load` passes `ReloadTimeout` (3 s); a `Load` that times out ends the return the way a failed return load already does.
- [ ] Test: on the return after a picture failure the fake renderer's `LoadTimeouts.Last()` is 3 s; fails if the timeout is dropped.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
