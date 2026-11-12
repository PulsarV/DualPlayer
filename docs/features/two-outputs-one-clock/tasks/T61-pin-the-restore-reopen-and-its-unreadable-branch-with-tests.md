---
id: T61
title: "Pin the restore reopen and its unreadable branch with tests"
layer: "integration"
deps: []
blocks: []
acs: ["AC-01", "AC-09"]
files_hint: ["tests/DualPlayer.Media.Tests/Audio/FfmpegAudioDecoderTests.cs", "src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs", "src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r6 (V1)"
status: "todo"
step: "—"
---

<!-- Follow-up task from re-review 6. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r6.md) · repo `CLAUDE.md`. Do not guess. -->

# T61 — Pin the restore reopen and its unreadable branch with tests

## Why

V1 (stage-2, AC-01/AC-09): both restore-seek tests (`FfmpegAudioDecoderTests.cs:306-347`) return early and pass whenever `Open` fails as `playback.file_unreadable`. `RestoreStart` (`FfmpegDecodeSession.cs:217-236`) always reopens on a failed restore, so a mutation that breaks every reopen (e.g. `_path = path + ".gone"` at `:186`) keeps the Media suite green (51/51). The new end-of-file test also passes on `e6383d2`: the old ranged retry was a real `av_seek_frame`, which succeeds on the cue-indexed `ramp-offset.mkv`, so T58's DoD "fails on the current code" is not met. The `Failure(Unreadable)` branch (`:225-228`) has no test. The comment at `:325-327` claims more than the one-call seam can model.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-09.

## Definition of Done

- [ ] Both restore-seek tests assert `opened.IsSuccess` unconditionally (with the error message), then keep the content-0 (StartFrame ≈ 0, ramp ≈ 0) and duration checks; no early return.
- [ ] A new test makes the reopen fail (e.g. a temp copy of the asset deleted inside `RewindSeekHook`, which then returns -1) and asserts `playback.file_unreadable`; no temp file left behind.
- [ ] Mutation check: breaking the reopen path makes the tests red.
- [ ] The comment above the end-of-file test is corrected (the seam models a failed seek, not a seek that reports success without rewinding); T58's note records that the old code cannot be failed through the one-seek seam.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
