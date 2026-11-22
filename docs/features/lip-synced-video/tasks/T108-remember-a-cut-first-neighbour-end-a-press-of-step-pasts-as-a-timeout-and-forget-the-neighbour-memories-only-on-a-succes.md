---
id: T108
title: "Remember a cut first neighbour, end a press of step-pasts as a timeout, and forget the neighbour memories only on a successful file change"
layer: "app"
deps: []
blocks: []
acs: ["AC-07", "AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/contracts/cli.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22e (W1, W2, W5)"
---

<!-- Follow-up task from review-2026-11-22e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22e.md) · repo `CLAUDE.md`. Do not guess. -->

# T108 — Remember a cut first neighbour, end a press of step-pasts as a timeout, and forget the neighbour memories only on a successful file change

## Why

- **W1** (two reviewers, checked in code; rest of V2). The listing-cut exit still sets `timedOut = listingUncut` (`PlaybackSession.cs:800`), and `listingUncut = remaining >= PictureProbeLimit` (`:793`) is measured from `scanAt`, so the folder enumeration and sort still count against the first file. Folder work 1.2 s + an E2 listing of 2.5 s: E2 is the first file checked, is cut, is not remembered; its worker completes, the next press checks E2 first again, and every press ends `next-file-timeout  checked 0`. spec.md:249 and cli.md:85 say the first file checked in the press is remembered.
- **W2** (two reviewers, checked in code). A press whose only skips were live-worker step-pasts (`:770-775`) or `video.probe_busy` answers (`:859-864`) publishes `NoNeighbour` and the note "No next file (skipped 1; last: Episode-02.mkv - file cannot be read)" (`:607-614`), and the CLI prints `no-next-file`. spec.md:249 says such a file is not judged and that a press reports no file nobody judged. E2 the only neighbour, NAS down at press 2: the window says there is no next file, press 3 plays E2. `PlaybackSessionNeighbourRecoveryTests.cs:69-70` pins the wrong outcome.
- **W5** (checked in code). `_timedOut` and `_judged` are cleared inside `StartCore` (`:293-294`), reached from a file change whose `Load` or engine start then fails, and from ← after every output was lost (`ReturnIntoFilm` → `StartCore`, `:372`), which is not a file change. A file judged `not-hardware-decodable` is then probed again and spends up to 2 s, against spec.md:249 ("spends no budget again"; both memories forgotten "on the next successful file change").

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08, AC-09b.

## Definition of Done

- [ ] The listing-cut exit sets `timedOut = listingUncut || wholeBudget`, so a first file of the press whose listing is cut by the budget is remembered however long the folder work took.
- [ ] When every skip of a press was a step-past (a live listing/probe worker, or `video.probe_busy`) and no file was judged, the press ends with the not-found-within-3-s outcome (`NeighbourSearchTimedOut`, `checked N`, the timeout note), not `NoNeighbour` / `no-next-file`; the stepped-past files are still reported as `skipped … unreadable` and counted. A press that judged at least one file unplayable and found no target still ends `no-next-file`.
- [ ] `_timedOut` and `_judged` are cleared only after a successful file change (after `StartCore` succeeds in the file change; and at the first `Start`), not inside `StartCore`: a failed file change and a ← return into the same film keep both memories.
- [ ] Tests: (1) a folder that costs 1.2 s of fake time and an E2 listing of 2.5 s → E2 remembered, the next press reaches E3; (2) E2 the only neighbour with its listing blocked through press 2 → press 2 publishes `NeighbourSearchTimedOut`, no `NoNeighbour`; press 3 after release plays E2 (update RecoveryTests :69-70); (3) B judged `not-hardware-decodable`, every output lost, ← return, next press does not probe B again; (4) a file change whose `Load` fails keeps the memories. Each fails if its fix is reverted.
- [ ] spec.md:249, sad.md §7 (:538) and cli.md §2.2 (:85) say that a press that only stepped past files ends as the timeout outcome (`next-file-timeout  checked N`), and that the memories survive a failed file change and a return.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
