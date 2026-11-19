---
id: T73
title: "Carry the pre-roll stop check and the 3 s start target into the run guide and T22"
layer: "app"
deps: []
blocks: []
acs: ["AC-08", "AC-13", "AC-01"]
files_hint: ["docs/features/two-outputs-one-clock/evidence/target-pc-run-guide.md", "docs/features/two-outputs-one-clock/tasks/T22-record-target-pc-evidence-run.md"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r3: F1, F2"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-17-r3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T73 — Carry the pre-roll stop check and the 3 s start target into the run guide and T22

## Why

- F1: the target-PC check R1 asked for (stop ~0.5 s and ~1 s after `play` on the Bluetooth pair, before `started …`; every output falls silent together, no film tail on the slower headset) reached only `test-plan.md:103`. The run guide §5 (`evidence/target-pc-run-guide.md:157-168`) and T22 do not carry it, so the `sad.md:539` mitigation has no executable step.
- F2: `evidence/target-pc-run-guide.md:162`, `:202` and the T22 quote of spec §6 (`tasks/T22-record-target-pc-evidence-run.md:60`) still say start-to-audio ≤ 2 s; spec.md:193 (`changed-by-fix`, ADR-0008) and `test-plan.md:102` say ≤ 3 s.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-13, AC-01 and §6 row "Start command to audio on all outputs"; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md); [test-plan.md](../test-plan.md) NFR validation.

## Definition of Done

- [x] The run guide §5 has a row for the pre-roll stop (AC-08, AC-13): 5 times `stop` ~0.5 s and ~1 s after `play` on A+B, before `started …`; expected: both headsets fall silent together, no film tail on the slower one, the run summary is printed.
- [x] The run guide §5 and §7 rows and the T22 spec §6 quote say start-to-audio ≤ 3 s.
- [x] The T22 checklist points to the pre-roll stop check.
- [x] The author's uncommitted run-guide change (§3.1/§3.2) stays out of this task's commit.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
