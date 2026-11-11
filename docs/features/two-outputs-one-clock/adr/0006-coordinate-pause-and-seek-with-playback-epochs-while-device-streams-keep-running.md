---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-11"
feature_size: "L"
ticket: ""
---

# 0006 — Coordinate pause and seek with playback epochs while device streams keep running

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** Volodymyr Kravchuk (project owner), during the design Socratic walk

## Context

Pause, resume and seek act on every output at once (US-04). Audio for each output sits in two queues the engine must account for: the output's ring buffer (seconds, ADR-0002) and the device's own shared-mode buffer. AC-08 wants all outputs silent within 10 ms of each other on pause, judged by device-reported position; AC-09 wants no old-position audio once the console confirms a seek; spec §6 wants every output back within tolerance ≤ 1 s after resume or seek.

## Decision drivers

- AC-08, AC-09, AC-09b; spec §6 recovery ≤ 1 s.
- QG-1: continuous device positions keep the drift and clock-mismatch estimates valid across commands.
- Spec §6: recovery verified with a fake clock and fake devices.

## Considered options

1. **Playback epochs with streams kept running** — one engine-control loop applies commands one at a time; seek and stop increment an epoch, the decoder tags frames with the epoch they belong to, render sources discard older-epoch frames and play silence until new frames arrive; pause and stop choose an effective instant on the master clock that lies beyond the audio already queued in every device (the largest queued amount among the devices, known from each device's written frames minus its reported position), and every render source switches to silence exactly at the frame that maps to that instant; device streams are never stopped during a run.
2. **Stop and restart every stream** — on pause or seek, stop and reset every device stream, clear rings, re-seek, refill, and start all streams together.

## Decision outcome

**Chosen:** option 1. Because the effective instant lies past every device's queued audio, silence starts at the same master-clock instant on every device, so pause alignment (AC-08) follows from the clock rather than from how much each device has buffered or how fast it stops; device positions stay continuous, so drift measurement and the per-device clock-mismatch estimate survive commands; and no Bluetooth stream restart (hundreds of ms, possible glitch) sits on the recovery path.

## Consequences

**Positive**
- Pause, resume and seek are pure Core logic over the render-source contract (ADR-0004), testable with fake devices.
- Commands and backend events never race: one control loop owns transport state.

**Negative**
- Devices keep consuming silence while paused (small CPU cost, counted in the ≤ 5 % budget).
- Pause and stop take effect a few tens of milliseconds after the command (the largest device queue), not instantly.
- A seek is confirmed only after each output has discarded its old-epoch ring frames **and** its device's reported position has passed the last old-epoch frame already handed to the device; confirmation time therefore includes the device buffer.

**Neutral**
- Seek past the end ends the run (AC-10); the epoch mechanism also drains outputs on stop before the run summary.

## Links

- Spec: [[../spec.md]] AC-08, AC-09, AC-09b, AC-10, §6
- SAD: [[../sad.md]] §6
- Related ADR: [[0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers]], [[0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position]]
