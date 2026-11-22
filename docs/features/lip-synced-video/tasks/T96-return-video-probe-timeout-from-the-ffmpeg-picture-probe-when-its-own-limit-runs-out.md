---
id: T96
title: "Return video.probe_timeout from the FFmpeg picture probe when its own limit runs out"
layer: "integration"
deps: []
blocks: []
acs: ["AC-07", "AC-08", "AC-09b", "AC-02"]
files_hint: ["src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "src/DualPlayer.Core/Playback/IPictureProbe.cs", "src/DualPlayer.App/Startup/StartCommand.cs", "tests/DualPlayer.Media.Tests/", "tests/DualPlayer.App.Tests/Startup/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22c (U1)"
---

<!-- Follow-up task from review-2026-11-22c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22c.md) · repo `CLAUDE.md`. Do not guess. -->

# T96 — Return video.probe_timeout from the FFmpeg picture probe when its own limit runs out

## Why

**U1** (checked in code). When the worker's limit runs out, the real `FfmpegPictureProbe` returns `video.not_hardware_decodable` (`src/DualPlayer.Media/Video/FfmpegPictureProbe.cs:57-70`), never `video.probe_timeout`. The session counts a neighbour as timed out only on `video.probe_timeout` (`PlaybackSession.cs:788, 798`), so a slow neighbour on the real PC is judged `not-hardware-decodable` ("picture cannot be played on this PC") for the rest of the film and is never retried. Spec §6.1 and cli.md §2.2 promise instead that it is remembered, checked last and printed `reason unreadable`. Only test fakes return `video.probe_timeout`; `IPictureProbe` (`:9-11`) does not document it, and the real-adapter test (`tests/DualPlayer.Media.Tests/AbandonedWorkerTests.cs:55-67`) does not check the code.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08, AC-09b, AC-02.

## Definition of Done

- [ ] When the probe's own limit runs out before the first picture is decoded (`worker.Wait` false), `FfmpegPictureProbe` returns `video.probe_timeout`, with `codec`/`profile` in `Error.Fields` when already known; a picture that decoded but not in hardware still returns `video.not_hardware_decodable`.
- [ ] `IPictureProbe`'s doc comment lists `video.probe_timeout` and when it is returned.
- [ ] At start (`StartCommand.Validate`), `video.probe_timeout` is refused exactly as cli.md §5 refuses `video.not_hardware_decodable` (same exit code and message class), so the AC-02 start refusal is unchanged; the session's `ReasonOf` maps `video.probe_timeout` to `unreadable`.
- [ ] Media test on the stalled pipe asserts the returned code is `video.probe_timeout`; App test asserts the start refusal for a probe returning `video.probe_timeout`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
