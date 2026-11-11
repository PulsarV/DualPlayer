---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-11"
feature_size: "L"
ticket: ""
---

# 0005 — Convert each track to its device's mix format with FFmpeg swresample in DualPlayer.Media

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** Volodymyr Kravchuk (project owner), during the design Socratic walk

## Context

In shared mode the OS fixes each device's mix format (sample rate, channel layout, float samples). A track rarely matches it: 44.1 kHz on a 48 kHz device, or 5.1/7.1 on stereo headphones. AC-12 asks for every channel except LFE folded into the device layout at the correct pitch and speed, and front-channels-only with no upmix for tracks with fewer channels.

## Decision drivers

- Spec AC-12 (fold-down without LFE, no upmix, correct pitch and speed).
- QG-1: conversion must not disturb drift tolerance.
- Repo convention: codec and media concerns live in `DualPlayer.Media`; Core references no native library.

## Considered options

1. **FFmpeg swresample in `DualPlayer.Media`** — Core gives the decoder each output's target format (from the backend, ADR-0004); Media converts rate and layout in one pass with a custom rematrix (LFE weight 0, mono to both fronts, no upmix) and hands Core frames already in the device format.
2. **Rate conversion in Media, fold-down matrix in Core** — Media resamples only; Core applies the channel matrix.

## Decision outcome

**Chosen:** option 1. One mature component does both conversions in a single pass, and Core only ever handles frames in device format; the fold-down rules are expressed as a matrix, so roadmap step 5's dialogue-normalized downmix can replace the matrix without moving code.

## Consequences

**Positive**
- No own rate-conversion or channel-mapping code; FFmpeg handles channel-order differences between containers.
- Ring buffers and the drift resampler work in one uniform format per device.

**Negative**
- One converter per output, so a track on two devices is converted twice.
- Fold-down correctness is verified by tests in `DualPlayer.Media` that need FFmpeg installed (Homebrew on the dev Mac, shipped DLLs on Windows).

**Neutral**
- Only the near-unity drift ratio is applied after the ring, in Core (ADR-0003).

## Links

- Spec: [[../spec.md]] AC-12, US-07
- SAD: [[../sad.md]] §5
- Related ADR: [[0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position]], [[0003-correct-drift-with-a-managed-adaptive-resampler-after-the-ring-buffer]]
