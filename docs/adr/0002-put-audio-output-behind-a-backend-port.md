---
status: Accepted
owner: "Architect (project owner)"
reviewers: []
updated_at: "2026-11-11"
feature_size: "XL"
ticket: ""
---

# 0002 — Put audio output behind an IAudioBackend port with WASAPI and dev-only AudioQueue adapters

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** project owner (roadmap review + survey foundation session)

## Context

The accepted engine decodes once and fans audio out to N endpoints under one master clock, correcting per-device drift against each device's clock (`docs/idea-brief.md §7`). WASAPI exists only on Windows, but development happens on macOS.

## Decision drivers

- Development on macOS, target Windows 11 (`docs/idea-brief.md §1`).
- Sync logic must be testable without hardware.
- Per-device clock position and event-driven rendering are required (`docs/idea-brief.md §7`, items 1, 4).

## Considered options

1. **Port + WASAPI adapter (NAudio, own N-device loop) + own AudioQueue P/Invoke for macOS dev** — real sound on both machines.
2. **Port + WASAPI adapter + fake backend only on macOS** — no sound on the Mac.
3. **Both a fake and AudioQueue on macOS.**

## Decision outcome

**Chosen:** option 1. It lets the owner hear the engine on the Mac with no extra dependency; tests use NSubstitute fakes of the port anyway.

## Consequences

**Positive**
- Core sync logic is OS-agnostic and unit-testable.
- WASAPI specifics (MMCSS, IAudioClock, exclusive mode) stay in one adapter.

**Negative**
- Two native audio adapters to maintain; the macOS one is never shipped.
- The macOS path does not validate Windows timing; sync targets are verified only on Windows (roadmap step 12).

**Neutral**
- The port also allows a future Linux adapter.

## Links

- `docs/roadmap.md` Decisions so far · `docs/architecture-map.md` §Module inventory
- NAudio WASAPI: https://github.com/naudio/NAudio/blob/master/NAudio.Wasapi/WasapiOut.cs
