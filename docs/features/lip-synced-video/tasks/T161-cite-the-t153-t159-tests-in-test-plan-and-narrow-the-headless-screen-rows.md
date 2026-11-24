---
id: T161
title: "Cite the T153\u2013T159 tests in test-plan and narrow the headless screen rows"
layer: "docs"
deps: ["T153", "T154", "T155", "T156", "T157", "T158", "T159"]
blocks: ["T23"]
acs: ["AC-01", "AC-03b", "AC-04", "AC-05", "AC-06b", "AC-07", "AC-10", "AC-12", "AC-13", "AC-15", "AC-15b", "AC-17"]
files_hint: ["docs/features/lip-synced-video/test-plan.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23i (AA1–AA6, AA8)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T161 — Cite the T153–T159 tests in test-plan and narrow the headless screen rows

## Why

- The AC rows of test-plan.md must cite the tests T153–T159 add. Rows :35 and :45 claim more than the single-screen headless tests show (AA6); :19 and :64 say "the three error end-file tests" are skipped without libmpv (AA8: after T159, two are).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03b, AC-04, AC-05, AC-06b, AC-07, AC-10, AC-12, AC-13, AC-15, AC-15b, AC-17.

## Definition of Done

- [x] The AC-01, AC-03b rows cite the T153 and T154 tests; :35 and :45 say what headless shows and that the two-screen choice is pinned by `ScreenChoiceTests`. *(Row 35 (AC-01) and row 45 (AC-03b) cite the T153 and T154 tests and say headless runs only on the primary screen; the two-screen choice is pinned by ScreenChoiceTests)*
- [x] The AC-04, AC-05, AC-07, AC-17 rows cite the T155 tests; AC-07, AC-15, AC-15b cite T156; the AC-17 row (:107) cites T157; AC-17, AC-10, AC-06b cite T158; AC-12, AC-13 cite T154's output-line checks. *(T155 cited in AC-04, AC-05, AC-07, AC-17; T156 in AC-07, AC-15, AC-15b; T157 in AC-17 and AC-06b; T158 in AC-17, AC-10, AC-06b; T154 output-line check in AC-12, AC-13)*
- [x] test-plan.md:19 and :64 say "the two error end-file tests" are skipped without libmpv. *(Lines 19 and 64 now say 'the two error end-file tests'; grep finds no 'three error end-file')*
- [x] One row per task T153–T160 is added in the task table, as for T147. *(Eight task rows T153-T160 added after the T150 row, same format as T147)*
- [x] Every test name cited in test-plan.md exists under the cited class (script check). *(Script check: 259 cited test names resolved, 0 missing)*
