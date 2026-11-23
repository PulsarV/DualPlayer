---
id: T144
title: "Cite the T142 and T143 tests in test-plan"
layer: "docs"
deps: ["T142", "T143"]
blocks: ["T23"]
acs: ["AC-02", "AC-09b"]
files_hint: ["docs/features/lip-synced-video/test-plan.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23f (W1)"
---

<!-- Follow-up task from review-2026-11-23f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23f.md) · repo `CLAUDE.md`. Do not guess. -->

# T144 — Cite the T142 and T143 tests in test-plan

## Why

- **W1.** test-plan.md:74 promises "note gives the reason in plain words"; :75 cites only adapter tests for `access-denied`. The T142/T143 tests need citations like the earlier follow-ups.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02, AC-09b.

## Definition of Done

- [ ] test-plan.md rows 74–75 (AC-09b) cite the T142 session theory and the access-denied terminal line test; the AC-02 no-picture row cites the strengthened probe test.
- [ ] Every test name cited in test-plan.md exists under the cited class (grep check)
