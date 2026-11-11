---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-11"
feature_size: "L"
ticket: ""
---

# 0003 — Correct drift with a managed adaptive resampler on the render side, after the ring buffer

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** Volodymyr Kravchuk (project owner), during the design Socratic walk

## Context

Each device's clock runs slightly fast or slow against the master clock. The spec fixes the mechanism — a continuous playback-rate adjustment in parts per million, plus a one-step re-alignment that is counted as a correction (CONTEXT `Correction`, AC-13) — but not where the rate change is applied. Because the pipeline buffers seconds of audio per output (ADR-0002), applying the adjustment before the ring would delay its effect by the whole ring depth.

## Decision drivers

- QG-1 / spec §6: steady-state drift ≤ 10 ms over a ≥ 3 h run; recovery ≤ 1 s after start, resume or seek.
- Spec §6: recovery verified by unit tests with a fake clock and fake devices.
- Repo constraint: Core references no native library; sync logic is testable with fakes.

## Considered options

1. **Controller in `Core/Sync/` + managed adaptive resampler on the render thread, between the ring and the device** — a proportional-integral controller turns the measured error into a rate ratio; the resampler applies it to the frames being handed to the device; one-step re-alignment drops or inserts frames at the same point.
2. **Controller in `Core/Sync/` + FFmpeg `swr_set_compensation` in `DualPlayer.Media`, on the decode thread before the ring** — reuse FFmpeg's resampler for the rate adjustment.

## Decision outcome

**Chosen:** option 1. The correction takes effect within one device period instead of after seconds of buffered audio, which keeps the control loop stable inside the 10 ms tolerance; and the whole loop runs in Core against fake clocks and fake devices. Option 2 would either oscillate or force a shallow ring that raises underrun risk.

## Consequences

**Positive**
- Short, predictable control-loop delay; drift, rate and re-alignment logic fully unit-testable without FFmpeg or hardware.
- The ring stays deep for underrun protection (QG-3).

**Negative**
- Own DSP code in Core: a fractional-rate resampler (cubic interpolation is sufficient for ratios within a few hundred ppm) must be written and checked for audible artefacts.
- Render-thread CPU grows with the resampler; counted against the ≤ 5 % budget.

**Neutral**
- Sample-rate and channel-layout conversion (e.g. 44.1 → 48 kHz, 5.1 → stereo) stays in `DualPlayer.Media` before the ring (§5); only the near-unity drift ratio is applied after it.

## Links

- Spec: [[../spec.md]] AC-07, AC-13, §6
- SAD: [[../sad.md]] §4, §5
- Related ADR: [[0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers]]
