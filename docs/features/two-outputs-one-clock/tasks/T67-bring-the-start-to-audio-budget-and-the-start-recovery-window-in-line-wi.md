---
id: T67
title: "Bring the start-to-audio budget and the start recovery window in line with ADR-0008 everywhere"
layer: "app"
deps: ["T66"]
blocks: []
acs: ["AC-01", "AC-07"]
files_hint: ["docs/features/two-outputs-one-clock/spec.md", "docs/features/two-outputs-one-clock/sad.md", "docs/features/two-outputs-one-clock/test-plan.md", "docs/features/two-outputs-one-clock/adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md", "docs/features/two-outputs-one-clock/CHANGELOG.md", "docs/features/two-outputs-one-clock/contracts/api-sync-report.md", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17: S1, S2"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from review-2026-11-17. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17.md) · repo `CLAUDE.md`. Do not guess. -->

# T67 — Bring the start-to-audio budget and the start recovery window in line with ADR-0008 everywhere

## Why

- S1: stale "≤ 2 s": `contracts/api-sync-report.md:34`, `CHANGELOG.md:31` (and ADR-0008 missing from its key decisions), `adr/0008-…md:24` (driver) and `:57` (Links). `AudioEngineRunTests.cs:592,609` `…just_past_the_two_second_start_to_audio_budget` uses a 2.35 s first render — now inside the 3 s budget, so T62 intent (a render past the budget is reported) is unpinned. `sad.md:461`, `test-plan.md:102` say "first rendered frame"; under ADR-0008 that is pre-roll silence; the engine logs the first film frame heard (`AudioEngine.cs:883`).
- S2: at start the 1 s recovery window counts from the clock start (`AudioEngine.cs:421` `BeginRecovery(run, run.ClockStartTimestamp)`; tests `AudioEngineRecoveryTests.cs:42`, `AudioEngineStartAndHangTests.cs:269`), but `spec.md:190-191` and QG-1 `sad.md:507` say "≤ 1 s after the command". T22 is judged against that text.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-07; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [ ] All listed "≤ 2 s" start-to-audio mentions read ≤ 3 s; CHANGELOG key decisions list ADR-0008.
- [ ] The T62 test moves the late first render to ~3.4 s (still under the 5 s `FirstAudioDeadline`), is renamed to say three seconds, and still fails against a 3 s deadline.
- [ ] sad.md §… and test-plan.md say "first film frame heard".
- [ ] spec §6 rows (drift recovery windows + recovery row) and QG-1 say that for a start the window opens when the master clock starts (ADR-0008); mark each change `<!-- changed-by-fix: 2026-11-17 -->` like the existing row.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
