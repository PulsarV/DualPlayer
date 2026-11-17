---
id: T77
title: "Name the log file and the last run block in the pre-roll stop check"
layer: "app"
deps: []
blocks: []
acs: ["AC-08", "AC-13"]
files_hint: ["docs/features/two-outputs-one-clock/evidence/target-pc-run-guide.md"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r6: F1"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from review-2026-11-17-r6. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r6.md) · repo `CLAUDE.md`. Do not guess. -->

# T77 — Name the log file and the last run block in the pre-roll stop check

## Why

- F1: the pre-roll stop row (`evidence/target-pc-run-guide.md:127` at `7e28962`) tells the operator to read "this run's" log lines but names neither the log file (the path appears only in §7, `target-pc-run-guide.md:151`, `%APPDATA%\DualPlayer\logs\dualplayer-console-<дата>.log`) nor that this run's block is the last `Run started with …` … `Run ended (Stop)` pair at the end of the file.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-13; [test-plan.md](../test-plan.md) NFR validation.

## Definition of Done

- [ ] The pre-roll stop row in guide §5 names the log file (path + "див. §7") and says this run's lines are the last `Run started with …` … `Run ended (Stop)` block at the end of the file.
- [ ] The author's uncommitted run-guide change (§3.1/§3.2) stays out of this task's commit.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
