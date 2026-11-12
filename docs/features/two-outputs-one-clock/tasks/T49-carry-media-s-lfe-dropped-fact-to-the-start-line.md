---
id: T49
title: "Carry Media's LFE-dropped fact to the start line"
layer: "app"
deps: ["T47", "T48"]
blocks: []
acs: ["AC-12"]
files_hint: ["src/DualPlayer.Media/", "src/DualPlayer.Core/Playback/AudioTrackInfo.cs", "src/DualPlayer.Console/Rendering/ConsoleFormat.cs", "tests/DualPlayer.Console.Tests/", "tests/DualPlayer.Media.Tests/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r3 (S7)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T49 — Carry Media's LFE-dropped fact to the start line

## Why

S7 (review-2026-11-12-r3): The console LFE rule is re-derived from the layout enum: 2.1/4.1/6.1 sources (mapped to `Other`) get no note though Media drops the LFE; a 6.0 source would print "LFE dropped".

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-12.

## Definition of Done

- [ ] The track info carries whether the source has an LFE channel (and its channel count) from Media; the console notes "LFE dropped" exactly when Media's fold-down drops it (source has LFE and more channels than the device).
- [ ] Tests for 2.1 → stereo, 6.0 → stereo, 7.1 → 5.1, 5.1 → 5.1.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
