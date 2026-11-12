---
id: T58
title: "Rewind or fail after a failed restore seek, and test it for real"
layer: "integration"
deps: []
blocks: []
acs: ["AC-01", "AC-09"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs", "src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs", "tests/DualPlayer.Media.Tests/Audio/FfmpegAudioDecoderTests.cs", "tests/DualPlayer.Media.Tests/TestMedia/README.md", "tests/DualPlayer.Media.Tests/TestMedia/ramp-offset-nocues.mkv"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12-r5 (U1, U3)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 5. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r5.md) · repo `CLAUDE.md`. Do not guess. -->

# T58 — Rewind or fail after a failed restore seek, and test it for real

## Why

U1 (stage-1, AC-01/AC-09): `RestoreStart` (`FfmpegDecodeSession.cs:213-226`) cannot rewind. The ranged retry `avformat_seek_file(_fmt,-1,MIN,_startTime,_startTime,0)` falls back to the same `av_seek_frame(_startTime, BACKWARD)` that just failed (Matroska has no `read_seek2`; `max_ts == ts`, so FFmpeg makes no second attempt). The byte fallback passes `_startTime` (AV_TIME_BASE µs) as a byte position: clamped to EOF on a small file, mid-file on a large one. A scratch probe (Homebrew FFmpeg 8.1) shows even `avio_seek(pb,0,0)` + a byte seek to 0 yields 0 packets once the Matroska demuxer has hit EOF. Both paths return `Success`, so the first `Read` is end of stream and the run ends `FileEnd` silently — what T57's DoD forbids. The `RestoreSeekHook` seam replaces only the first seek, so the fallback and the `Failure` branch never run in tests.
U3 (stage-2): `ramp-offset-nocues.mkv` does not make the restore fail — FFmpeg indexes keyframes read during the duration probe; rc=0, first pts 3000 ms; the test (`FfmpegAudioDecoderTests.cs:305-322`) passes on the old code. `TestMedia/README.md:52-54` claims the seek fails (false) and its regex `rb'\xb3\x82..'` lacks `re.DOTALL`.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-09.

## Definition of Done

- [ ] When the restore seek fails, either reopen the input (close + `OpenInput` again, skip the duration probe, keep the computed duration, rebuild the outputs) and play from content 0, or return `Result.Failure(FfmpegAudioDecoder.Unreadable, …)` — never `Success` with the demuxer at EOF.
- [ ] The ranged retry that repeats the failed seek and the byte-seek branch are removed (or a fallback is kept only if it is verified, e.g. by reading a packet whose pts is near `_startTime`).
- [ ] The test seam can make every rewind attempt fail; a test using it asserts playback from content 0 (first block StartFrame ≈ 0, ramp sample ≈ 0) or `playback.file_unreadable`, and fails on the current code (silent end of stream).
- [ ] `ramp-offset-nocues.mkv` and its test are removed, or replaced by an asset that really makes the restore fail; README corrected either way (no false claim, regex fixed if kept).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
