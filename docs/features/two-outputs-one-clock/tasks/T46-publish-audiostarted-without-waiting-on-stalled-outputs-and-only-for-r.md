---
id: T46
title: "Publish AudioStarted without waiting on stalled outputs and only for real audio"
layer: "app"
deps: ["T45"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r3 (S3)"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T46 — Publish AudioStarted without waiting on stalled outputs and only for real audio

## Why

S3 (review-2026-11-12-r3): An output hung from the start keeps `awaiting` true forever, so `AudioStarted` never comes; after a seek before first audio `ApplyEpoch` sets `_lastContentFrame ≥ 0`, so a silent render counts as first audio.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [ ] Stalled outputs are not waited for (as `WaitForSeek` / stop do).
- [ ] First audio = the first render that handed out decoded audio, not `LastContentFrameHandedOut >= 0`.
- [ ] Tests: one output hung from start → `AudioStarted` still published from the others; seek before first audio → start-to-audio measured to real audio.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
