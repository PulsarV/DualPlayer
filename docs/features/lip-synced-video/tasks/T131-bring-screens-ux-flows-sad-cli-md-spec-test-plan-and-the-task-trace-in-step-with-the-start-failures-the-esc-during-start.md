---
id: T131
title: "Bring screens, ux-flows, sad, cli.md, spec, test-plan and the task trace in step with the start failures, the Esc-during-start path and the cut-probe rule"
layer: "docs"
deps: ["T128", "T129", "T130"]
blocks: ["T23"]
acs: ["AC-01", "AC-02", "AC-06", "AC-07", "AC-08", "AC-09b"]
files_hint: ["docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/ux-flows.md", "docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/tasks.json", "docs/features/lip-synced-video/tasks/tracker.md", "docs/features/lip-synced-video/tasks/T20-playback-window-keys-and-notes.md", "docs/features/lip-synced-video/tasks/T125-stamp-the-file-change-recovery-clock-when-the-target-is-chosen-before-the-old-run-stops.md", "docs/features/lip-synced-video/tasks/T23-measure-lip-sync-on-the-target-pc.md", "src/DualPlayer.App/App.axaml.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23b (Z1 (docs), Z7, Z8, Z9, Z10, Z11, Z12, Z13)"
---

<!-- Follow-up task from review-2026-11-23b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23b.md) · repo `CLAUDE.md`. Do not guess. -->

# T131 — Bring screens, ux-flows, sad, cli.md, spec, test-plan and the task trace in step with the start failures, the Esc-during-start path and the cut-probe rule

## Why

- **Z1** (docs). spec.md:249 and cli.md:85 encode the cut-probe split T129 removes, and say a wrapper is given up only at the press bound, though the code also gives up at its own limit + 50 ms.
- **Z7.** screens.md:34, :73, ux-flows.md:48, :153 and sad.md:249 give a fixed list of five session-start failures with the window open, all exit 2. The code can also close the open window with `outputs.device_not_found` (exit 2), `outputs.device_access_denied` (exit 4, `WasapiErrorMapper.cs:18-23`, `StartExitCodes.cs:12`) and `playback.file_access_denied` / `playback.file_unreadable` from the engine's own open (`AudioEngine.cs:151, 169, 178`). cli.md §4 (:122-123) calls exit 4 "Refused before start"; cli.md §5 (:136, :139) does not say these codes can come with the window open.
- **Z8.** Esc during the start posts `Exit`, which waits behind `Start`; `closing` is set only after `Exited`, so a start that fails while Esc is queued prints its error and exits 2 or 4. Exit 0 with no error line happens only when the OS closes the window. ux-flows.md:49, :61, screens.md:73 and the comment at `src/DualPlayer.App/App.axaml.cs:146` claim otherwise. Owner decision: keep the code, fix the docs and the comment.
- **Z9.** screens.md:109 (SCR-03 "no next / previous" trigger) still names only the 3 s budget running out.
- **Z10.** test-plan.md:217 expects "(checked 1)" for a lone neighbour whose listing runs out; the code (`PlaybackSession.cs:876-886`) gives `checked 0` on the first press.
- **Z11.** T125 claims AC-09, which its change does not touch (`tasks.json`, T125 task file).
- **Z12.** AC-06's window behaviour reaches no `ui` task: T20 builds Esc → `Exit` but does not list AC-06.
- **Z13.** test-plan.md:182 cites the X10 test under `PlaybackSessionNeighbourRecoveryTests`; it is in `PlaybackSessionNeighbourMemoryTests`.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02, AC-06, AC-07, AC-08, AC-09b.

## Definition of Done

- [ ] spec.md:249 and cli.md:85 match T129: a cut probe is never reported as skipped, whether its adapter answered at the limit or its wrapper did not answer; a wrapper is given up at its own limit + the grace or at the press bound, whichever comes first.
- [ ] screens (SCR-01 / SCR-02), ux-flows E4 and sad flow 1 say "any session-start failure (for example …) closes the window; exit code per cli.md §4 (2, or 4 for an access denial)"; cli.md §4 and §5 note that these codes can also come from the session start with the window open.
- [ ] ux-flows E5 and SCR-02 say: Esc during the start acts once the start returns (a failed start still reports its error); closing the window by the OS during the start exits 0 with no error line. The comment at `App.axaml.cs:146` says the same.
- [ ] screens.md:109 adds "or every file the press passed over was stepped past or ran out its own full limit".
- [ ] test-plan.md:217 is split: probe → "(checked 1)"; listing → "(checked 0)" on the first press, "(checked 1)" on later presses while the worker still runs. test-plan.md:182 names `PlaybackSessionNeighbourMemoryTests`. test-plan.md has a row for every test T128–T130 added.
- [ ] T125's `acs` are `["AC-07", "AC-17"]` in tasks.json and its task file; T20's `acs` include AC-06 in tasks.json and its task file.
- [ ] Every DoD box of T128–T130 that holds is ticked; the tracker's T23 row and T23's deps include T128–T131.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
