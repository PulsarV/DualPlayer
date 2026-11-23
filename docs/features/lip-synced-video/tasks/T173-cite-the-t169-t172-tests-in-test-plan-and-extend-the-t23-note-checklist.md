---
id: T173
title: "Cite the T169\u2013T172 tests in test-plan and extend the T23 note checklist"
layer: "docs"
deps: ["T169", "T170", "T171", "T172"]
blocks: ["T23"]
acs: ["AC-02", "AC-03", "AC-03b", "AC-09", "AC-10", "AC-16"]
files_hint: ["docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "XS"
context_budget: "XS"
origin: "review-2026-11-24 (AD1, AD2, AD3, AD4)"
---

<!-- Follow-up task from review-2026-11-24. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-24.md) · repo `CLAUDE.md`. Do not guess. -->

# T173 — Cite the T169–T172 tests in test-plan and extend the T23 note checklist

## Why

- **AD1, AD2, AD3, AD4.** Doc side of AD1–AD4: test-plan.md:74-76 (AC-09) cite only equal output/track fixtures; test-plan.md:105 (AC-16) says the test uses "the script's own filter graph" — true only after T170; the note rows and the App-entry rows need the T171/T172 tests. The T23 guide §6 notes item (:76) checks yellow warnings but not that an info note after a warning is back to the normal colour.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02, AC-03, AC-03b, AC-09, AC-10, AC-16.

## Definition of Done

- [ ] test-plan.md cites the T169–T172 tests in the AC-09, AC-16, note (SCR-03) and App-entry / exit-code rows; :105 says the test reads the generator script and covers H.264 and HEVC; every cited `Class.Method` resolves (scripted check).
- [ ] The T23 guide §6 notes item adds: an info note right after a warning is in the normal colour, not yellow.
- [ ] No code or test changes.
