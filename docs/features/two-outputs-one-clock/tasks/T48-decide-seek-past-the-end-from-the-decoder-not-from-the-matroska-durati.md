---
id: T48
title: "Decide seek past the end from the decoder, not from the Matroska duration"
layer: "integration"
deps: []
blocks: []
acs: ["AC-09", "AC-10"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs", "tests/DualPlayer.Media.Tests/", "src/DualPlayer.Core/Playback/AudioEngine.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r3 (S6)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T48 — Decide seek past the end from the decoder, not from the Matroska duration

## Why

S6 (review-2026-11-12-r3): The Matroska duration rule assumes Duration is the end timestamp (FFmpeg muxer); mkvmerge writes the span, and linked split parts have start_time > 0, so a valid seek is treated as past the end and ends the run.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09, AC-10.

## Definition of Done

- [ ] The session no longer refuses a seek because of `Duration`; past-the-end is decided by the decoder having no audio at the target (engine path).
- [ ] `Duration` stays usable for the end clamp; a Matroska file whose Duration is the span reports the right length (or the clamp tolerates it).
- [ ] Media test with an mkvmerge-style MKV (start offset, span Duration) seeking near the end plays audio; the existing offset tests stay green.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
