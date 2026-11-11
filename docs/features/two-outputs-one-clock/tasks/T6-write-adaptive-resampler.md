---
id: T6
title: "Write the managed adaptive resampler with one-step re-alignment"
layer: "domain"
deps: []
blocks: ["T9"]
acs: ["AC-07"]
files_hint: ["src/DualPlayer.Core/Sync/AdaptiveResampler.cs", "src/DualPlayer.Core/Sync/IFrameReader.cs", "tests/DualPlayer.Core.Tests/Sync/AdaptiveResamplerTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "S"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T6 — Write the managed adaptive resampler with one-step re-alignment

## Place in the sequence

- **Blocked by:** nothing (wave 1 root)
- **Blocks:** T9 — Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting
- **Wave:** 1 — no dependencies — starts immediately.
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to hear my track without drift building up over a feature-length film
> **So that** my sound never wanders away from the shared playback timeline
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task builds the part that actually applies the drift correction to the audio a device plays, without audible artefacts.

## Inlined context

> **Chosen:** Controller in `Core/Sync/` + managed adaptive resampler on the render thread, between the ring and the device — a proportional-integral controller turns the measured error into a rate ratio; the resampler applies it to the frames being handed to the device; one-step re-alignment drops or inserts frames at the same point.
>
> — `adr/0003 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0003](../adr/0003-correct-drift-with-a-managed-adaptive-resampler-after-the-ring-buffer.md)

> - Own DSP code in Core: a fractional-rate resampler (cubic interpolation is sufficient for ratios within a few hundred ppm) must be written and checked for audible artefacts.
> - Render-thread CPU grows with the resampler; counted against the ≤ 5 % budget.
> - Sample-rate and channel-layout conversion (e.g. 44.1 → 48 kHz, 5.1 → stereo) stays in `DualPlayer.Media` before the ring (§5); only the near-unity drift ratio is applied after it.
>
> — `adr/0003 §Consequences, Negative + Neutral, verbatim` · full text: [ADR-0003](../adr/0003-correct-drift-with-a-managed-adaptive-resampler-after-the-ring-buffer.md)

> The managed adaptive resampler (ADR-0003) may add audible artefacts or cost more CPU than budgeted — Mitigation: Unit tests on sine input (no new spectral peaks above a set level at ±500 ppm); listening check on the dev Mac; CPU measured in the evidence run against ≤ 5 %
>
> — `sad.md §11, Risks row 3, abridged` · full text: [sad.md](../sad.md)

> **Hard rule — Real-time code.** **Override:** code called on a render thread (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws; it only reads its ring and updates atomic counters; faults are reported to the control loop as events
>
> — `sad.md §8, Real-time code, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-07 — happy path (long run)

> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `IFrameReader` — the input the resampler pulls from (the ring implements it in T9) — `src/DualPlayer.Core/Sync/IFrameReader.cs`
- [ ] `AdaptiveResampler.Process(IFrameReader input, Span<float> output, double ratio)` with cubic interpolation, fractional phase and per-channel history carried across calls — `src/DualPlayer.Core/Sync/AdaptiveResampler.cs`
- [ ] `Realign(int frames)`: positive drops input frames, negative inserts (repeats) at the same point; applied at the next `Process` call
- [ ] `Reset()` clears history and phase (used on a new epoch)
- [ ] Tests on sine input — `tests/DualPlayer.Core.Tests/Sync/AdaptiveResamplerTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Ratio exactly 1.0 | output equals input (bit-exact or within float rounding) |
| Input runs dry mid-call | returns frames produced; caller fills the rest with silence |
| `Realign` larger than the frames available | drops what is there and carries the remainder to the next call |

## Definition of Done

- [ ] `AdaptiveResamplerTests` pass: at ratios 1 ± 500 ppm a sine keeps its expected frequency and shows no new spectral peak above a threshold named in the test
- [ ] `Realign(±n)` changes the consumed input count by exactly n
- [ ] a test asserts `Process` allocates 0 bytes
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
