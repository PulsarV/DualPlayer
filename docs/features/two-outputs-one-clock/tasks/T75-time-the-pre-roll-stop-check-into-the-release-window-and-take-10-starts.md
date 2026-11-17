---
id: T75
title: "Time the pre-roll stop check into the release-to-start window and take start-to-audio over 10 starts"
layer: "app"
deps: []
blocks: []
acs: ["AC-08", "AC-13", "AC-01"]
files_hint: ["docs/features/two-outputs-one-clock/evidence/target-pc-run-guide.md", "docs/features/two-outputs-one-clock/tasks/T22-record-target-pc-evidence-run.md", "docs/features/two-outputs-one-clock/test-plan.md", "docs/features/two-outputs-one-clock/sad.md"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r4: F1, F2"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-17-r4. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r4.md) · repo `CLAUDE.md`. Do not guess. -->

# T75 — Time the pre-roll stop check into the release-to-start window and take start-to-audio over 10 starts

## Why

- F1: the pre-roll stop check (`evidence/target-pc-run-guide.md:127` at HEAD, `tasks/T22-record-target-pc-evidence-run.md:108`, `test-plan.md:103`, `sad.md:539`) stops ~0.5 s / ~1 s after `play`. On the JBL (0.6 s) + MOTIF (~1 s) pair the film release (`AudioEngine.cs:447-457`) comes ~1.0–1.1 s after the devices start and the master clock ~2 s after, so the stop lands before `Film released …`; no film is queued anywhere (`AudioEngine.cs:978-983`) and every run passes by construction. The film-tail risk of `sad.md:539` is never exercised.
- F2: the run guide (`:125`, `:166` at HEAD) and T22 (`:109`) take start-to-audio ≤ 3 s from one start; `test-plan.md:102` asks for 10 starts, ≤ 3 s every time.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-13, AC-01 and §6 row "Start command to audio on all outputs"; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md); [test-plan.md](../test-plan.md) NFR validation.

## Definition of Done

- [x] The pre-roll stop check (guide §5, T22, test-plan.md, sad.md §11) aims the stop after the film release and before the master-clock start (~1.5 s after `play` on the Bluetooth pair) and counts a run only when its log shows `Film released …` before `Run ended (Stop)` and the console printed no `started …` line; 10 counted runs.
- [x] The AC-01 start check in guide §5 is 10 starts, ≤ 3 s each; guide §7 and T22 record the maximum of the 10 `started` values.
- [x] The author's uncommitted run-guide change (§3.1/§3.2) stays out of this task's commit.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
