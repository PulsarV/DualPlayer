---
id: T166
title: "Tighten the recovery-budget edge case to 1.0 s"
layer: "test"
deps: []
blocks: ["T23", "T168"]
acs: ["AC-17"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionBoundEdgeTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "XS"
context_budget: "S"
origin: "review-2026-11-23j (AB6)"
---

<!-- Follow-up task from review-2026-11-23j. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23j.md) · repo `CLAUDE.md`. Do not guess. -->

# T166 — Tighten the recovery-budget edge case to 1.0 s

## Why

- **AB6.** `PlaybackSessionBoundEdgeTests.A_seek_whose_output_qualifies_at_1_1_s_gives_up_AC_17` (`tests/DualPlayer.Core.Tests/Playback/PlaybackSessionBoundEdgeTests.cs:88-95`) clears the error at `Render(110)`; outputs are read at most every 100 ms, so the output qualifies only at about 1.2 s and `RecoveryBudget = 1.2 s` passes the class. The name overstates the test.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17.

## Definition of Done

- [x] The case clears the output error at 1.0 s (`Render(100)`) and is renamed to say what it checks; it still expects `NotLipSynced` for `seek` and no `LipSyncedAgain`. *(renamed to A_seek_whose_output_is_still_off_at_1_0_s_gives_up_AC_17, Render(100); test-plan rows AC-05 and AC-17 follow the rename)*
- [x] Mutant on a scratch copy fails the case: `RecoveryBudget = 1.2 s`. (1.1 s stays indistinguishable at the 100 ms read step; say so in a comment.) *(RecoveryBudget = 1.2 s fails the renamed case; 1.0 s passes; comment states the 100 ms read step)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(build 0 warnings, all suites pass, format clean)*
