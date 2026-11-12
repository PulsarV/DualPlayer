---
id: T57
title: "Check the restore seek after the Matroska duration probe"
layer: "integration"
deps: ["T56"]
blocks: []
acs: ["AC-01", "AC-09"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs", "tests/DualPlayer.Media.Tests/Audio/FfmpegAudioDecoderTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r4 (T6)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 4. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r4.md) · repo `CLAUDE.md`. Do not guess. -->

# T57 — Check the restore seek after the Matroska duration probe

## Why

T6 (AC-01, AC-09): for Matroska with a non-zero start offset, `Init` seeks to the Duration timestamp and reads on, then seeks back to `_startTime` ignoring the return code (`FfmpegDecodeSession.cs:85`). If that seek fails (e.g. no cues) the demuxer stays at the end: the first `Read` returns end of stream and the run ends `FileEnd` without playing.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-09.

## Definition of Done

- [ ] The restore seek's return code is checked; on failure fall back (e.g. byte seek to the data offset / `avformat_seek_file`) or open fails with `Result.Failure` (unreadable) — never a silent `FileEnd`.
- [ ] Test covers the failure path (a seam forcing the restore seek to fail, or a cue-less offset MKV) and asserts playback starts or an unreadable failure — not an immediate end of stream.
- [ ] Existing offset/span MKV tests stay green.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
