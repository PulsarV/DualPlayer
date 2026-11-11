---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-11"
feature_size: "L"
ticket: ""
---

# 0002 — Fan one decoder out to per-device render threads through per-output ring buffers

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** Volodymyr Kravchuk (project owner), during the design Socratic walk

## Context

The engine decodes a media file once and plays chosen tracks on N devices (spec §1, AC-01). Devices fail independently — a Bluetooth headset can stall or vanish mid-run — and the remaining outputs must not notice (AC-06). The pipeline's threading shape decides how far one device's trouble can spread and how much CPU the run costs.

## Decision drivers

- QG-2 / spec §6: 0 underruns on the surviving output within ±5 s of another device's loss.
- QG-3 / spec §6: 0 underruns per 3 h run with two Bluetooth headsets; ≤ 5 % CPU with two tracks.
- Spec §1: decode the file once (one demux pass for all chosen tracks).
- Repo convention: audio render loops on dedicated threads with MMCSS "Pro Audio".

## Considered options

1. **One decode thread, one ring buffer per output, one render thread per device** — the decode thread demuxes once, decodes each chosen track, converts it to its device's format and writes into each output's single-producer/single-consumer ring buffer holding several seconds of audio; each device's event-driven render thread reads only its own ring.
2. **One decode thread, per-output ring buffers, one shared render thread** servicing every device's event through a multi-object wait.

## Decision outcome

**Chosen:** option 1. A stalled or lost device blocks only its own render thread, which is what QG-2 demands; the deep per-output ring absorbs decoder hiccups (QG-3). Option 2 saves threads but lets one hung device call silence every output.

## Consequences

**Positive**
- Device faults stay inside one thread; the decode thread simply stops feeding a lost output's ring.
- The decoder runs ahead of real time, so a slow demux or disk read never reaches a device.

**Negative**
- N + 1 threads plus the console and engine-control threads.
- Seek must invalidate every ring consistently (ordering defined in §6 and ADR-0006).
- The ring depth adds latency between the decoder and the device, so drift correction must act after the ring (ADR-0003).

**Neutral**
- The decoder never waits on a single full ring: while any other ring has room it keeps decoding and drops (and logs) frames for the output whose ring is full. An output that has not read its ring for more than 1 s is marked stalled and no longer paces the decoder; a lost output's ring is detached. If a stalled output resumes, the drift controller re-aligns it in one step. This keeps a hanging device — one not yet reported lost — from starving the others (QG-2 counts the 5 s before a loss too).

## Links

- Spec: [[../spec.md]] §1, AC-01, AC-06, §6
- SAD: [[../sad.md]] §4, §5
- Related ADR: [[0003-correct-drift-with-a-managed-adaptive-resampler-after-the-ring-buffer]]
