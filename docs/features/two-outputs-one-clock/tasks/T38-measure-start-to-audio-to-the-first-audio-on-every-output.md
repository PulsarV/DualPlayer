---
id: T38
title: "Measure start-to-audio to the first audio on every output"
layer: "app"
deps: ["T37"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Playback/EngineEvent.cs", "src/DualPlayer.Console/Commands/PlayCommand.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Console.Tests/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r2 (R7)"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T38 — Measure start-to-audio to the first audio on every output

## Why

R7 (review-2026-11-12-r2): `RunStarted.StartToAudio` is taken right after `device.Start()`; on WASAPI that only launches the render thread, so the printed `start-to-audio` understates the spec §6 figure. The real per-device value is only in the later "First audio on …" log line.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [ ] `StartToAudio` = latest per-output first-audio timestamp − start command instant; the started line is printed once that is known (or the value arrives in a later event the printer uses).
- [ ] Test with a fake device whose first audio lands later than `Start()` returns: the value reflects the first-audio instant.
- [ ] T22's task text points at the "First audio" log lines as the evidence.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
