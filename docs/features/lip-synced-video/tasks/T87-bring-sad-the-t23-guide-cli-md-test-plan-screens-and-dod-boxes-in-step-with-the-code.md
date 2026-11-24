---
id: T87
title: "Bring sad, the T23 guide, cli.md, test-plan, screens and DoD boxes in step with the code"
layer: "docs"
deps: ["T80", "T81", "T82", "T83", "T84", "T85", "T86"]
blocks: []
acs: ["AC-05", "AC-07", "AC-17"]
files_hint: ["docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/tasks/T10-device-latency-probes.md", "docs/features/lip-synced-video/tasks/T21-terminal-lines-and-run-summaries.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22 (P11)"
---

<!-- Follow-up task from review-2026-11-22. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22.md) · repo `CLAUDE.md`. Do not guess. -->

# T87 — Bring sad, the T23 guide, cli.md, test-plan, screens and DoD boxes in step with the code

## Why

P11 (rest of R12): `sad.md:23, 571` (QG-2(b)) and `evidence/t23-acceptance-run-guide.md:63` (L2) lack `return` with 3 s and "counted from the key press", so a 2 s return would be judged FAIL; `contracts/cli.md:106` allows a null picture error in `LipSyncedAgain` that cannot occur (`PlaybackSession.cs:1306`); `test-plan.md:119` cites the test T78 renamed; `screens.md:60-63` prints `next` before `run-summary`, the reverse of the code and AC-07; `sad.md:48, 138` (§2) omit T70's `RunSummary.File` / `RunSummaryBuilder.Build(…, file)`; T10:97 and T21:130 hold but are unticked.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-07, AC-17.

## Definition of Done

- [x] sad.md QG-2(b) and T23 L2 include `return` at 3 s and say timing counts from the key press.
- [x] cli.md `LipSyncedAgain` row has no null clause for the picture error (unless T80–T86 made null possible again — then say when).
- [x] test-plan.md:119 removed or pointed at the renamed test; all test names in test-plan.md exist.
- [x] screens.md SCR-01 wireframe prints `run-summary` before `next`.
- [x] sad.md §2 (and line 138) names `RunSummary.File` / `RunSummaryBuilder.Build(…, file)` (T70) and any Core changes T80–T86 made.
- [x] T10:97 and T21:130 ticked; every DoD box of T80–T86 that holds is ticked.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
