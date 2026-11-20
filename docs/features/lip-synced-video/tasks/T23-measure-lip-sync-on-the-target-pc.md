---
id: T23
title: "Measure lip-sync per output on the target PC with a click-and-flash video and record the acceptance evidence"
layer: "tests"
deps: ["T22", "T24", "T25", "T26", "T27", "T28", "T29", "T30", "T31", "T32", "T33", "T34", "T35", "T36", "T37", "T38"]
blocks: []
acs: ["AC-16", "AC-01"]
files_hint: ["build/make-click-flash-video.ps1", "docs/features/lip-synced-video/evidence/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
status: "blocked"
step: "owner run on the target PC (script + run guide ready)"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T23 — Measure lip-sync per output on the target PC with a click-and-flash video and record the acceptance evidence

## Place in the sequence

- **Blocked by:** T22 — Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code; T24–T38 — the follow-ups from review-2026-11-20 (the measurement runs on the fixed code)
- **Blocks:** — (last task)
- **Wave:** 11 — after its deepest dependency finishes (wave 10).
- **Lane:** shares `docs/features/lip-synced-video/evidence/` with T5 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to measure, per output, how far the sound is from the picture with a click-and-flash test video filmed by a phone, and see in the log which latency, offset and picture delay the player used
> **So that** I can accept this step on measurements of what the Viewer actually hears, not only on the player's own numbers
>
> — `spec.md §4, US-07, verbatim` · full text: [spec.md](../spec.md)

This task is the step's acceptance: proof from what the Viewer actually hears, not from the player's own numbers.

## Inlined context

| Aspect | Target | Measurement |
|---|---|---|
| Lip-sync per output, after the Viewer's offsets are set | sound no more than 45 ms ahead of and no more than 125 ms behind its picture (ITU-R BT.1359 detectability) | click-and-flash test video filmed with a phone at ≥ 120 frames per second, the phone's own offset subtracted (AC-16), 10 flashes per output, the largest error counts |
| CPU while playing a 1080p HEVC film on two outputs | ≤ 15 % of total CPU (`idea-brief.md` NFR) | operating-system performance counter, average over the run |
| Picture presentation on the target PC | ≤ 1 frame presented late or dropped per minute, averaged over a 2 h film, for 1080p H.264 and HEVC files with hardware decoding; a frame is late when presented more than 20 ms after its due time (the due time follows the picture delay, so a deliberate move at ≤ 0.2 % is not lateness) | the renderer's late and dropped frame counters in the log |

— `spec.md §6, NFR rows lip-sync + CPU + picture presentation, verbatim` · full text: [spec.md](../spec.md)

> - **Lip-sync measurements recorded on the target PC** — baseline: 0, target: one measurement (AC-16) per output for both Bluetooth headsets and one wired or HDMI output, committed under `docs/` before roadmap step 8 is specified.
> - **Offset each device needs on first use** — baseline: unknown, target: recorded per device type in the evidence; used to set the type defaults (§8) and as input to step 12.
>
> — `spec.md §7, KPIs, abridged` · full text: [spec.md](../spec.md)

> Acceptance evidence — the spike results (ADR-0002), the click-and-flash measurements per output (AC-16) and the CPU average from the OS performance counter go to `docs/features/lip-synced-video/evidence/`.
>
> — `sad.md §7, monitoring, acceptance evidence, verbatim` · full text: [sad.md](../sad.md)

> - [ ] Which numbers are the device-type defaults (Bluetooth headphones, Bluetooth speaker, wired, HDMI, other)? Default now: Bluetooth headphones 200 ms, Bluetooth speaker 200 ms, wired 0 ms, HDMI 0 ms, other 0 ms, revised from the first-use measurements (§7). — owner: Volodymyr Kravchuk, due: after the acceptance measurement, before roadmap step 8 is specified
> - [ ] Does the playback position a Bluetooth device reports already include part of its codec and headset latency (carried from `two-outputs-one-clock/spec.md §8`)? Default now: treat it as not included (adopted by `sad.md` ADR-0004); the first-use measurement (§6) settles it. — owner: Volodymyr Kravchuk, due: after the acceptance measurement, before roadmap step 8 is specified
>
> — `spec.md §8, open questions type defaults + BT position, verbatim` · full text: [spec.md](../spec.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-16 — happy path

> **Given** a click-and-flash test video (a white flash on screen and a click on every output at the same moment, once per second) is playing on the target PC with the outputs under test
> **When** the Developer films the screen with a phone, first recording a hand clap close in front of the phone and then holding each output's device to the phone's microphone in turn
> **Then** the clap gives the phone's own offset between sound and picture, which is subtracted from every measurement, and the recording shows, per output, the time between the flash and the click, and the Developer records it in the evidence together with the latency, offset and picture delay the player logged
>
> — `spec.md §5, AC-16, verbatim` · full text: [spec.md](../spec.md)

### AC-01 — happy path

> **Given** a video file with a picture and at least two audio tracks, and the Developer's start command names the file, a track and a device for each Viewer, and a screen
> **When** the start command is given
> **Then** the film opens fullscreen on the named screen (on the main screen when none is named), the picture and every output's sound start together within the start budget (§6), and each Viewer hears their own track; on a device whose offset was set in an earlier run (AC-12) the sound is in step with the lips within the lip-sync budget (§6), on a device's first use the error is only measured (§7)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [x] Script that generates the click-and-flash test video with FFmpeg (white flash + click on every audio track at the same instant once per second; H.264 and HEVC 1080p; ≥ 2 audio tracks) — `build/make-click-flash-video.ps1`
- [x] Run guide for the owner (phone at ≥ 120 fps, hand clap first, each device to the microphone in turn, 10 flashes per output) — `docs/features/lip-synced-video/evidence/`
- [ ] Owner runs it on the target PC: first-use (offset 0) and after setting offsets; 2 h H.264 + HEVC runs for frame counters and CPU; checks every spec §5 key with the video window shown and listens during offset ramps
- [ ] The T22 manual run (folded in from review-2026-11-20 F19): `play` starts fullscreen on the named and on the main screen; refusals print their contract line and exit code with no window (audio-only file, file without read access, missing `--screen`, libmpv renamed away); Esc during the start exits 0 with no error line and no sound afterwards
- [ ] Record the evidence with the logged latency/offset/picture delay per measurement; answer the spec §8 type-default and BT-position questions from it — `docs/features/lip-synced-video/evidence/<date>-target-pc-acceptance.md`

## Edge cases

| Case | Behaviour |
|---|---|
| An output outside 45 ms early / 125 ms late after offsets | acceptance fails: open a `/sdd:fix` against AC-01 with the evidence |
| Type defaults differ from the first-use offsets | revise the defaults in `LatencyModel` with this measurement; Viewers re-correct each device of that type once (AC-12) |
| ITU-R BT.1359 primary text not yet read (spec §8) | confirm before the measurement; until then 45 / 125 ms |

## Definition of Done

- [ ] acceptance evidence committed with every number listed in the DoD sentence
- [ ] spec §8 type-default and BT-position questions answered or re-dated in `spec.md`
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
