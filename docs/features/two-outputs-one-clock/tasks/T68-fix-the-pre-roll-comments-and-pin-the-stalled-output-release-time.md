---
id: T68
title: "Fix the pre-roll comments and pin the stalled-output release time"
layer: "app"
deps: ["T67"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17: N1, N2"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from review-2026-11-17. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17.md) · repo `CLAUDE.md`. Do not guess. -->

# T68 — Fix the pre-roll comments and pin the stalled-output release time

## Why

- N1: `AudioEngine.cs:21` says `StartMargin` is the lead between the release and the clock start — it is added after the slowest output hears its next frame (release→clock start ≈ `L_max` + 30 ms). `:373` says the wait is bounded by the first-audio deadline; code bounds it by `FirstAudioDeadline + StartLevelTimeout` (`:377`). `:861` says a non-moved output holds the release for `FirstAudioDeadline`; a non-rendering one stops holding after `DecodePump.StallThreshold` (1 s, `:449`).
- N2: `AudioEngineRunTests.cs:473` loosened from ≤ 100 ms to ≤ 5.1 s after one 5 s fake-time jump; it cannot tell "released once the hung output counts as stalled (~1 s)" from "held to the 5 s deadline".

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [ ] The three comments describe what the code does.
- [ ] The test advances the fake clock in ~10 ms steps and asserts `StartToAudio` within ~1.0–1.2 s; it fails if the release waits for `FirstAudioDeadline`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
