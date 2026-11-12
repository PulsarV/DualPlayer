---
id: T23
title: "Stop the decode pump from dropping blocks for healthy outputs and keep content position true"
layer: "app"
deps: []
blocks: []
acs: ["AC-07", "AC-09"]
files_hint: ["src/DualPlayer.Core/Playback/DecodePump.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "src/DualPlayer.Core/Outputs/AudioRingBuffer.cs", "src/DualPlayer.Core/Playback/IAudioDecoder.cs", "tests/DualPlayer.Core.Tests/Playback/DecodePumpTests.cs", "tests/DualPlayer.Core.Tests/Outputs/OutputRenderSourceTests.cs", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F3)"
status: "todo"
step: "—"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T23 — Stop the decode pump from dropping blocks for healthy outputs and keep content position true

## Why (review findings)

### F3 — stage-2 (correctness), high

> `DecodePump` drops a block for a healthy output whenever its ring is momentarily full and another ring has room; content position is epoch start + frames read, so every drop is an audible skip with drift still ~0 (scratch harness: 320–660 ms lost per output in 30 s). `TryConsumeResumedFromStall` is never called.
>
> **Where:** `src/DualPlayer.Core/Playback/DecodePump.cs:111,283-287,302-313`, `src/DualPlayer.Core/Outputs/OutputRenderSource.cs:182-186`
>
> **Suggested:** Drop only for a ring whose own output is stalled, otherwise wait; carry `StartFrame` (or skipped-frame count) so content position stays true.
>
> — `_review/review-2026-11-12.md, F3, verbatim`

## Acceptance criteria

> ### AC-07 (US-03) — happy path (long run)
> 
> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim`

> ### AC-09 (US-04) — happy path
> 
> **Given** two audio outputs are playing
> **When** the Developer seeks 10 or 60 seconds forward or backward
> **Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console; the console prints that confirmation only after every output has discarded the audio it had queued from the old position
>
> — `spec.md §5, AC-09, verbatim`

## Hard rules (unchanged)

- Real-time code (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws — `sad.md §8`.
- No `DateTime.Now` / `Stopwatch` in Core; time from `TimeProvider` or the master clock. Expected failures return `Result` / domain events, never thrown across module boundaries — repo `CLAUDE.md`.
- Never weaken an existing test to make it pass; update a test only where it encoded the bug named above.

## Definition of Done

- [ ] a DecodePump test with mismatched block sizes (1536/1024 frames) and readers at different rates drops 0 blocks for non-stalled outputs; a block is dropped only for an output whose own ring is stalled; when frames are skipped the render source's content position advances by the skipped amount (test asserts it); `TryConsumeResumedFromStall` is used or removed
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
