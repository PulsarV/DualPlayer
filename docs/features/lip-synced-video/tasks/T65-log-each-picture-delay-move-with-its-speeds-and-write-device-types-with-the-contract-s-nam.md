---
id: T65
title: "Log each picture-delay move with its speeds and write device types with the contract's names everywhere"
layer: "app"
deps: ["T64"]
blocks: []
acs: ["AC-14", "AC-17", "AC-13"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Diagnostics/", "src/DualPlayer.Core/Sync/", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "src/DualPlayer.App/Startup/RunSummaryLines.cs", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md", "tests/DualPlayer.Core.Tests/", "tests/DualPlayer.App.Tests/Startup/"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21c (S12, S10)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T65 — Log each picture-delay move with its speeds and write device types with the contract's names everywhere

## Why

S12: the log records no playback speed during a picture-delay move — `CorrectionPpm` (logged as `correction=…ppm`) excludes `RampPpm` (`DriftController.cs:24, 27`, `OutputSync.cs:69`), mpv's `speed` is never logged (`MpvVideoRenderer.cs:400-415`), and nothing records a move's rate or duration. T23 L3 "playback speed ≤ 2000 ppm" (`evidence/t23-acceptance-run-guide.md:64`) passes whatever the ramp does. Spec §6 "Picture-delay change" is measured by the log of playback speed; ADR-0005 says the ramp is logged as its own event.

S10: `LogLatency` (`PlaybackSession.cs:1549-1550`) writes `type BluetoothHeadphones` next to `LatencyEstimated … Type=bluetooth-headphones` (T53 DoD: every enum value in the log uses the cli.md string).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-14, AC-17, AC-13.

## Definition of Done

- [ ] Every picture-delay move publishes one move record: cause, from and to picture delay, duration, the picture's speed in ppm and each output's ramp in ppm; the log writer writes it with contract names.
- [ ] cli.md §3 lists the record; T23 L3 reads it (≤ 2000 ppm for the picture and every ramp).
- [ ] The device-type mapper lives in Core and both `LogLatency` and the App log writer use it; the Core log line writes `bluetooth-headphones`.
- [ ] Tests: an offset change publishes the move record with speeds ≤ 2000 ppm and the stretched duration; the writer writes its fields; the latency line uses the contract type name.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
