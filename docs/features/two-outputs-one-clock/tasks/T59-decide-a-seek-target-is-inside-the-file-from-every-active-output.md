---
id: T59
title: "Decide a seek target is inside the file from every active output, hung ones included"
layer: "app"
deps: []
blocks: []
acs: ["AC-09", "AC-10"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineSeekEvidenceTests.cs", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r5 (U2)"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from re-review 5. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r5.md) · repo `CLAUDE.md`. Do not guess. -->

# T59 — Decide a seek target is inside the file from every active output, hung ones included

## Why

U2 (stage-1, AC-09/AC-10): seek confirmation (`AudioEngine.cs:377,387`) decides "the target is inside the file" only from `confirming` (non-stalled outputs). With one hung output whose track has audio at the target (the pump keeps writing new-epoch blocks into its ring, reclaiming space, `DecodePump.cs:341-347`) and one live output whose track ended before the target (marked "no blocks in epoch"), `confirming.Any(HasAudioOfEpoch)` stays false: the seek times out (`playback.seek_timeout`) or, once the decoder reaches EOF, branch 1 returns past-end and the run ends `SeekPastEnd` although the target is inside the file. Old code failed this too (not a regression).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09, AC-10.

## Definition of Done

- [ ] Both branches decide "inside the file" from `active.Any(o => HasAudioOfEpoch(o, epoch))`; the `HasPlayedOutOldEpoch` / no-blocks gate still applies to `confirming` (non-stalled outputs) only.
- [ ] A test with one hung output whose track has audio at the target and one live output whose track ended before it: the seek succeeds (OutsideFile false, no `SeekTargetOutsideFile`, no `playback.seek_timeout`) within fake time; it fails on the current code.
- [ ] Existing seek-evidence tests (T2/T3 cases, past-end) stay green unchanged.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
