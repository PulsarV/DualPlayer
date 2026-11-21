---
id: T69
title: "Bring test-plan, contracts, sad, roadmap, tasks and the T23 guide in step with the code"
layer: "docs"
deps: ["T60", "T61", "T62", "T63", "T64", "T65", "T66", "T67", "T68"]
blocks: []
acs: []
files_hint: ["docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/sad.md", "docs/roadmap.md", "docs/features/lip-synced-video/tasks.json", "docs/features/lip-synced-video/tasks/T13-playback-session-start-end-exit.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21c (S11, S15, S16, S18, S19)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T69 — Bring test-plan, contracts, sad, roadmap, tasks and the T23 guide in step with the code

## Why

S11: test-plan rows 113-114 name `PlaybackSessionLipSyncedOutputsTests` (tests are in `PlaybackSessionRecoveryBudgetTests`); row 111 names `PlaybackSessionLogRecordsTests` for `NotLipSynced` (it is in `SessionEventLogWriterTests`); row 116 tags the flush AC-12 (AC-10); row 117 tags the device-open check AC-03/AC-15 (T55 claims AC-01/AC-12); row 50 and line 122 do not link `PlayEndToEndTests.Space_through_the_real_toggle…`.

S15: step 4 edits `Core/Diagnostics/` (`EngineEvent.cs:12-16`, `RunSummaryBuilder.cs`), adds `Core/Outputs/IDeviceAccessProbe.cs`/`BackendDeviceAccessProbe.cs`, and changes `AudioEngine` beyond the lag hooks, while `sad.md:48, 122, 128, 138` and `roadmap.md:120` reserve those for steps 5 and 6; T13's DoD box (`T13-…md:157`) is unticked in a done task.

S16: `previous <file>` is printed (`TerminalPrinter.cs:123`) but cli.md §2.2 / `screens.md:36` define only `next`.

S18: T20 `acs: ["AC-01"]` and T31 `acs: ["AC-08","AC-15"]` claim fewer ACs than they build (AC-04, AC-05, AC-06b, AC-07, AC-09, AC-09b, AC-10, AC-11, AC-15b).

S19: the T23 guide line 48 says `PictureDelay` (record is `PictureDelayChanged`); L1 judges the first frame from the first `PictureTiming`, which comes from the 1 s health tick.

## Acceptance criteria

No AC of its own: none (gate reliability / docs).

## Definition of Done

- [ ] test-plan rows cite the right classes and ACs, the AC-04 row links the Space E2E test, and the rows for T60–T68's tests are added.
- [ ] cli.md §2.2 and SCR-01 list `previous <file name>`.
- [ ] sad.md §2/§5/§11 and the roadmap wave-3 zone line record the step-4 edits to `Core/Diagnostics/`, `Core/Outputs/` and `AudioEngine`, with a note for steps 5 and 6; T13's DoD box is resolved with a pointer to that record.
- [ ] T20's and T31's `acs` list the ACs they build.
- [ ] The T23 guide uses `PictureDelayChanged` and judges L1 on `LipSyncedAgain`/`NotLipSynced` with command `start`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
