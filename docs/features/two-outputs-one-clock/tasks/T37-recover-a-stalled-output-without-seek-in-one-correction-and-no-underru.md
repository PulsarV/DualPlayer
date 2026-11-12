---
id: T37
title: "Recover a stalled output without seek in one correction and no underruns"
layer: "app"
deps: ["T36"]
blocks: []
acs: ["AC-07"]
files_hint: ["src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "src/DualPlayer.Core/Outputs/AudioRingBuffer.cs", "src/DualPlayer.Core/Sync/AdaptiveResampler.cs", "src/DualPlayer.Core/Playback/DecodePump.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRecoveryTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12-r2 (R4)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T37 — Recover a stalled output without seek in one correction and no underruns

## Why

R4 (review-2026-11-12-r2): A stalled output recovering without a seek first plays stale ring contents; the 1 s record requests a drop, and the drop counts ring frames while crossing the skipped-frame marker also advances content — the stall is removed twice: ~1.5 s underruns (411), then ~2 s ahead, ~5 s to settle.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07.

## Definition of Done

- [ ] On the first read after a stall the output restarts content at the post-stall frame (stale frames dropped, skip marker honoured once), then is aligned once from its true position.
- [ ] Render path stays allocation-, lock-, log- and throw-free.
- [ ] Engine test: 3 s hang then recovery → 0 underruns on the recovered output after recovery, at most one correction, within tolerance within the recovery time; healthy outputs keep 0 underruns.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
