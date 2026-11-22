---
id: T107
title: "List the new start error codes in cli.md, redraw sad flow 1 in the code's check order, and bring test-plan and the tracker in step"
layer: "docs"
deps: ["T102", "T103", "T104", "T105", "T106"]
blocks: []
acs: ["AC-02", "AC-03b", "AC-09b"]
files_hint: ["docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/tasks/tracker.md", "docs/features/lip-synced-video/tasks/T23-measure-lip-sync-on-the-target-pc.md", "docs/features/lip-synced-video/tasks.json"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22d (V7, V11)"
---

<!-- Follow-up task from review-2026-11-22d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22d.md) · repo `CLAUDE.md`. Do not guess. -->

# T107 — List the new start error codes in cli.md, redraw sad flow 1 in the code's check order, and bring test-plan and the tracker in step

## Why

- **V7** (all three reviewers). Two codes reach stderr at start that cli.md §5 does not list: `video.probe_timeout` (T96; `StartCommand.Validate` passes it through, `StartCommandTests.cs:99-107`) and `mpv.load_timeout` (T98; via `StartErrors.AfterSessionStart`, `StartErrors.cs:34`). Row :133 still files a first picture that misses the start budget under `video.not_hardware_decodable`. An `unreadable` skip from a timed-out probe also prints `codec`/`profile` (`PlaybackSession.cs:837-842`, `TerminalPrinter.cs:106`), which cli.md §2.2 shows only for `not-hardware-decodable`. Owner decision: document both codes.
- **V11**. sad flow 1 (`sad.md:226-233`) checks the named screen before the picture probe; the code (`StartCommand.cs:79-121`) and cli.md:35 run probe → tracks → devices → screen → renderer library. A file with no picture and a missing `--screen` gets `video.no_picture`, not `start.screen_not_found`.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02, AC-03b, AC-09b.

## Definition of Done

- [ ] cli.md §5 has rows for `video.probe_timeout` and `mpv.load_timeout` (both exit 2, with when each is printed); row :133 no longer claims the start-budget case; the code list in sad.md (:542) includes both.
- [ ] cli.md §2.2 documents that an `unreadable` skip from a timed-out probe may carry `codec`/`profile` when they are known.
- [ ] sad flow 1 runs the checks in the code's order (probe → tracks → devices → screen → renderer library), matching cli.md §1.1.
- [ ] test-plan.md has a row for every test T102–T106 added; every DoD box of T102–T106 that holds is ticked; the tracker's T23 row and T23's deps include T102–T107.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
