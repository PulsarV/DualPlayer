---
id: T146
title: "Cite the T145 test in test-plan and fix the AC-02 row punctuation"
layer: "docs"
deps: ["T145"]
blocks: ["T23"]
acs: ["AC-02", "AC-09b"]
files_hint: ["docs/features/lip-synced-video/test-plan.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23g (X1, X2)"
---

<!-- Follow-up task from review-2026-11-23g. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23g.md) · repo `CLAUDE.md`. Do not guess. -->

# T146 — Cite the T145 test in test-plan and fix the AC-02 row punctuation

## Why

- **X1.** test-plan.md:75 cites only adapter tests for the decoder-reported `access-denied`; the T145 session theory needs a citation.
- **X2.** test-plan.md:37 (AC-02) lacks ". " before "Tests:", unlike the other rows.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02, AC-09b.

## Definition of Done

- [x] test-plan.md row 75 (AC-09b `access-denied`) cites the T145 session theory; the AC-02 row (line 37) has ". " before "Tests:". *(Rows 37 and 75, and a new T145 row.)*
- [x] Every test name cited in test-plan.md exists under the cited class (grep check) *(121 cited class.method names, 0 missing; `A_seek_whose_engine_call_takes_300_ms_...` resolves as a prefix.)*
