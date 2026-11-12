---
id: T42
title: "Take the LFE-dropped note from Media's conversion"
layer: "app"
deps: ["T34", "T41", "T38"]
blocks: []
acs: ["AC-12"]
files_hint: ["src/DualPlayer.Media/Audio/MixFormatConverter.cs", "src/DualPlayer.Core/Playback/EngineEvent.cs", "src/DualPlayer.Console/Rendering/ConsoleFormat.cs", "tests/DualPlayer.Console.Tests/", "tests/DualPlayer.Media.Tests/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r2 (R9)"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T42 — Take the LFE-dropped note from Media's conversion

## Why

R9 (review-2026-11-12-r2): Console prints "LFE dropped" only for 5.1/7.1 onto < 6 channels; Media drops LFE whenever the source has more channels than the device (`FoldDown` weight 0), so 7.1 → 5.1 prints no note though LFE is dropped.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-12.

## Definition of Done

- [ ] The start line's LFE note comes from the same rule Media applies (a flag carried into `StartedOutput`, or the identical predicate in one shared place).
- [ ] Tests: 7.1 → 5.1 prints `LFE dropped`; 5.1 → 5.1 and stereo → stereo do not.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
