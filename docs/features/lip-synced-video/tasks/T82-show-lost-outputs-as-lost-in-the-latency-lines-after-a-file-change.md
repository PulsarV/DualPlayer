---
id: T82
title: "Show lost outputs as lost in the latency lines after a file change"
layer: "app"
deps: ["T80"]
blocks: []
acs: ["AC-07", "AC-12", "AC-13", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Sync/", "src/DualPlayer.App/Startup/TerminalPrinter.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "docs/features/lip-synced-video/contracts/cli.md", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/Startup/"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22 (P4)"
---

<!-- Follow-up task from review-2026-11-22. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22.md) · repo `CLAUDE.md`. Do not guess. -->

# T82 — Show lost outputs as lost in the latency lines after a file change

## Why

P4: when outputs stay lost across a file change, `StartCore` publishes `LatencyEstimated` (`PlaybackSession.cs:268-277`) and `PictureDelayChanged(file-change)` (`:608`) with the lost outputs still in them at their old assumed/lag, while the picture delay is worked out from the playing outputs only. Example: Bluetooth output 1 (assumed 200) lost, wired output 2 playing; Page Down prints `output 1 … assumed 200 lag 0` next to `picture delay 0` (`TerminalPrinter.cs:142-152`, `SessionEventLogWriter.cs:34, 43`). This breaks cli.md §1.1 (picture delay = largest assumed among the outputs; lag = picture delay − assumed) and feeds stale numbers into the AC-16 pairing. `PlaybackSessionFileChangeTests.cs:205` checks only numbering.

Owner decision: mark lost outputs in `OutputLatency` and print/log them as `output N  lost`, keeping the numbering; state the rule in cli.md §1.1/§3.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-12, AC-13, AC-17.

## Definition of Done

- [ ] `OutputLatency` carries whether the output is lost; lost outputs keep their output number.
- [ ] The terminal prints a lost output as `output N  lost` (no assumed/lag) and the per-output log records mark it lost; the playing outputs' lines satisfy cli.md §1.1 against the printed picture delay.
- [ ] Test: lose the slowest output, Page Down, assert the printed lines and the `LatencyEstimated` / `PictureDelayChanged(file-change)` records; it fails on the old code.
- [ ] cli.md §1.1 and §3 state the rule for lost outputs.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
