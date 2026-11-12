---
id: T34
title: "Subtract the stream start time so files whose timestamps start above 0 play and seek correctly"
layer: "integration"
deps: []
blocks: []
acs: ["AC-01", "AC-09", "AC-10"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs", "tests/DualPlayer.Media.Tests/Audio/FfmpegAudioDecoderTests.cs", "tests/DualPlayer.Media.Tests/TestMedia/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12-r2 (R1)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T34 — Subtract the stream start time so files whose timestamps start above 0 play and seek correctly

## Why

R1 (review-2026-11-12-r2): `Anchor` derives `StartFrame` from the absolute `best_effort_timestamp`, `av_seek_frame` gets an absolute target and `Duration` is relative; `fmt->start_time` is never used. Files starting above 0 (MPEG-TS/M2TS ~1.4 s, MKV with `-output_ts_offset`) start with a hold equal to the offset, seek to target − offset, and a seek near the end is treated as past the end while audio remains. Regression from T24.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-09, AC-10.

## Definition of Done

- [ ] Read `start_time` once (ignored when `AV_NOPTS_VALUE`); subtract it, rescaled to the output rate, in `Anchor`; add it to the `av_seek_frame` target; the past-end decision compares against the relative timeline.
- [ ] Media tests on a small committed TS file and an MKV made with `-output_ts_offset` (README updated): first block starts at frame 0 (± swr delay); a seek to 5 s yields content at 5 s; a seek just before the end is not past the end.
- [ ] Files starting at 0 unchanged (existing tests green).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
