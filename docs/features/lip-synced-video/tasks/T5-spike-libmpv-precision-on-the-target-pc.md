---
id: T5
title: "Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict"
layer: "tests"
deps: ["T3", "T4"]
blocks: ["T6", "T7", "T8", "T9", "T11"]
acs: ["AC-14"]
files_hint: ["tests/DualPlayer.Media.Tests/Video/MpvSpikeTests.cs", "docs/features/lip-synced-video/evidence/", "docs/roadmap.md", "docs/features/lip-synced-video/ux-flows.md"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T5 — Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict

## Place in the sequence

- **Blocked by:** T3 — Steer the picture to clock minus picture delay with a Core picture-sync loop, T4 — Implement the hardened libmpv video renderer with presented-position snapshots and OSD notes
- **Blocks:** T6 — Model device types, type defaults, assumed latency, picture delay and lags behind a latency-probe port, T7 — Persist per-device offsets in device-offsets.json and keep them in an offset book with clamp, step and debounced save, T8 — List a file's folder neighbours (.mkv/.mp4) in natural name order for next and previous, T9 — Probe a file's picture with FFmpeg in hardware, off the caller's thread, with a time limit, T11 — Measure each output's drift against clock minus its lag and ramp lag changes at up to 2000 ppm
- **Wave:** 3 — after its deepest dependency finishes (wave 2).
- **Lane:** shares `docs/features/lip-synced-video/evidence/` with T23 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** the picture to follow smoothly when the slowest output changes — its offset is corrected or its device is lost
> **So that** neither Viewer sees the picture jump or loses lip-sync for longer than a moment
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task is the verification gate for roadmap D1: it proves on the target PC that libmpv steered by the Core loop meets the lip-sync precision before anything else is built on it.

## Inlined context

> **Verification gate (D1 stays open until it passes).** The first implementation task is a spike on the target PC. It drives libmpv through the `IVideoRenderer` adapter with the Core loop. **Kill criterion** — the spike fails if, on a 1080p HEVC file with hardware decoding, any of these holds:
> - in steady playback, the picture is presented more than 20 ms from due;
> - after pause, resume or seek, the picture is not back within 20 ms of due within 1 s;
> - a 0.2 % speed move shows a visible jump or repeated frame;
> - more than 1 frame per minute is late or dropped.
>
> On failure, the fallback is option 2 behind the same port, recorded as a superseding ADR, with the step re-sized to XL.
>
> — `adr/0002 §Decision outcome, verification gate, verbatim` · full text: [ADR-0002](../adr/0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port.md)

> - [ ] Roadmap D1: can the picture be presented against the audio master clock precisely enough for the §6 lip-sync budget and the 0.2 % speed change with the planned video rendering, or is an own decode-and-present path needed? Direction decided 2026-11-20: libmpv steered by a Core loop (`sad.md` ADR-0002); still open until the first-task spike on the target PC passes the ADR-0002 kill criterion, with an own decode-and-present path as the fallback. — owner: Volodymyr Kravchuk (decision), spike by the agent, due: before any task after the spike in `/sdd:implement`
>
> — `spec.md §8, open question D1, verbatim` · full text: [spec.md](../spec.md)

> | libmpv steered through `speed` and `seek` may not hold the picture within 20 ms of due, or may show a 0.2 % move as a jump (roadmap D1) | High | The first task is a spike on the target PC with the kill criterion in ADR-0002. On failure, an own FFmpeg decode-and-present adapter behind the same `IVideoRenderer` port. A failed spike means re-sizing the step to XL, a superseding ADR, a repeated design pass for the Media part, and an updated roadmap status | Volodymyr Kravchuk |
>
> — `sad.md §11, risk row D1, verbatim` · full text: [sad.md](../sad.md)

> | The start budget of ≤ 3 s is tight. The step-3 start already takes ~2 s with the MOTIF headset (silent pre-roll, step-3 ADR-0008), and this step adds the picture probe and mpv's file load | Medium | Run the picture probe and the device latency probe before the pre-roll. Load the file in mpv while the pre-roll runs. Log each phase's timestamps; the spike measures them | Volodymyr Kravchuk |
>
> — `sad.md §11, risk row start budget, verbatim` · full text: [sad.md](../sad.md)

> - `ux-flows.md` flow US-02 still says the picture freezes "on the frame shown at the key press"; spec AC-04 (clarified 2026-11-20) and flow 4 freeze it on the frame matching the last sound heard. Update `ux-flows.md` before `screens`.
>
> — `sad.md §6, flagged for later stages, verbatim` · full text: [sad.md](../sad.md)

> | Parallel wave 4 ∥ 5 ∥ 6: … Step 4 also adds new files outside its roadmap zone: `Platform.Windows/Audio/WasapiLatencyProbe.cs` (next to step 5's enumeration) and `Core/Settings/DeviceOffsetsFile.cs` | Low | Step 4 confines engine changes to the lag hooks and only adds new files elsewhere (§2, §5). Update the roadmap's wave-3 zone line for step 4. Merge order follows the roadmap waves | Volodymyr Kravchuk |
>
> — `sad.md §11, risk row parallel wave, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-14 — domain invariant

> **Given** a film is playing and the picture is held back by the picture delay the slowest output needs
> **When** the picture delay has to change — an offset is corrected, or the slowest output's device is lost and another output becomes the slowest
> **Then** the picture and every output whose offset did not change move to the new delay together, at most 0.2 % faster or slower than normal speed, so that nobody sees a jump, a black frame or a repeated scene or hears a change of pitch, and each of those outputs stays within 10 ms of where it was against the picture throughout the move; the corrected output reaches its new offset no later than the move ends; a seek, file change or resume during a move sets the target picture delay and offsets at once instead, since picture and sound are re-aligned then anyway
>
> — `spec.md §5, AC-14, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Write a `[HardwareFact]` spike harness that opens `MpvVideoRenderer` (mpv may create its own window here), drives it with `PictureSync` on a `TimeProvider.System` clock, and runs: 10 min steady play, 10 × pause/resume, 10 × seek ±10 s/±60 s, 10 × picture-delay moves of ±10…100 ms; it measures presented-vs-due per frame, recovery time, late/dropped per minute and mpv load time, and writes a markdown report — `tests/DualPlayer.Media.Tests/Video/MpvSpikeTests.cs`
- [ ] Owner runs it on the target PC with a 1080p HEVC file and watches the moves for jumps or repeated frames (agent prepares a run guide, as `two-outputs-one-clock/evidence/target-pc-run-guide.md`)
- [ ] Record the evidence with a verdict per kill-criterion line — `docs/features/lip-synced-video/evidence/<date>-d1-spike.md`
- [ ] Pass → mark D1 decided with a link to ADR-0002 and the evidence, and update the step-4 wave-3 zone line — `docs/roadmap.md`; fail → stop `/sdd:implement`, raise a superseding ADR via `/sdd:decide-adr`, re-size to XL
- [ ] Fix the US-02 freeze wording flagged by `sad.md §6` — `docs/features/lip-synced-video/ux-flows.md`

## Edge cases

| Case | Behaviour |
|---|---|
| Any kill-criterion line fails | D1 fails: no task after T5 starts; superseding ADR + re-size (sad §11) |
| Key focus with the native child window (`sad.md §11`) | cannot be checked here — no Avalonia window yet; checked in T22/T23 and noted in the evidence |
| Resampler artefacts at ±2500 ppm (`ADR-0005`) | audio ramps arrive in T11–T12; listened for in T23 and noted in the evidence |

## Definition of Done

- [ ] evidence file committed with numbers and a verdict per kill-criterion line
- [ ] roadmap D1 status updated (decided, or blocked with the superseding-ADR link)
- [ ] `ux-flows.md` US-02 wording matches AC-04
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
