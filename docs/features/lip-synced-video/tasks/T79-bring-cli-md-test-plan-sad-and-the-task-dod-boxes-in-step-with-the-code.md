---
id: T79
title: "Bring cli.md, test-plan, sad and the task DoD boxes in step with the code"
layer: "docs"
deps: ["T70", "T71", "T72", "T73", "T74", "T75", "T76", "T77", "T78"]
blocks: []
acs: ["AC-01", "AC-17"]
files_hint: ["docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/tasks/", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21d (R7, R12)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T79 — Bring cli.md, test-plan, sad and the task DoD boxes in step with the code

## Why

R7: cli.md §3 lacks `LipSyncedAgain` and `DamagedStretchSkipped`, though both are written to the log (`SessionEventLogWriter.cs:96-104, 114-120`) and the T23 guide judges L1/L2 on `LipSyncedAgain` (`t23-acceptance-run-guide.md:62-63`; `cli.md:97-107`).

R12: `test-plan.md:75` still says the audio decoder reports an OS refusal as unreadable (changed by T66); `test-plan.md:130` labels a `[HardwareFact(HardwareOs.MacOS)]` test as unit; `sad.md:541` hardening list omits `keep-open=yes`, though `MpvVideoRendererTests.cs:92` calls it "exactly the sad list" (and `force-window=yes` from T67). The T13 DoD (`tasks/T13-…md:157`) attributes the out-of-zone edits to T60–T68; they came from T29 (8f321c8), T41 (ec8f28a), T55 (bee2716). Tasks T1–T22 are `done` with every DoD box unticked; `T11-…md:109` and `T12-…md:129` ("no file in Core/Outputs/ or Core/Diagnostics/ changed") are false; `T22-…md:115` (manual dev-Mac run) has no evidence.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-17.

## Definition of Done

- [ ] cli.md §3 has rows for `LipSyncedAgain` (clock, command, ms after command, picture error, per-output errors) and `DamagedStretchSkipped` (position, length, frames), matching `SessionEventLogWriter`.
- [ ] test-plan row 75 is rewritten for T66; row 130 is marked integration/hardware; rows for T70–T78's tests are added with the right class names and ACs.
- [ ] sad.md §8 hardening list matches the renderer's options (incl. `keep-open=yes`, `force-window=yes`).
- [ ] T13's DoD names T29, T41 and T55.
- [ ] In T1–T22 the DoD boxes that hold are ticked; T11:109 and T12:129 are resolved with a pointer to sad.md §2 (as T13); T22:115 is folded into the T23 checklist and marked so.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
