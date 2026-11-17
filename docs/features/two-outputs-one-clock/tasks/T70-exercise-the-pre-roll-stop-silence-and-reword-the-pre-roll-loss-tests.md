---
id: T70
title: "Exercise the pre-roll stop silence and reword the pre-roll loss tests"
layer: "app"
deps: ["T69"]
blocks: []
acs: ["AC-08", "AC-06b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r2: R2, R3"
status: "todo"
step: "—"
---

<!-- Follow-up task from review-2026-11-17-r2. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T70 — Exercise the pre-roll stop silence and reword the pre-roll loss tests

## Why

- R2: removing `SetSilenceFrom(0)` in the pre-roll stop branch (`AudioEngine.cs:938-941`) leaves every Core test green — the fake closes the devices before any further render.
- R3: in both last-device-lost tests (`AudioEngineStartAndHangTests.cs:378-405`) "no film after the loss" holds whatever the engine does: a lost fake device never renders again (`FakeAudioDevice.cs:93`, `Fill` skips lost devices).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-06b; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [ ] A stop after the release but before the clock start renders on the devices during `EndRun` (before the close) and asserts no new film; mutation: removing `SetSilenceFrom(0)` turns it red.
- [ ] The two loss tests drop the film assertion and the "no film plays" wording; their summary / `AllDevicesLost` / no-`AudioStarted` assertions stay.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
