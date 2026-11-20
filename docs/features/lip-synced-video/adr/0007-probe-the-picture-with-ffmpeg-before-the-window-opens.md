---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-20"
feature_size: "L"
ticket: ""
---

# 0007 — Probe the picture with FFmpeg before the window opens

- **Status:** Accepted
- **Date:** 2026-11-20
- **Deciders:** Volodymyr Kravchuk

## Context

A file with no picture, or with a picture this PC cannot decode in hardware, must be refused before any window opens (AC-02). A neighbouring file like that must be skipped before the player switches to it (AC-09b). mpv only learns whether hardware decoding works after it has a window and a GPU context. FFmpeg is already loaded in `DualPlayer.Media` for audio decoding (FFmpeg.AutoGen 8.1.0).

## Decision drivers

- Spec AC-02: nothing plays, no window stays open, and the Developer is told the format.
- Spec AC-09b: skip the neighbour and say why; the current film is not disturbed by the check.
- Spec §6: start ≤ 3 s; next and previous within the start budget, even in a folder with thousands of files.
- Spec §6.1: a hostile file is refused and never crashes or hangs the player.

## Considered options

1. **FFmpeg probe, with mpv as a backstop.** `IPictureProbe` in Media opens the file, finds the video stream, creates a D3D11VA hardware device and decodes the first frame in hardware, with no window. If mpv later reports that hardware decoding is not active (`hwdec-current` = no), that is treated as a picture decode failure (AC-06b path, or a skip during next/previous).
2. **mpv only, in a hidden window.** Create the window hidden, load the file in mpv, show the window only once hardware decoding is confirmed; checking a neighbour needs a second mpv instance or stopping the current film.

## Decision outcome

**Chosen:** Option 1. It answers before any window exists and checks a neighbour without touching the playing film. It reuses a library already loaded, and it can be tested on its own with sample files. The backstop covers the case where the two FFmpeg builds (ours and libmpv's) disagree.

## Consequences

**Positive**
- AC-02 and AC-09b are decided before any window or renderer state changes.
- The probe runs off the UI thread with a time limit, so a hostile file cannot hang the start (spec §6.1).

**Negative**
- Two FFmpeg builds can disagree about a codec or profile; the backstop turns a disagreement into an AC-06b-style error instead of software playback.
- The first hardware frame is decoded twice (probe, then mpv), adding tens of milliseconds to the start budget.

**Neutral**
- On the dev Mac the probe uses VideoToolbox in place of D3D11VA; the dev path is not held to spec §6 targets.

## Links

- Spec: [[../spec.md]] AC-02, AC-06b, AC-09b, §6, §6.1
- SAD: [[../sad.md]] §5
- Related ADR: [[0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port]]
