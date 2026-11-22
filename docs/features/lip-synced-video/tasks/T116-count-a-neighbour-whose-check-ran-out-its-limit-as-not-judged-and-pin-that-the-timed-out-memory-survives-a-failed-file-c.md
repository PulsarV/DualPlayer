---
id: T116
title: "Count a neighbour whose check ran out its limit as not judged, and pin that the timed-out memory survives a failed file change and a return"
layer: "app"
deps: ["T115"]
blocks: []
acs: ["AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/contracts/cli.md"]
status: "todo"
step: ""
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22f (X1, X13)"
---

<!-- Follow-up task from review-2026-11-22f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22f.md) · repo `CLAUDE.md`. Do not guess. -->

# T116 — Count a neighbour whose check ran out its limit as not judged, and pin that the timed-out memory survives a failed file change and a return

## Why

- **X1** (all three reviewers). `judgedAny |= !steppedPast` (`PlaybackSession.cs:576`) counts a file as judged when its probe ran out its full 2 s limit, but such a file goes to `_timedOut` (`:752-759`), never `_judged`. E2 the only neighbour, its probe times out every time (the adapter's worker ends on cancel, so `IsBusy` is false): presses 1 and 2 end `NoNeighbour` / `no-next-file` (`:611`); press 3, after the NAS recovers, plays E2. spec.md:249 says a press whose skips were remembered files or live-worker files judged nothing and ends `next-file-timeout  checked N`. cli.md:85 and sad.md:542 mention only live-worker files.
- **X13**. The T108 memory tests (`PlaybackSessionNeighbourMemoryTests.cs:144-183`) check only a `_judged` verdict; putting `_timedOut.Clear()` back into `StartCore` would pass them.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [ ] A skip counts as judged only when it reached or reused a verdict (`judgedAny |= !steppedPast && !timedOut`, with `timedOut` returned from the check). A press whose every skip was a step-past or a check that ran out its limit ends `NeighbourSearchTimedOut` (`checked N`, the timeout note), never `NoNeighbour`; the skipped files are still reported `skipped … unreadable`.
- [ ] Test: the only neighbour's probe runs out its full limit on press 1 and again on press 2 (a fake that ends on cancel, `IsBusy` false) → both presses publish `NeighbourSearchTimedOut` and no `NoNeighbour`. Fails if the fix is reverted.
- [ ] Tests: E2 remembered as timed out, then (a) a file change whose `Load` fails and (b) a ← return after every output was lost → the next press still checks E2 last. Fail if `_timedOut.Clear()` goes back into `StartCore`.
- [ ] cli.md §2.2 (:85) and sad.md §7 (:542) say, like spec.md:249, that a press whose skips were remembered files or files whose worker is still running ends `next-file-timeout  checked N`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
