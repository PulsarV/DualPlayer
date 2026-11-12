---
id: T36
title: "Keep an output that hung during a seek measured and corrected after it recovers"
layer: "app"
deps: ["T35"]
blocks: []
acs: ["AC-07", "AC-09", "AC-11"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12-r2 (R3)"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T36 — Keep an output that hung during a seek measured and corrected after it recovers

## Why

R3 (review-2026-11-12-r2): `WaitForSeek` skips stalled outputs but `BeginAlignment` still runs for them with a pre-seek measurement (~−116 s). On recovery `ApplyEpoch` discards the pending re-align, after which the output is never measured or corrected and its records show 0 ms drift while it plays ~2 s ahead.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-09, AC-11.

## Definition of Done

- [ ] No alignment is started for a stalled output; the position measurement returns false until the output has applied the current epoch.
- [ ] When a hung output recovers, it is aligned once from its true position; `RealignsRequested`/`RealignsApplied` stay consistent.
- [ ] Engine test (`EngineRig`): hang output 2, seek, recover after 1 s → within the drift tolerance within the recovery time after recovery, records report its real drift (not a constant 0), other outputs unaffected.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
