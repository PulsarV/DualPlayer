---
id: T33
title: "Compare the first block of each epoch with the seek target so a late-landing seek stays aligned"
layer: "app"
deps: ["T28"]
blocks: []
acs: ["AC-09", "AC-01"]
files_hint: ["src/DualPlayer.Core/Playback/DecodePump.cs", "tests/DualPlayer.Core.Tests/Playback/DecodePumpTests.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12 (F4, remainder found during T28)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T33 — Compare the first block of each epoch with the seek target so a late-landing seek stays aligned

## Why

F4 (review-2026-11-12) required Core to honour each block's `StartFrame` and detect gaps. T24 does this between consecutive blocks, but `DecodePump._expectedNext` is −1 at the start of an epoch, so the **first** block of an epoch is never compared with the seek target (or with frame 0 at start). If the decoder's first block starts after the target — an audio stream whose first packet is 45 ms in, or a hole at the seek point — the ring labels that content as starting at the target. The engine's sync records show ~0 ms drift while the audio actually played is 45 ms off for the rest of the run.

> Repro (T28 `EngineRig`): `seekLandsLate: [TimeSpan.FromMilliseconds(45), TimeSpan.Zero]`; `Start()`, `Render(7000)`, `Seek(130 s)`, `Render(3300)`, then `MaxDriftMs` over [1 s, 31 s] on dev-1 → ~45 ms.
>
> — implement run, T28 report

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09 (all outputs continue from the new position and are back within the drift tolerance within the recovery time) and AC-01 (start from the beginning of the file).

## Definition of Done

- [ ] The baseline for the first block of each epoch (start and every seek) is the epoch's start frame (target × output rate); a first block starting later records the gap via `ring.NoteSkipped`, one starting earlier is trimmed.
- [ ] An engine-level test with `seekLandsLate` (45 ms) and a start-time variant keeps the played audio within 10 ms of the master clock from 1 s after the command; a `DecodePumpTests` unit test pins the first-block gap.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
