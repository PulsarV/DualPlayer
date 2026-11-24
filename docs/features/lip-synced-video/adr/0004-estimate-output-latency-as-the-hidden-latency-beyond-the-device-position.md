---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-20"
feature_size: "L"
ticket: ""
---

# 0004 — Estimate output latency as the hidden latency beyond the device position

- **Status:** Accepted
- **Date:** 2026-11-20
- **Deciders:** Volodymyr Kravchuk

## Context

Spec AC-13 estimates an output's latency as "the latency the operating system reports for the device plus the default for its device type". The step-3 master clock already aligns every output on its device's reported playback position (`IAudioClock`, two-outputs-one-clock ADR-0008: the clock starts when the slowest output's position shows the first frame). The OS buffer and pipeline latency is therefore already compensated before this step adds anything. The latency WASAPI reports for a stream (`GetStreamLatency`, the device period) describes exactly that already-compensated buffering. Adding it again would hold the picture back by an extra ~10–40 ms on every output.

## Decision drivers

- QG-1 / spec §6: sound no more than 45 ms ahead of its picture. Double counting pushes every output towards "sound early".
- Spec §8 default: treat a Bluetooth device's reported position as not including its codec and headset latency.
- Spec AC-12, AC-13, AC-17: the log shows the reported part and the type default separately; the latency is worked out again at every start.
- `CONTEXT.md`: Latency = the time between handing a sound to a device and the Viewer hearing it.

## Considered options

1. **Hidden latency only.** Assumed latency = the latency the OS reports *beyond* the device position + the type default − the offset. On WASAPI no such report exists today, so the reported part is 0 ms and is logged as such, with its source. A latency-probe port still returns the reported part, so a later OS facility or platform can fill it.
2. **Literal reading: add `GetStreamLatency` (+ device period) on top.** The log carries a non-zero reported part, but every output's picture is held back by buffering the clock already compensated.

## Decision outcome

**Chosen:** Option 1. It is the only option consistent with how the step-3 clock already aligns outputs. It avoids a systematic sound-early error that would eat most of the 45 ms budget even on wired devices. AC-13's reported part stays a separate, logged term (0 ms on Windows today), so the spec's log contract holds and the model can grow later.

## Consequences

**Positive**
- No double counting: wired and HDMI outputs need no offset out of the box, and Bluetooth outputs start from the type default (200 ms) alone.
- One rule for every platform: whatever a platform can report beyond its device position goes into the reported part.

**Negative**
- On Windows today the estimate is the type default alone. The "OS-reported" half of AC-13 adds nothing measurable until a platform reports a hidden latency. Recorded as a decision override in `sad.md` §1 and as a risk in §11.
- Whether a Bluetooth device's position already includes part of its codec latency (spec §8) still decides how good the 200 ms default is. The first-use measurements settle it.

**Neutral**
- The sign convention is fixed here for every later stage: assumed latency `Aᵢ = reported part + type default − offset`, where a positive offset makes sound heard later (`CONTEXT.md` Offset), and picture delay `PD = max Aᵢ`.

## Links

- Spec: [[../spec.md]] AC-12, AC-13, AC-17, §8
- SAD: [[../sad.md]] §4
- Related ADR: [[0005-hold-faster-outputs-back-with-a-target-lag-moved-by-the-drift-resampler]]; two-outputs-one-clock ADR-0008
