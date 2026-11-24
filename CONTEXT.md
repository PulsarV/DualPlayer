---
status: Living
updated_at: "2026-11-20"
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
- Device type — the latency class a Device is taken as when its latency is estimated — Bluetooth headphones, Bluetooth speaker, wired, HDMI or other — decided from the connection kind and form factor the operating system reports, never from the device name. NOT Device (the endpoint itself; the type is only the class used for its default latency).
- Drift — how far an audio output's playback position has moved away from the master clock, because its device runs slightly fast or slow. NOT Latency (a fixed delay between playing a sample and hearing it, handled in roadmap step 4).
- Latency — the fixed time between the player handing a sound to a device and the Viewer hearing it: the device's buffer plus, for Bluetooth, the codec and the headset; it differs per device. NOT Drift (drift changes slowly during a run; latency is a constant delay).
- Master clock — the single playback timeline every audio output follows; it keeps running when any one device stops. NOT a device's own clock.
- Offset — a Viewer's manual correction to the latency the player assumes for an audio output, in milliseconds, remembered per device; a positive offset makes the output's sound heard later against the picture. NOT Latency (the player estimates the latency; the Viewer only adds or removes a little on top).
- Picture delay — how far the picture is held back behind the master clock so that it lines up with the sound of the slowest audio output: the largest assumed latency (latency minus offset) among the playing outputs; every faster output waits the difference; it may be negative: when every output's assumed latency is below zero, the picture is shown ahead of the master clock. NOT Latency (a device has latency; the picture has a delay chosen to match the largest one).
- Run summary — the per-output account of one run, printed by the console and recorded in the log when playback stops: for every audio output its device, played duration, largest drift, device clock mismatch, number of corrections and underruns, and any device-lost events. NOT the live sync counters (they report the current state while playing; the run summary totals the whole run after it stops).
- Track — one audio stream inside the media file (e.g. the Ukrainian or English dub). NOT Audio output (a track is content, an output is where it plays).
- Underrun — a moment when a device had no audio ready to play, heard as a click, gap or crackle. NOT Drift.
- Viewer — one of the people watching the video, each listening to their own audio output. NOT Developer.

## Invariants

- One audio output per device: a device can never be assigned to two audio outputs at once.
- The master clock never depends on any single device: losing one device can never stop the other audio outputs.
