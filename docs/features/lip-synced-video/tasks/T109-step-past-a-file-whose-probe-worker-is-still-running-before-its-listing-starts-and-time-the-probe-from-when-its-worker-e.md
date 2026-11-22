---
id: T109
title: "Step past a file whose probe worker is still running before its listing starts, and time the probe from when its worker enters the probe"
layer: "app"
deps: ["T108"]
blocks: []
acs: ["AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/IPictureProbe.cs", "src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "tests/DualPlayer.Core.Tests/Fakes/", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRecoveryTests.cs", "tests/DualPlayer.Media.Tests/Video/FfmpegPictureProbeTests.cs", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/sad.md"]
status: "blocked"
step: "implement — dep T108 blocked"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22e (W3, W7)"
---

<!-- Follow-up task from review-2026-11-22e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22e.md) · repo `CLAUDE.md`. Do not guess. -->

# T109 — Step past a file whose probe worker is still running before its listing starts, and time the probe from when its worker enters the probe

## Why

- **W3** (two reviewers, checked in code). The session checks only its own `_workers` (`PlaybackSession.cs:768-778`) before it starts a listing (`:794`); the adapter's busy probe worker (`FfmpegPictureProbe.cs:41-54`) shows up only after that listing, as `video.probe_busy` (`:859`). Press 1: E2's listing returns from cache, its probe blocks in `ReadFile`, the adapter returns `probe_timeout` at 2 s, the session keeps no worker. Press 2: the share is fully down, a new listing blocks on open. E2 holds two blocked threads, against spec.md:249, sad.md:538 ("at most one blocked thread per file") and the T102 DoD.
- **W7**. The session reads `probeStart` before `Task.Run` and gives up at `probeLimit + ProbeTimingSlack` (50 ms) (`:834-851`), while the adapter counts its limit from when the pool runs the wrapper. A pool delay over 50 ms (threads held by blocked listing workers, a GC pause) makes the session invent `video.probe_timeout` with no codec/profile; an uncut probe is then remembered and reported `unreadable` although the adapter was still inside its limit. Before T102 the probe ran synchronously.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b.

## Definition of Done

- [ ] Before it starts a listing, the session knows whether the probe adapter still has a live worker on that file (e.g. `IPictureProbe.IsBusy(path)`, or the adapter hands its live worker back and the session keeps it in `_workers`); such a file is stepped past without a listing, as a live listing worker is. At most one blocked thread per file, listing or probe.
- [ ] The probe wrapper does not depend on thread-pool scheduling for its time limit: it runs with `TaskCreationOptions.LongRunning` and/or the session counts the limit + slack from a signal raised once the wrapper has entered `Probe`; when the adapter's own result arrives it is used with its codec/profile.
- [ ] Tests: (1) press 1 E2's listing returns and its probe blocks (adapter returns `probe_timeout`); press 2 E2's listing would block → zero `ListTracks` calls on E2 in press 2, E2 stepped past; (2) a probe wrapper whose start is delayed by more than the slack (fake scheduler / gated start) → the adapter's own result (success, or `probe_timeout` with codec/profile) is used, not an invented timeout; (3) a Media test for the busy query on `FfmpegPictureProbe`. Each fails if its fix is reverted.
- [ ] spec.md:249 and sad.md:538 still state "at most one blocked thread per file, listing or probe" and now hold; reword only if the mechanism they name changed.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
