---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-20"
feature_size: "L"
ticket: ""
---

# 0005 — Hold faster outputs back with a target lag moved by the drift resampler

- **Status:** Accepted
- **Date:** 2026-11-20
- **Deciders:** Volodymyr Kravchuk

## Context

With picture delay `PD = max Aᵢ` (ADR-0004), every faster output has to wait `Dᵢ = PD − Aᵢ ≥ 0` behind the master clock so that its sound reaches the Viewer together with the picture. `Dᵢ` changes during playback when an offset is corrected or the slowest output is lost. The change must happen at no more than 0.2 % faster or slower than normal speed, with no click or pitch change heard (AC-10, AC-14). The step-3 engine already keeps each output on the clock with a managed adaptive resampler after the ring buffer (two-outputs-one-clock ADR-0003), capped at 500 ppm for drift. At start it already inserts silence before frame 0 for faster outputs (two-outputs-one-clock ADR-0008).

## Decision drivers

- Spec AC-10, AC-14, §6: per-output moves at ≤ 0.2 % speed; picture and unchanged outputs stay within the lip-sync budget throughout; seek, file change and resume set targets at once.
- Step-3 targets still hold: drift ≤ 10 ms, 0 underruns, ≤ 1 s recovery.
- §2 constraint: step 4 does not edit `Core/Outputs/` (parallel wave 4 ∥ 5 ∥ 6). This rules out a separate per-output delay line with its own time-stretch in `OutputRenderSource`, which would also add a second resampler per output.

## Considered options

1. **Target lag inside the drift controller.** Each output's drift error is measured against `clock − Dᵢ` instead of the clock. At start the lag joins the silent pre-roll alignment. During playback a change of `Dᵢ` is a target ramp at ≤ 2000 ppm, added to the drift correction and carried by the existing resampler. Changes stay in `Core/Sync/` and `Core/Playback/`.

No other option survives the drivers: a separate delay line is ruled out by the §2 constraint above, and a one-step move is ruled out by AC-10 and AC-14. The ADR records the decision because it is irreversible and touches several modules (`Core/Sync/`, `Core/Playback/`, the step-3 metrics).

## Decision outcome

**Chosen:** Option 1. It adds no new audio stage on the render thread and reuses the resampler that already carries step-3 corrections. It stays within the folders step 4 may edit.

## Consequences

**Positive**
- One mechanism moves every output: drift correction and lip-sync moves share the controller, the resampler and the same tests with a fake clock and fake devices.
- Start alignment needs no new phase: the lag is extra silence in the existing pre-roll.

**Negative**
- During a ramp, the drift metrics must tell an intended move from real drift. Drift is measured against the moving target, and the ramp is logged as its own event. Otherwise the run summary would report false excursions and corrections.
- The resampler's total ratio range widens from ±500 ppm to about ±2500 ppm, which must be checked for audible artefacts on the target PC.

**Neutral**
- With a negative picture delay the picture starts before the slowest output hears frame 0, timed from the pre-roll measurement (`sad.md` §6 flow 1).
- The picture moves at the same ≤ 0.2 % rate through mpv's `speed` (ADR-0002), so a picture-delay change finishes for picture and outputs at the same moment.

## Links

- Spec: [[../spec.md]] AC-10, AC-14, AC-15, §6
- SAD: [[../sad.md]] §4
- Related ADR: [[0004-estimate-output-latency-as-the-hidden-latency-beyond-the-device-position]], [[0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port]]; two-outputs-one-clock ADR-0003, ADR-0008
