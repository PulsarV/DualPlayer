---
id: T56
title: "Label a source 5.1 or 7.1 only when it has an LFE channel"
layer: "integration"
deps: []
blocks: []
acs: ["AC-02", "AC-12"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs", "tests/DualPlayer.Media.Tests/Audio/FfmpegAudioDecoderTests.cs", "tests/DualPlayer.Media.Tests/TestMedia/README.md", "tests/DualPlayer.Console.Tests/StartLinesTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r4 (T4)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 4. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r4.md) · repo `CLAUDE.md`. Do not guess. -->

# T56 — Label a source 5.1 or 7.1 only when it has an LFE channel

## Why

T4 (AC-02, AC-12): `ToLayout` (`FfmpegAudioDecoder.cs:38,128-135`) picks the layout from the channel count, so hexagonal 6.0 (no LFE) becomes `Surround51`: `tracks` and the start line print "5.1", and on a 5.1 device `ConsoleFormat.TrackConversion` shows no conversion although Media maps different channels. `TestMedia/README.md:45` says 6.0 → `ChannelLayout.Other`; `StartLinesTests.cs:74` case `(6, false) -> "eng other -> stereo"` builds a track Media never reports.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02, AC-12.

## Definition of Done

- [ ] `Surround51`/`Surround71` are reported only when the source has an LFE (or derived from the channel mask); 6.0 reports `Other` with 6 channels and no LFE.
- [ ] Media test with `tones-6.0.wav` asserts the reported layout/channels/LFE; README matches what Media reports.
- [ ] The console start-line case for 6.0 matches the track Media actually reports.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
