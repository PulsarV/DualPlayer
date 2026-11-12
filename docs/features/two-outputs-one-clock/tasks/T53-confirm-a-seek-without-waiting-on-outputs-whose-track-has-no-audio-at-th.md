---
id: T53
title: "Confirm a seek without waiting on outputs whose track has no audio at the target, counting only new-epoch frames"
layer: "app"
deps: ["T52"]
blocks: []
acs: ["AC-09", "AC-10", "AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Playback/DecodePump.cs", "src/DualPlayer.Core/Outputs/AudioRingBuffer.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12-r4 (T2, T3, T7)"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from re-review 4. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r4.md) · repo `CLAUDE.md`. Do not guess. -->

# T53 — Confirm a seek without waiting on outputs whose track has no audio at the target, counting only new-epoch frames

## Why

T2 (stage-1, AC-09): seek confirmation (`AudioEngine.cs:390-391`) requires new-position audio on every rendering output. A track that ends earlier than the others (e.g. commentary), seeked past its own end but inside the file, never gets audio; the decoder keeps running for the other output, so `DecodeEnded` stays false and the seek fails with `playback.seek_timeout` after 10 s while the other output already plays. Confirmed before T48. T3 (AC-10): `Ring.FramesAvailable > 0` (`AudioEngine.cs:383,390-391`) counts frames of any epoch — a late pre-seek block still in the ring can confirm a past-end seek as success, the run then ends `FileEnd` and `SeekTargetOutsideFile` is never published. T7: the T46 test `A_silent_render_after_a_seek_before_the_first_audio_does_not_count_as_first_audio` (`AudioEngineRunTests.cs:478-494`) now passes through a `playback.seek_timeout` and never asserts the seek result.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09, AC-10, AC-01.

## Definition of Done

- [ ] The pump reports per output when it has no more blocks for that output's track in the current epoch (or equivalent); seek confirmation does not wait on such outputs; a seek past one track's end but inside the file succeeds and the other output plays the new position.
- [ ] Ring-occupancy evidence counts only frames of the seek's epoch (or requires the pump's applied epoch first); a late pre-seek block cannot confirm a past-end seek.
- [ ] Tests: two tracks of different length, seek past the short one's end → success within the seek deadline; a past-end seek with a late old-epoch block in the ring → `SeekTargetOutsideFile` + run ends seek-past-end (deterministic, fake clock/decoder).
- [ ] The T46 silent-render test releases the decoder before the seek deadline and asserts the seek succeeded, with no `AudioStarted` before the release.
- [ ] Existing seek/past-end/recovery tests stay green.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
