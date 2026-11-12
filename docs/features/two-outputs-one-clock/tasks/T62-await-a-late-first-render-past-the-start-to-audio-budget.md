---
id: T62
title: "Await a late first render past the start-to-audio budget"
layer: "app"
deps: []
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r6 (V2)"
status: "todo"
step: "—"
---

<!-- Follow-up task from re-review 6. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r6.md) · repo `CLAUDE.md`. Do not guess. -->

# T62 — Await a late first render past the start-to-audio budget

## Why

V2 (stage-2, AC-01, spec §6 "start command to audio on all outputs ≤ 2 s"): `FirstAudioDeadline` (`AudioEngine.cs:735`) is 2 s — exactly the budget — and is measured from `run.StartTimestamp`, taken before `ListTracks` / `Open` / the start-level fill (`:114`). An output that first renders at 2.4 s is no longer awaited (`:747`), `IsStalled` is true, so `AudioStarted.StartToAudio` (the console `started` line) shows the faster output's value: a budget breach shows as a pass. The comments at `:535` ("A stalled output is not waited for by AudioStarted") and `:757` ("stalled before its first audio is not waited for") no longer match the code.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [ ] The first-audio deadline sits clearly above the 2 s budget (e.g. reuse `StartLevelTimeout` or another named option), so a first render up to the deadline is counted in `AudioStarted.StartToAudio`.
- [ ] A test with a first render just past 2 s (e.g. 2.4 s) asserts `StartToAudio` ≥ 2 s; it fails on the current code.
- [ ] An output that never renders is still not awaited forever (existing tests stay green, wall-clock cost kept small).
- [ ] The comments at `:535` and `:757` describe the actual behaviour.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
