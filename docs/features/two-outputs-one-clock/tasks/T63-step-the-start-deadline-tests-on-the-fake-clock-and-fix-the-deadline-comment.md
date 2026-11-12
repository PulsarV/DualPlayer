---
id: T63
title: "Step the start-deadline tests on the fake clock and fix the deadline comment"
layer: "app"
deps: []
blocks: []
acs: ["AC-01"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r7 (W1, W2, W3)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 7. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r7.md) · repo `CLAUDE.md`. Do not guess. -->

# T63 — Step the start-deadline tests on the fake clock and fix the deadline comment

## Why

- W1 (medium): with `FirstAudioDeadline` = 5 s, `An_output_stalled_at_start_that_recovers_after_audio_started_still_logs_its_start_to_audio` (`AudioEngineRunTests.cs:536-542`) steps 350 × `Render(1)` inside `WaitFor` to reach `AudioStarted`; at Windows' ~15.6 ms sleep that is ≈ 10.9 s, past the 10 s `WaitFor` timeout.
- W2 (low): `Audio_started_counts_a_first_render_just_past_the_two_second_start_to_audio_budget` (`:587-590`) catches a revert to the 2 s deadline only if the 2 ms control-loop poll runs in the ~2 ms between `Advance(2_350 ms)` and dev-2's first render.
- W3 (low): the comment at `AudioEngine.cs:761-762` says a "still silent and stalled past FirstAudioDeadline" output is not waited for; the deadline applies only to outputs that never rendered.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [x] W1: the stalled-at-start test jumps the fake clock past the deadline (as `:461-463` does) instead of stepping 350 renders; assertions unchanged.
- [x] W2: the T62 test keeps dev-2 hung long enough in real time for several polls after the advance and asserts `AudioStarted` is still empty before `Unhang()`; it fails deterministically against the 2 s deadline.
- [x] W3: the comment describes the three cases (lost before first audio; rendered only silence then stalled; never rendered and stalled past `FirstAudioDeadline`).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
