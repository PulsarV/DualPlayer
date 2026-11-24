---
id: T77
title: "Load FFmpeg before mapping an audio open failure so the mapping tests run alone"
layer: "integration"
deps: []
blocks: []
acs: ["AC-09b", "AC-03"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs", "tests/DualPlayer.Media.Tests/Audio/FfmpegAudioDecoderTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21d (R10)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T77 — Load FFmpeg before mapping an audio open failure so the mapping tests run alone

## Why

R10: `FfmpegAudioDecoder.OpenFailure` calls `ErrorText` → `av_strerror` before `FfmpegLibrary.Load()` (`FfmpegAudioDecoder.cs:91-92, 131`). Run in isolation (`--filter "FullyQualifiedName~FfmpegAudioDecoderTests.An_open_failure|FullyQualifiedName~FfmpegAudioDecoderTests.Any_other_open_failure"`), both theories fail with `NotSupportedException` (confirmed in review); they pass in the full suite only because another test loads FFmpeg first (`FfmpegAudioDecoderTests.cs:20, 31`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b, AC-03.

## Definition of Done

- [x] `OpenFailure` (or the path that builds the error text) ensures FFmpeg is loaded before calling `av_strerror`.
- [x] The two theories pass when run alone with the filter above (RED is that filtered run failing).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
