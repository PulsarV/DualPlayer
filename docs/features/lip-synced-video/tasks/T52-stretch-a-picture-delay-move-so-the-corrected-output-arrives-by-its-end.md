---
id: T52
title: "Stretch a picture-delay move so the corrected output arrives by its end"
layer: "app"
deps: ["T51"]
blocks: []
acs: ["AC-14"]
files_hint: ["src/DualPlayer.Core/Sync/PictureSync.cs", "src/DualPlayer.Core/Sync/LagRamp.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Sync/", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21b (Q11)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T52 — Stretch a picture-delay move so the corrected output arrives by its end

## Why

Q11: AC-14 says the corrected output arrives by the end of the picture move. The move lasts |ΔPD|/0.2 % (`PictureSync.cs:232`), while each output's lag ramps on its own at 2000 ppm (`LagRamp.cs:24`, `PlaybackSession.cs:679-686`); the corrected output's lag change can be larger than the picture's move (e.g. the slowest output pressed later with the next-slowest within 10 ms), so it lands after the move ends. Not reachable on Windows today (WASAPI reports 0, 10 ms grid), reachable on the dev Mac. `PlaybackSessionOffsetTests.cs:35` covers only the trivial case.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-14.

## Definition of Done

- [ ] The move duration is max(|ΔPD|, |ΔD_c| over changed outputs)/0.2 %; the picture and every output's ramp finish together at ≤ 2000 ppm.
- [ ] Test: an offset press where both ΔPD and the corrected output's lag change are non-zero and differ — the corrected output reaches its target no later than the picture move ends; no rate exceeds 0.2 %.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
