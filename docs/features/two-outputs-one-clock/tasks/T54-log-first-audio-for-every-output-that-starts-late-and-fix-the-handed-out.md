---
id: T54
title: "Log first audio for every output that starts late and fix the handed-out doc comments"
layer: "app"
deps: ["T53"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRunTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r4 (T5, T9)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 4. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r4.md) · repo `CLAUDE.md`. Do not guess. -->

# T54 — Log first audio for every output that starts late and fix the handed-out doc comments

## Why

T5 (AC-01, spec §6 start-to-audio): a stalled-at-start output is no longer awaited (`AudioEngine.cs:737-738`) and `LogFirstAudio(run)` runs only from `Poll` (`AudioEngine.cs:532`), which stops after `AudioStarted` unless the decoder ended or an alignment runs. An output that recovers later never gets its start-to-audio log line (the comment says it is logged if it recovers); a device whose first render is > 1 s late counts as stalled, so `started` under-reports. T9: `HasHandedOutAudio` has two `<summary>` tags; `LastContentFrameHandedOut` has none (`OutputRenderSource.cs:74-78`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [ ] Polling (or the sync timer) keeps checking first audio while any non-lost output has not logged it; an output stalled at start that recovers after `AudioStarted` gets its start-to-audio log line.
- [ ] Test: one output stalls at start and recovers after `AudioStarted` → its first-audio log/event is recorded with the right start-to-audio.
- [ ] `LastContentFrameHandedOut` and `HasHandedOutAudio` each carry their own single `<summary>`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
