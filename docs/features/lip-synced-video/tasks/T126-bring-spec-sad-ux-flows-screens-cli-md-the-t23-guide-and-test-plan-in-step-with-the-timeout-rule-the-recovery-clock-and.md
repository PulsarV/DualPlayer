---
id: T126
title: "Bring spec, sad, ux-flows, screens, cli.md, the T23 guide and test-plan in step with the timeout rule, the recovery clock and the start failures with the window open"
layer: "docs"
deps: ["T122", "T123", "T124", "T125", "T127"]
blocks: []
acs: ["AC-01", "AC-02", "AC-06", "AC-07", "AC-08", "AC-09b", "AC-17"]
files_hint: ["docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/ux-flows.md", "docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/tasks/tracker.md", "docs/features/lip-synced-video/tasks/T23-measure-lip-sync-on-the-target-pc.md", "docs/features/lip-synced-video/tasks.json"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23 (Y6, Y7 (docs), Y8, Y9, Y10 (docs))"
---

<!-- Follow-up task from review-2026-11-23. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23.md) · repo `CLAUDE.md`. Do not guess. -->

# T126 — Bring spec, sad, ux-flows, screens, cli.md, the T23 guide and test-plan in step with the timeout rule, the recovery clock and the start failures with the window open

## Why

- **Y6**. The code, cli.md:85 and sad.md:543 end a press whose only skips were files whose own check ran out its full limit as `next-file-timeout  checked N`; spec.md:249 names only step-pasts, AC-08's Then (spec.md:159) gives the timeout only when the 3 s budget runs out, and sad flow 3's opt (sad.md:323) and the ux-flows US-03 edge (ux-flows.md:91) say the same. Owner decision: the code's behaviour (timeout) is right.
- **Y7** (docs part). cli.md §3 (`:112, :114, :117`) says `LipSyncedAgain` / `NotLipSynced` count "ms since the key press" for every command; for `file-change` they now count from the target choice. The T23 run guide (`evidence/t23-acceptance-run-guide.md:63, 65`) still counts L2 from the key and judges file changes on `StartTimings.CommandToFirstFrameMs`, which runs from the key, scan included. spec.md:235 "with no skips the two moments are the same" is false.
- **Y8**. screens.md (`:34, :73, :78`) says start errors never open the window, and the ux-flows US-01 flowchart (`:42-46`) has no such branch; per cli.md §5 and sad flow 1, `mpv.load_timeout`, `mpv.load_failed`, `outputs.device_open_failed`, `playback.start_timeout` and `playback.decoder_fault` come after `window.Show()` (`App.axaml.cs:135-162`) and the window then closes.
- **Y9**. cli.md §4 (`:120`) documents exit 0 only for a window closed after a film played; closing it during the start returns 0 with no error line (`StartErrors.cs:28-32`).
- **Y10** (docs part). test-plan.md:180 (T115) points to `PlaybackSessionNeighbourWorkerTimingTests`, but the rewritten test is in `PlaybackSessionNeighbourRecoveryTests`; :173 cites the old test name; the `RigLoopsTests` case is not named.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02, AC-06, AC-07, AC-08, AC-09b, AC-17.

## Definition of Done

- [x] spec AC-08's Then and spec §6.1 add "or a file whose own listing or probe ran out its full limit in this press" to the timeout outcome; sad flow 3's opt and the ux-flows US-03 edge match.
- [x] spec.md:235's parenthetical no longer says the two moments are the same (the stop of the old film counts; the queue wait, the folder listing and the target's own check do not). cli.md §3: for `file-change`, `AfterCommandMs` counts from the target choice, and `StartTimings` for a file change counts from the key, scan included. The T23 run guide's L2/L4 judge the file-change 3 s on `LipSyncedAgain(file-change).AfterCommandMs` and the L4 key answer on the `next` / `previous` line time against the 3 s scan budget.
- [x] screens.md SCR-02 `loading — opening` has "start failed → window closed, SCR-01 prints the error, exit 2"; SCR-02 `error` and SCR-01 `error` say start errors found before the window never open it and a session-start failure closes it; the ux-flows US-01 flowchart has that branch.
- [x] cli.md §4's `0` row adds "or the window was closed during the start (no error line; the abandoned start is logged)".
- [x] test-plan.md has a row for every test T122–T125 added, and rows :173 / :180 cite the renamed X9 test and `RigLoopsTests.Disposing_a_session_rig_with_a_stuck_command_returns_within_a_bounded_time`; every DoD box of T122–T125 that holds is ticked; the tracker's T23 row and T23's deps include T122–T126.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
