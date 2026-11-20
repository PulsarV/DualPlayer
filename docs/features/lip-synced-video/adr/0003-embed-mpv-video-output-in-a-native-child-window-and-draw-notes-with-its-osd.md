---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-20"
feature_size: "L"
ticket: ""
---

# 0003 — Embed mpv's video output in a native child window and draw notes with its OSD

- **Status:** Accepted
- **Date:** 2026-11-20
- **Deciders:** Volodymyr Kravchuk

## Context

This is the UI-architecture decision for the `desktop-app` surface. The roadmap places the picture in a fullscreen Avalonia window. libmpv can draw into it in two ways. It can use its own video output (on Windows, D3D11) into a native child window it is given. Or it can use the render API, drawing into an OpenGL texture owned by an Avalonia control. `ux-flows.md` hands design the question of where the brief note (SCR-03) is drawn. That choice affects frame timing and what roadmap step 8 can draw over the picture.

## Decision drivers

- QG-3 / spec §6: ≤ 1 frame late or dropped per minute; ≤ 15 % CPU for 1080p HEVC with hardware decoding.
- QG-1 / spec §6: the picture presented within 20 ms of due.
- Spec AC-01, AC-03b: fullscreen on the named screen.
- `ux-flows.md` platform decisions: one window, keyboard only, brief note held 2 s.
- Roadmap step 8 will need UI (bar, output tiles) over the video.
- An mpv-owned window with no Avalonia window is excluded: the roadmap and `sad.md` §2 fix a fullscreen Avalonia window.

## Considered options

1. **mpv's own video output in a native child window; notes via mpv's OSD.** Avalonia `NativeControlHost` provides a child window handle passed to mpv as `wid`. mpv decodes and presents with zero-copy hardware decoding and its own vsync timing. The note is an mpv OSD overlay styled from `Themes/Tokens.axaml`.
2. **libmpv render API in an Avalonia OpenGL control; notes as an Avalonia layer.** Avalonia composes the frame and any overlay. On Windows this goes through ANGLE (OpenGL over D3D11), so hardware decoding likely needs copy-back, and presentation timing follows Avalonia's compositor.

## Decision outcome

**Chosen:** Option 1. It keeps decoding zero-copy and presentation timing inside mpv, which serves the two top quality goals (lip-sync precision and smooth frames at low CPU) better than routing frames through Avalonia's compositor.

## Consequences

**Positive**
- Best available frame pacing and dropped-frame counters, straight from mpv.
- No GPU-to-CPU copy of decoded frames.
- The window code stays small: one view hosting the native child window, key handling and window placement on the chosen screen.

**Negative**
- Airspace: Avalonia cannot draw over the native child window. Step 8's on-screen UI will need either a transparent top-level overlay window over the video or mpv's OSD. That is recorded as a risk in `sad.md` §11.
- The native child window must not take keyboard focus away from the Avalonia window. mpv's input handling is switched off (no default bindings, no keyboard on the video output).
- The note's look is limited to what mpv's OSD can draw (text, size, colour, border), which is enough for SCR-03.

**Neutral**
- On the dev Mac the same option works with mpv's macOS video output; it is not held to spec §6 targets.

## Links

- Spec: [[../spec.md]] AC-01, AC-03b, §6
- UX flows: [[../ux-flows.md]] platform decisions, SCR-02, SCR-03
- SAD: [[../sad.md]] §4
- Related ADR: [[0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port]], [[0001-host-the-start-command-in-the-app-executable]]
