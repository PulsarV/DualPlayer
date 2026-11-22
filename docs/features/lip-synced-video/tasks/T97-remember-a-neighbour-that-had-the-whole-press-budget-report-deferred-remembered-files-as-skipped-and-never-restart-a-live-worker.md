---
id: T97
title: "Remember a neighbour that had the whole press budget, report deferred remembered files as skipped, and never restart a live worker"
layer: "app"
deps: []
blocks: []
acs: ["AC-07", "AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/contracts/cli.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22c (U2, U3, U4, U5)"
---

<!-- Follow-up task from review-2026-11-22c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22c.md) · repo `CLAUDE.md`. Do not guess. -->

# T97 — Remember a neighbour that had the whole press budget, report deferred remembered files as skipped, and never restart a live worker

## Why

Three gaps in the T88 neighbour scan, plus the docs that describe it.

- **U2** (checked in code). A healthy neighbour checked first, whose listing plus probe need more than 3 s, is never judged or remembered. Example: listing 1.2 s, then the probe needs 1.9 s and has 1.8 s left. `cut && usedUp` returns `(null, true)` (`PlaybackSession.cs:790-795`); a listing alone over 2.9 s hits `:771-775`. So every press ends `next-file-timeout checked 0` and no file after it is ever reached.
- **U3.** A remembered file is passed over silently. Press 1 times out on E2 and prints no skip line. Press 2 checks E2 last and plays E3 with `skipped 0` (`:538-561, 663-682`). AC-09b requires "which file was skipped and why", and cli.md:85 says remembered files are printed as `skipped … reason unreadable` and counted in `checked N`.
- **U4.** A remembered file is re-checked on every press when nothing else is left, and each check starts a fresh worker (`:742, :780`). On a dead share, N presses leave N pool threads blocked in the kernel, which contradicts spec §6.1 (:249) and sad §7 (:526) ("the memory bounds how many such threads can pile up").
- **U5** (docs). sad.md:526 ends with the T84 rule ("remembers the files it gave up on and steps past them … cleared on a successful file change"). spec.md:247 still promises "no key goes unanswered for longer than 3 s", but :249 counts the budget from the start of the command.

Owner decision: fix now, all together.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08, AC-09b.

## Definition of Done

- [ ] A file whose check started with the press's whole budget (no earlier file in this press spent any) and still could not be judged is remembered as timed out (same `NeighbourKey`), whether the listing or the probe ran out; only a file cut short because earlier files spent the budget stays unremembered and unreported.
- [ ] When the target is chosen after remembered files that come before it in scan order were deferred, each such file is published as `FileSkipped(name, "unreadable")`, logged and counted in `checked N`; the terminal and the note show it as for any skip.
- [ ] The abandoned worker `Task` (listing or probe) is kept per `NeighbourKey`; while it has not completed, a later press steps past that file (counted as checked, reason `unreadable`) without starting a new worker, and retries only once the task completed or the key changed.
- [ ] Tests: listing 1.2 s + probe 1.9 s on E2 → press 2 reaches E3 (and reports E2 skipped); a remembered E2 deferred before E3 → press 2 publishes `FileSkipped(E2, unreadable)` and `checked` counts it; a listing that never returns + three presses → exactly one `ListTracks` call for E2.
- [ ] spec §6.1 (:247 and :249), sad.md §7 (:526) and cli.md §2.2 (:85) state the final rule: what is remembered, that the 3 s budget counts from the start of the command plus any queue wait, that remembered files are reported as skipped, and that a live worker is not restarted (so at most one blocked thread per file).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
