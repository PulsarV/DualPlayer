---
status: Living
updated_at: "2026-11-11"
---

# Domain Context — DualPlayer

<!--
CONTEXT.md is the domain glossary — not a spec and not a scratch pad. NO implementation
detail here (no datastore/broker/framework names, no API contracts) — only domain words
and the boundaries between them. Implementation choices live in the SAD and ADRs; behaviour
lives in spec.md.
-->

## Glossary

- Audio output — one listening channel of the player: a track played on a device with its own volume and offset. NOT Device (each output plays on its own device; the output is the player's notion, the device is the OS's).
- Correction — the adjustment the engine makes to bring an audio output back to the master clock: reported as the continuous playback-rate adjustment in parts per million, and counted only when the output's position is re-aligned in one step. NOT Drift (drift is the error; correction is the response to it).
- Developer — the project owner who builds, runs and measures the player, including the step-3 console. NOT Viewer (a Developer acts on the engine; a Viewer only watches and listens).
- Device — an audio endpoint the operating system exposes (BT headphones, speakers, HDMI, USB), identified by its OS endpoint id. NOT Audio output.
- Drift — how far an audio output's playback position has moved away from the master clock, because its device runs slightly fast or slow. NOT Latency (a fixed delay between playing a sample and hearing it, handled in roadmap step 4).
- Master clock — the single playback timeline every audio output follows; it keeps running when any one device stops. NOT a device's own clock.
- Track — one audio stream inside the media file (e.g. the Ukrainian or English dub). NOT Audio output (a track is content, an output is where it plays).
- Underrun — a moment when a device had no audio ready to play, heard as a click, gap or crackle. NOT Drift.
- Viewer — one of the people watching the video, each listening to their own audio output. NOT Developer.

## Invariants

- One audio output per device: a device can never be assigned to two audio outputs at once.
- The master clock never depends on any single device: losing one device can never stop the other audio outputs.
