---
id: T45
title: "Keep the recovery window to start, resume and seek"
layer: "app"
deps: ["T44"]
blocks: []
acs: ["AC-07"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRecoveryTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r3 (S5)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T45 — Keep the recovery window to start, resume and seek

## Why

S5 (review-2026-11-12-r3): `StartAlignmentIfRecovered` calls `BeginRecovery`, opening a ~1 s exclusion window for every output's max drift and excursions; spec §6 excludes only the 1 s after start, resume or seek.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07.

## Definition of Done

- [ ] No recovery window is opened when a hung output recovers.
- [ ] Test: after a hang across a seek, the recovered output's off-sync interval shows in `max_drift_ms` / excursions, and the other output's records are not excluded.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
