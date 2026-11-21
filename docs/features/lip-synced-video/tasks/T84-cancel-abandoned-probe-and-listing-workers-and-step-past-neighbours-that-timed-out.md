---
id: T84
title: "Cancel abandoned probe and listing workers and step past neighbours that timed out"
layer: "integration"
deps: ["T83"]
blocks: []
acs: ["AC-07", "AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/IAudioDecoder.cs", "src/DualPlayer.Core/Playback/", "src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs", "src/DualPlayer.Media/", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Media.Tests/", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/sad.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22 (P7)"
---

<!-- Follow-up task from review-2026-11-22. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22.md) · repo `CLAUDE.md`. Do not guess. -->

# T84 — Cancel abandoned probe and listing workers and step past neighbours that timed out

## Why

P7 (rest of R3): workers abandoned at the deadline — the `ListTracks` worker (`PlaybackSession.cs:625-631`) and the `FfmpegPictureProbe` worker (`FfmpegPictureProbe.cs:32-38`) — keep running: no cancellation reaches FFmpeg, so a hung `avformat_open_input` keeps its file open (on Windows this blocks rename/delete) and its thread busy. Each later Page Down rescans the same slow files and starts new workers on them; with `PictureProbeLimit` = 2 s (`:55`) and a 3 s budget, two hanging neighbours in a row make Page Down report a timeout forever, so the user can never get past them.

Owner decision: an `AVIOInterruptCB` (or a `CancellationToken` the interrupt callback checks) for both workers, tripped when the session gives up; remember the session's timed-out files and step past them on the next press.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08, AC-09b.

## Definition of Done

- [x] `ListTracks` and the picture probe accept a cancellation token; the FFmpeg calls install an interrupt callback that checks it, so a cancelled open/probe returns promptly and closes the file.
- [x] The session cancels the in-flight worker when it gives up on a candidate (budget or probe limit).
- [x] The session remembers files that timed out during this session and, on the next press in the same direction, steps past them (they count as checked); a file is retried only after the remembered set is cleared on a successful file change — or per the rule stated in spec §6.1 / cli.md §2.2.
- [x] Tests: two hanging neighbours in a row → the second press reaches the third file; a cancelled fake worker is observed cancelled; a Media test shows an interrupted open returns (on a blocking custom AVIO or slow pipe).
- [x] spec §6.1, cli.md §2.2 and sad.md describe the cancel + step-past rule.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
