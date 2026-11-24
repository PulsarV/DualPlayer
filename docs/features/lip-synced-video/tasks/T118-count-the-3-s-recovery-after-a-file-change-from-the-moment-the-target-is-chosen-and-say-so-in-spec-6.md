---
id: T118
title: "Count the 3 s recovery after a file change from the moment the target is chosen, and say so in spec \u00a76"
layer: "app"
deps: ["T117"]
blocks: []
acs: ["AC-07", "AC-09", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/tasks/T23-measure-lip-sync-on-the-target-pc.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22f (X5)"
---

<!-- Follow-up task from review-2026-11-22f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22f.md) · repo `CLAUDE.md`. Do not guess. -->

# T118 — Count the 3 s recovery after a file change from the moment the target is chosen, and say so in spec §6

## Why

- **X5**. spec §6 (spec.md:235) holds a file change to "lip-synced again within 3 s, counted from the key's arrival", while §6.1 (spec.md:249) lets the neighbour scan alone spend 3 s. The code starts the file-change recovery clock at the key (`_resync = ("file-change", commandAt)`, `PlaybackSession.cs:691`). A Page Down that spends more than about 1 s skipping slow neighbours, plus the ~2 s pre-roll, logs `NotLipSynced` (file-change) though it behaves as designed. T23 L2/L4 use healthy neighbours, so acceptance never sees it.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-09, AC-09b.

## Definition of Done

- [x] Owner decision: the file-change recovery is counted from the moment the target is chosen (`FileChanged`), not from the key. spec §6 (spec.md:235 row) says so, and notes that with no skips the two moments are the same.
- [x] `_resync` for a file change takes the time the target was chosen. Test: a press that spends 2 s of fake time skipping a slow neighbour, then a normal pre-roll → no `NotLipSynced` (file-change); a target chosen at once but a recovery longer than 3 s → still `NotLipSynced`. Fails if reverted.
- [x] sad.md (the recovery/quality row that names the 3 s file-change recovery) and the T23 run guide's file-change threshold say the same.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
