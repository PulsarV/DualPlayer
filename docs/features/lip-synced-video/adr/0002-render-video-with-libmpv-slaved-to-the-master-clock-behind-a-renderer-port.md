---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-20"
feature_size: "L"
ticket: ""
---

# 0002 — Render video with libmpv slaved to the master clock behind a renderer port

- **Status:** Accepted
- **Date:** 2026-11-20
- **Deciders:** Volodymyr Kravchuk

## Context

Roadmap D1 asks whether libmpv can present the picture precisely enough against our audio master clock, or whether an own decode-and-present path is needed. mpv has no external-clock option. The picture must be shown at `master clock − picture delay`, follow pause, seek and resume, and move smoothly at ≤ 0.2 % speed when the picture delay changes. Spec §8 set D1 due during design, to be decided by a prototype. A prototype on the dev Mac was ruled out: it runs on VideoToolbox and the macOS compositor, which the dev path is not held to (`sad.md` §2), so only a target-PC spike can decide. The step-3 master clock is driven by `TimeProvider`, the same OS monotonic time base mpv uses for video-only playback, so long-run drift between the two clocks is not expected.

## Decision drivers

- Spec §6: lip-sync per output within 45 ms sound-early / 125 ms sound-late; recovery ≤ 1 s with the picture presented within 20 ms of due; picture-delay change at ≤ 0.2 % speed; ≤ 1 frame late or dropped per minute; ≤ 15 % CPU (QG-1, QG-2, QG-3).
- Spec AC-02: hardware decoding only in this step.
- `CLAUDE.md`: sync logic is tested with a fake clock; Core holds no native library.
- Roadmap step 11 renders subtitles over the video (libass, secondary subtitles).

## Considered options

1. **libmpv, slaved by a Core loop through a renderer port.** mpv decodes in hardware with its audio off. A Core picture-sync loop compares mpv's presented position with `clock − picture delay` and steers `speed`, with seek for large errors. Pause, resume, seek and start are aligned explicitly. A spike on the target PC verifies the precision first.
2. **Own FFmpeg decode-and-present path.** D3D11VA decode, an own swapchain, and per-frame scheduling against the master clock, with presentation times read from the swapchain.

## Decision outcome

**Chosen:** Option 1. The two clocks share one time base, so the loop's job is mostly start, transport and the 0.2 % moves, which mpv's `speed` and `seek` cover. libmpv brings hardware decoding, colour management, HDR handling and the subtitle renderer step 11 needs. Option 2 costs weeks and turns the step into XL.

**Verification gate (D1 stays open until it passes).** The first implementation task is a spike on the target PC. It drives libmpv through the `IVideoRenderer` adapter with the Core loop. **Kill criterion** — the spike fails if, on a 1080p HEVC file with hardware decoding, any of these holds:
- in steady playback, the picture is presented more than 20 ms from due;
- after pause, resume or seek, the picture is not back within 20 ms of due within 1 s;
- a 0.2 % speed move shows a visible jump or repeated frame;
- more than 1 frame per minute is late or dropped.

On failure, the fallback is option 2 behind the same port, recorded as a superseding ADR, with the step re-sized to XL.

## Consequences

**Positive**
- Core owns timing decisions; the adapter only reports positions and applies commands, so the picture-sync rules are unit-tested with a fake renderer and a fake clock.
- Hardware decoding, colour and subtitle rendering come from a maintained engine.
- The fallback is contained: a different `IVideoRenderer` adapter in `DualPlayer.Media`, with Core, App and the tests unchanged.

**Negative**
- Precision is bounded by what mpv reports: presented-position readings are frame- and vsync-granular, and the "presented vs due" log (AC-17) is an estimate, not a swapchain statistic.
- A second native LGPL dependency (`libmpv-2.dll`) to ship and load; on the dev Mac it comes from Homebrew.
- Two decoders open the same file: FFmpeg for audio (step 3) and mpv for video.

**Neutral**
- The spike's measurements go into the feature's `evidence/`, and roadmap D1 is marked decided with a link here.

## Links

- Spec: [[../spec.md]] §1, §6, §8 (D1), AC-02, AC-14, AC-17
- SAD: [[../sad.md]] §4
- Related ADR: [[0003-embed-mpv-video-output-in-a-native-child-window-and-draw-notes-with-its-osd]], [[0005-hold-faster-outputs-back-with-a-target-lag-moved-by-the-drift-resampler]]
