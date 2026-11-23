---
id: T130
title: "Announce the next or previous file when the target is chosen, before the old run stops"
layer: "app"
deps: ["T129"]
blocks: ["T131", "T23"]
acs: ["AC-07", "AC-08"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23b (Z2)"
---

<!-- Follow-up task from review-2026-11-23b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23b.md) · repo `CLAUDE.md`. Do not guess. -->

# T130 — Announce the next or previous file when the target is chosen, before the old run stops

## Why

- **Z2.** After T125, `chosenAt` (`PlaybackSession.cs:645`) and `FileChanged` (`:674`, printed as the `next` / `previous` line by `TerminalPrinter.cs:109-111`) are separated by `_engine.Stop`, which waits up to `StopTimeout` = 2 s (`AudioEngine.cs:1196-1208`). T23 L4 and the T23 DoD judge that line against the 3 s scan budget, so a healthy press can fail L4 (scan 2.7 s + BT drain 0.4 s). The comments at `:672` ("said as soon as the target is chosen") and `:529-530` are stale. Owner decision: publish `FileChanged` at `chosenAt`, before `CancelFreeze` / `_engine.Stop`; L4 stays as written.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08.

## Definition of Done

- [ ] `FileChanged` is published right after the target is chosen (at `chosenAt`), before `CancelFreeze` and `_engine.Stop`; the run summary and the other lines still read in a sensible order (check the App/Console printers and their tests).
- [ ] Test: the rig's stop takes non-zero fake time (e.g. 1 s) → `FileChanged` is published before the stop ends (its timestamp equals the choice, not the stop's end). Fails if reverted.
- [ ] The comments at `:529-530` and `:672` describe the order as it now is.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
