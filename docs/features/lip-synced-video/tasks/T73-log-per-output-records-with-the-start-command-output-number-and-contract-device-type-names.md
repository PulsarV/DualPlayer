---
id: T73
title: "Log per-output records with the start-command output number and contract device-type names"
layer: "app"
deps: ["T70", "T72"]
blocks: []
acs: ["AC-13", "AC-14", "AC-15", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/Startup/"]
status: "blocked"
step: "RED — test defect: ShouldNotContain is case-insensitive"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21d (R6, R8)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T73 — Log per-output records with the start-command output number and contract device-type names

## Why

R6: after a lost output, per-output log lines carry shifted output numbers: `Reestimate` returns only playing outputs (`PlaybackSession.cs:798-803`, also `712, 1616`) and `SessionEventLogWriter` numbers them `i + 1` (`SessionEventLogWriter.cs:43, 54-60, 126-143`). With output 1 lost, output 2 is logged as `Output=1` in `PictureDelayChanged` and `PictureDelayMoved` — misleading the AC-16 pairing with three outputs (cli.md §3).

R8: "type Other" is still written: the probe-failure `Source` string of `LatencyEstimated` and its warning use the enum name next to `Type=other` (`PlaybackSession.cs:245, 1639`). T65 DoD said contract names everywhere.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-13, AC-14, AC-15, AC-17.

## Definition of Done

- [ ] Every per-output event carries the output's number from the start command (its index in the full estimate), and the log writer prints that number instead of the list position.
- [ ] Test: three outputs, output 1 lost, then a picture-delay change → the records name outputs 2 and 3.
- [ ] `PlaybackSession.cs:245, 1639` use `DeviceTypeNames.Of(DeviceType.Other)`; a test asserts the source/warning say `type other`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
