---
id: T123
title: "Give up on a probe wrapper that has not answered when the press bound is reached, without judging or remembering its file"
layer: "app"
deps: ["T122"]
blocks: []
acs: ["AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourWorkerTimingTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23 (Y1)"
---

<!-- Follow-up task from review-2026-11-23. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23.md) · repo `CLAUDE.md`. Do not guess. -->

# T123 — Give up on a probe wrapper that has not answered when the press bound is reached, without judging or remembering its file

## Why

- **Y1** (regression from X12). When the probe wrapper enters more than about 100 ms late but before the backstop (scanAt + 3.05 s), the backstop breaks first (`src/DualPlayer.Core/Playback/PlaybackSession.cs:916-932`); `probeStart` becomes `enteredAt`, so `usedUp` is false (`:959-979`). The token is never cancelled (the CTS is disposed while the probe holds it), and the synthetic `video.probe_timeout` falls through to `FileSkipped("unreadable")`: stored in `_judged`, published as skipped, counted as judged (the press can end `NoNeighbour`), and the file is never probed again this film. A wrapper that never enters is remembered as timed out though the adapter never ran. spec.md:249: a file cut only because earlier files spent the budget is neither remembered nor reported as skipped.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [ ] When the probe has not completed after the wait loop, the session treats it as a give-up: it always cancels the worker (with the grace) and returns `(null, true)` (expired); the file is neither judged nor reported as skipped.
- [ ] The file is remembered as timed out only if the wrapper entered and ran its full limit (`elapsed(enteredAt) + slack >= probeLimit`) and the limit was not cut, or it had the press's whole budget.
- [ ] Test: a 1.0–1.5 s listing, then the wrapper held back ~200 ms of fake time on a cut probe → no `FileSkipped` for that file, the press ends `NeighbourSearchTimedOut` (not `NoNeighbour`), and the next press probes that file again. Fails if reverted.
- [ ] Test: a wrapper that never enters before the press bound → the file is not remembered as timed out (the next press probes it).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
