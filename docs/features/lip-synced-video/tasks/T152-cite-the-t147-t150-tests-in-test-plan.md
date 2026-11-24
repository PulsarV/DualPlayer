---
id: T152
title: "Cite the T147–T150 tests in test-plan"
layer: "docs"
deps: ["T147", "T148", "T149", "T150"]
blocks: ["T23"]
acs: ["AC-01", "AC-03b", "AC-06b", "AC-17"]
files_hint: ["docs/features/lip-synced-video/test-plan.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23h (Y1, Y2, Y3)"
---

<!-- Follow-up task from review-2026-11-23h. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23h.md) · repo `CLAUDE.md`. Do not guess. -->

# T152 — Cite the T147–T150 tests in test-plan

## Why

- **Y1–Y3.** The AC-06b rows (test-plan.md:147, :174), the AC-01, AC-03b and AC-17 rows need the new tests cited. test-plan.md:19 promises "one shared behaviour suite runs against both renderer implementations", which was never built.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03b, AC-06b, AC-17.

## Definition of Done

- [x] The AC-06b rows cite the T147 and T148 tests; the AC-01 and AC-03b rows cite the T149 tests (and T148's handle case for AC-01); the AC-01 start-budget and AC-17 rows cite the T150 test; one row per task T147–T150 is added in the task table, as for T145. *(AC-06b rows cite the four `MpvEndFileTests` and seven `WindowBoundRendererTests` tests; AC-01/AC-03b rows cite the T149 and T148 handle tests; the AC-01/AC-17 row cites the T150 test; rows for T147–T150 added to the task table.)*
- [x] test-plan.md:19 describes the renderer contract checks that exist (the fake's behaviour tests, the real adapter's `OnEndFile` and wrapper tests, the hardware tests) instead of the shared suite that was never built. *(Line 19 now names `FakeVideoRendererTests`, `MpvEndFileTests`, `WindowBoundRendererTests` and the libmpv-gated `MpvVideoRendererTests`; no shared suite is claimed.)*
- [x] Every test name cited in test-plan.md exists under the cited class (script check). *(Script check: 222 `Class.Method` citations resolved, 0 missing; every cited `*Tests` class exists.)*
