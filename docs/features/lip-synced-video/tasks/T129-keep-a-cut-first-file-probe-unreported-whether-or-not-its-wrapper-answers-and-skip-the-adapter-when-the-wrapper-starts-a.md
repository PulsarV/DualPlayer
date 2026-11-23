---
id: T129
title: "Keep a cut first-file probe unreported whether or not its wrapper answers, and skip the adapter when the wrapper starts after a give-up"
layer: "app"
deps: ["T128"]
blocks: ["T130", "T131", "T23"]
acs: ["AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourWorkerTimingTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23b (Z1, Z5)"
---

<!-- Follow-up task from review-2026-11-23b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23b.md) · repo `CLAUDE.md`. Do not guess. -->

# T129 — Keep a cut first-file probe unreported whether or not its wrapper answers, and skip the adapter when the wrapper starts after a give-up

## Why

- **Z1.** When the first file of a press had the whole budget and its listing cut the probe limit (`wholeBudget && cut`), a wrapper still unanswered at limit + 50 ms returns `(FileSkipped, false)` (printed `skipped … unreadable`, `checked 1`), while the same file whose adapter answers `probe_timeout` at that limit returns `(null, true)` (nothing printed, `checked 0`). Output depends on a ~50 ms race; this contradicts AC-08 ("a file whose probe was cut short by the budget is not reported as skipped") and T123's DoD box 1. Cites: `PlaybackSession.cs:935, 960-970` vs `:994-998`. Owner decision: in the unanswered branch report a skip only when `!cut`; when `cut`, return `(null, true)` with `timedOut = wholeBudget`, as at `:994-998`. Fix the comment at `:960-963`.
- **Z5.** A wrapper given up on before it entered still calls `_pictureProbe.Probe(path, probeLimit, cancel)` with the already-cancelled token (`PlaybackSession.cs:923-926`); the adapter (`FfmpegPictureProbe.cs:67-83`) registers a `_running` worker and starts `ProbeCore`, whose file `open()` is not guarded by the interrupt callback. On a dead SMB share the thread blocks, `IsBusy` holds, and later presses step past the file. The current test (`PlaybackSessionNeighbourWorkerTimingTests.cs:963-964`) does not assert the adapter was not called.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b.

## Definition of Done

- [ ] In the unanswered branch the session reports a skip only when the limit was not cut; a cut probe returns `(null, true)` with `timedOut = wholeBudget`, the same as an adapter answer at the limit; the comment at `:960-963` says so.
- [ ] Test: a first file with a 1.2 s listing and a wrapper that never answers → no `FileSkipped`, the press ends `NeighbourSearchTimedOut`, and the file is remembered only per the whole-budget rule. Fails if reverted.
- [ ] Before marking itself entered, the wrapper checks `cancel.IsCancellationRequested` and returns a failure without calling the adapter.
- [ ] Test: a wrapper whose thread starts after the press gave up → the picture probe's `Probe` is never called for that file. Fails if reverted.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
