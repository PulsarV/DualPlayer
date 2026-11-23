---
id: T143
title: "Pin the plain-words no-picture message of the picture probe"
layer: "test"
deps: []
blocks: ["T144", "T23"]
acs: ["AC-02"]
files_hint: ["tests/DualPlayer.Media.Tests/Video/FfmpegPictureProbeTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23f (W2)"
---

<!-- Follow-up task from review-2026-11-23f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23f.md) · repo `CLAUDE.md`. Do not guess. -->

# T143 — Pin the plain-words no-picture message of the picture probe

## Why

- **W2.** AC-02: the Developer is told "in plain words that the file has no picture". `FfmpegPictureProbeTests.cs:29-35` checks only the code `video.no_picture`; the message at `FfmpegPictureProbe.cs:164` is not pinned.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02.

## Definition of Done

- [ ] `FfmpegPictureProbeTests.Probe_reports_no_picture_for_a_file_with_audio_only` asserts the message contains `"has no picture"`; with the text at `FfmpegPictureProbe.cs:164` changed (scratch copy) it fails.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
