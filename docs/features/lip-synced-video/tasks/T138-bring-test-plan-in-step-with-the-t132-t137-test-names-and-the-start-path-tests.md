---
id: T138
title: "Bring test-plan in step with the T132-T137 test names and the start-path tests"
layer: "docs"
deps: ["T136", "T137"]
blocks: ["T23"]
acs: ["AC-06", "AC-08", "AC-09b"]
files_hint: ["docs/features/lip-synced-video/test-plan.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23d (V4)"
---

<!-- Follow-up task from review-2026-11-23d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23d.md) · repo `CLAUDE.md`. Do not guess. -->

# T138 — Bring test-plan in step with the T132-T137 test names and the start-path tests

## Why

- **V4.** test-plan.md:193 cites the pre-T132 name `A_give_up_on_a_probe_worker_that_stays_busy_forever_costs_a_small_bounded_real_time_AC_08` (now `…_polls_it_a_small_bounded_number_of_times_AC_08`, counting `IsBusy` polls); no row cites T133's test; row 200 labels T134's AppEntryTests case as T135; :206 says the Closing test raises its video handle, which it does not.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06, AC-08, AC-09b.

## Definition of Done

- [ ] test-plan.md:193 cites `…_polls_it_a_small_bounded_number_of_times_AC_08` and says "polls the adapter at most `MaxBusyPolls` times".
- [ ] Rows cite T132's rebuilt grace and listing-budget tests, T133's `A_cut_first_file_whose_wrapper_never_answers_is_remembered_only_under_the_whole_budget_rule_AC_08`, and T136's and T137's tests by their final names.
- [ ] Row 200 is labelled T134; the sentence at :206 no longer says the Closing test raises its video handle and cites the T136 start-path tests.
- [ ] Every test name cited in test-plan.md exists under the cited class (grep check).
