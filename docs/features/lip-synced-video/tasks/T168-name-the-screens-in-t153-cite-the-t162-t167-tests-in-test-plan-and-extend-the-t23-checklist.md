---
id: T168
title: "Name the screens in T153, cite the T162\u2013T167 tests in test-plan and extend the T23 checklist"
layer: "docs"
deps: ["T162", "T163", "T164", "T165", "T166"]
blocks: ["T23"]
acs: ["AC-01", "AC-03b", "AC-07", "AC-08", "AC-10", "AC-17"]
files_hint: ["docs/features/lip-synced-video/tasks/T153-move-the-screen-mapping-and-the-number-to-screen-choice-into-pure-helpers-and-pin-them-with-two-screens.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23j (AB1, AB2, AB4)"
---

<!-- Follow-up task from review-2026-11-23j. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23j.md) · repo `CLAUDE.md`. Do not guess. -->

# T168 — Name the screens in T153, cite the T162–T167 tests in test-plan and extend the T23 checklist

## Why

- **AB4.** T153 (`ui`) names no screen id — the only `ui` task with no link to screens.md. Plus the doc side of AB1/AB2: test-plan.md:87 claims "note shown for 2 s, replaced by the next note" without a test that sends the hold; the T23 refusal checklist (`evidence/t23-acceptance-run-guide.md:74`) has no usage or too-many-outputs case, and the notes item (:76) checks only placement.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03b, AC-07, AC-08, AC-10, AC-17.

## Definition of Done

- [x] T153's task file has a `## Trace` naming SCR-02 (opening on the chosen screen) and SCR-01 (error — screen not found, numbered list). *(T153 has a ## Trace naming SCR-02 (loading — opening on the chosen screen) and SCR-01 (error — screen not found, numbered list))*
- [x] test-plan.md cites the T162–T166 tests in the AC-01, AC-03b, AC-07/AC-08, AC-10 (note hold and colour, row :87 corrected to what is tested) and AC-17 rows; every cited `Class.Method` resolves (scripted check). *(test-plan AC-01, AC-03b, AC-07, AC-10 (row corrected to the mpv command level + T23) and App-entry rows cite T162–T165; new T162–T166 rows; scripted check: 277 citations resolve, 0 bad)*
- [x] The T23 guide §6 adds: `play` with no file → `error cli.usage: …`, exit 2, no window; four `--out` → `error outputs.too_many: …`, exit 2; notes held about 2 s and warning notes yellow. *(T23 guide §6: cli.usage and outputs.too_many refusals (exit 2, no window); notes held about 2 s, warnings yellow)*
- [x] No code or test changes. *(no code or test changes)*
