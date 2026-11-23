---
id: T151
title: "State in spec and cli.md that a run-out track listing is remembered but not printed or counted"
layer: "docs"
deps: []
blocks: ["T23"]
acs: ["AC-08", "AC-09b"]
files_hint: ["docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/contracts/cli.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23h (Y4)"
---

<!-- Follow-up task from review-2026-11-23h. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23h.md) · repo `CLAUDE.md`. Do not guess. -->

# T151 — State in spec and cli.md that a run-out track listing is remembered but not printed or counted

## Why

- **Y4.** A track listing that runs out the budget is remembered and ends the press with no `skipped` line and no count (`PlaybackSession.cs:564-571, 877-887`; pinned by `PlaybackSessionNeighbourBudgetTests.cs:159-162` and test-plan.md:234, owner choice Z10). spec.md:249 says "the stepped-past and the run-out files are still reported as skipped and counted", and cli.md:85 says the same. The same holds for a remembered file checked last whose new listing hangs after its earlier worker has ended.

Owner decision: keep the code; align the docs.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [x] spec.md:249 adds the listing exception: a file whose track listing runs out the budget (its own full limit, or as the first check of the press) ends the press; it is remembered as timed out but neither reported as skipped nor counted in that press, including a remembered file checked last whose new listing hangs. "the run-out files are still reported as skipped and counted" is limited to probes that ran out their full limit. *(spec.md:249 now has the listing exception; matches PlaybackSession give-up path and PlaybackSessionNeighbourBudgetTests:159-162)*
- [x] cli.md:85 states the same rule in its own words; the `next-file-timeout  checked N` text and the stepped-past rule are otherwise unchanged. *(contracts/cli.md:85 states the same rule; next-file-timeout checked N text unchanged)*
- [x] test-plan.md:234 and the spec/cli wording agree (read check); no code or test changes. *(agrees with test-plan.md:234 (checked 0 first press, checked 1 later); docs only, no code or tests)*
