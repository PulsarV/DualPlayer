---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-11"
feature_size: "L"
ticket: ""
---

# 0004 — Let the audio backend pull frames from Core, passing the device's playback position on every call

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** Volodymyr Kravchuk (project owner), during the design Socratic walk

## Context

`IAudioBackend` (repo ADR-0002) is the contract between `DualPlayer.Core` and two adapters: WASAPI on Windows and AudioQueue on the dev Mac. This feature fixes its shape: who owns the render thread, how frames cross the boundary, and how Core learns each device's playback position, which is the only input to drift measurement (spec §1).

## Decision drivers

- QG-1: drift is measured from the device-reported playback position against the master clock; both must be read on the same monotonic time base.
- QG-2: each device renders on its own thread (ADR-0002).
- Repo convention: MMCSS "Pro Audio" on Windows render threads; Core references no OS library.
- Spec §6: recovery and isolation verified with fake devices.

## Considered options

1. **Pull** — the backend opens the device, owns its render thread and, whenever the device needs data, calls a Core-provided source `Render(buffer, devicePosition)` where `devicePosition` carries the frames the device has played and the monotonic timestamp of that reading; device loss and access denial arrive as backend events.
2. **Backend-owned buffer fed by Core** — the backend still owns the render thread and its loop (repo ADR-0002), but keeps its own sample buffer that Core's decode side writes into (`Write(frames)`, in the style of NAudio's buffered provider); Core polls the device position separately.

`IAudioBackend` also lists devices (`ListDevices()`) and opens them (`Open(deviceId)` returning an `IAudioDevice` stream) under both options.

## Decision outcome

**Chosen:** option 1. It matches both WASAPI's event-driven mode and AudioQueue's callback, keeps MMCSS and every OS wait inside the adapters, and lets a fake device drive Core by calling `Render` with any position it wants. Option 2 would put the drift adjustment in front of the backend's buffer, contradicting ADR-0003, and would read position and data at unrelated moments.

## Consequences

**Positive**
- OS threading details stay in the adapters; Core sees one call per device period.
- Drift tests replay arbitrary position sequences through a fake device.

**Negative**
- Core code runs on the backend's real-time thread: `Render` must not allocate, lock or block (rule in §8).
- The position timestamp must come from the same monotonic source as the master clock's `TimeProvider`; each adapter converts its OS timestamp (QPC on Windows, host time on macOS) to that base.

**Neutral**
- The backend also reports the device's shared-mode mix format when a stream opens; Core passes it to the decoder as the output's target format (ADR-0005).

## Links

- Spec: [[../spec.md]] §1, AC-04, AC-06, §6
- SAD: [[../sad.md]] §5
- Repo ADR: `docs/adr/0002-put-audio-output-behind-a-backend-port.md`
- Related ADR: [[0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers]], [[0003-correct-drift-with-a-managed-adaptive-resampler-after-the-ring-buffer]]
