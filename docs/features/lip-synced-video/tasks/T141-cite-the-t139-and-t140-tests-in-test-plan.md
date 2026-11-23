---
id: T141
title: "Cite the T139 and T140 tests in test-plan"
layer: "docs"
deps: ["T139", "T140"]
blocks: ["T23"]
acs: ["AC-05", "AC-06", "AC-06b", "AC-07", "AC-15b"]
files_hint: ["docs/features/lip-synced-video/test-plan.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23e (U2)"
---

<!-- Follow-up task from review-2026-11-23e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23e.md) · repo `CLAUDE.md`. Do not guess. -->

# T141 — Cite the T139 and T140 tests in test-plan

## Why

- **U2.** test-plan.md:58 must cite the new exit-while-paused test; the T139/T140 tests need rows like the earlier follow-ups.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-06, AC-06b, AC-07, AC-15b.

## Definition of Done

- [ ] test-plan.md row 58 (AC-06) cites the T140 exit-while-paused e2e test; a T139/T140 row cites the note-text asserts and the reworked OS-close test by their final names.
- [ ] Every test name cited in test-plan.md exists under the cited class (grep check)
