---
id: T47
title: "Add the target-PC load scenarios to the T23 run guide"
layer: "docs"
deps: []
blocks: []
acs: ["AC-01", "AC-04", "AC-05", "AC-07", "AC-14"]
files_hint: ["docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/tasks/T23-measure-lip-sync-on-the-target-pc.md"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21 (R12)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T47 — Add the target-PC load scenarios to the T23 run guide

## Why

R12 (stage-1, §6/§6.1): four target-PC load scenarios in `test-plan.md:148-150, 153` belong to no task — 20 consecutive starts within ≤ 3 s; 60 scripted commands + 5 file changes with recovery ≤ 1 s; 20 offset presses at ≤ 0.2 %; a 5,000-file folder. T23's run guide §5 covers only lip-sync, CPU and late/dropped frames.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-04, AC-05, AC-07, AC-14.

## Definition of Done

- [x] The T23 run guide has a section for each of the four scenarios: setup, steps, what to read from the log/terminal, pass threshold, and what the evidence file records.
- [x] test-plan.md rows 148-150, 153 point at T23 / the guide section.
- [x] T23's task file lists the scenarios in its DoD.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
