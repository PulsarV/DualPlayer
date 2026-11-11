---
id: T5
title: "Build the epoch-tagged single-producer/single-consumer ring buffer"
layer: "domain"
deps: []
blocks: ["T9", "T10"]
acs: ["AC-09"]
files_hint: ["src/DualPlayer.Core/Outputs/AudioRingBuffer.cs", "tests/DualPlayer.Core.Tests/Outputs/AudioRingBufferTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "S"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T5 — Build the epoch-tagged single-producer/single-consumer ring buffer

## Place in the sequence

- **Blocked by:** nothing (wave 1 root)
- **Blocks:** T9 — Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting, T10 — Fan one decoder out to the output rings with epochs, full-ring dropping and stall detection
- **Wave:** 1 — no dependencies — starts immediately.
- **Lane:** shares `src/DualPlayer.Core/Outputs/AudioRingBuffer.cs` with T9 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to pause, resume and seek all audio outputs at once
> **So that** start and stop alignment is proven before video is added
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task provides the per-output buffer that lets a seek throw away old-position audio without stopping any device.

## Inlined context

> **Chosen:** One decode thread, one ring buffer per output, one render thread per device — the decode thread demuxes once, decodes each chosen track, converts it to its device's format and writes into each output's single-producer/single-consumer ring buffer holding several seconds of audio; each device's event-driven render thread reads only its own ring.
>
> — `adr/0002 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0002](../adr/0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers.md)

> All commands and backend events enter the engine through one engine-control loop in `DualPlayer.Core` that handles them one at a time, so transport state never races. Every seek or stop bumps a playback **epoch**: frames carry the epoch they were decoded in, and a render source discards frames from an older epoch.
>
> — `sad.md §6, intro, abridged` · full text: [sad.md](../sad.md)

> **Hard rule — Real-time code.** **Override:** code called on a render thread (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws; it only reads its ring and updates atomic counters; faults are reported to the control loop as events
>
> — `sad.md §8, Real-time code, verbatim` · full text: [sad.md](../sad.md)

> GC pauses or JIT on render threads cause underruns that look like device problems — Mitigation: §8 real-time rule (no allocation on render threads), pre-allocated rings and buffers, warm-up before the master clock starts; underruns logged with master-clock position for correlation
>
> — `sad.md §11, Risks row 4, abridged` · full text: [sad.md](../sad.md)

> Memory: about 0.8 MB of ring per stereo 48 kHz float output at 2 s — comfortable for the ≥ 3 outputs spec §6 asks for.
>
> — `sad.md §7, Scaling thresholds, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-09 — happy path

> **Given** two audio outputs are playing
> **When** the Developer seeks 10 or 60 seconds forward or backward
> **Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console; the console prints that confirmation only after every output has discarded the audio it had queued from the old position
>
> — `spec.md §5, AC-09, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Pre-allocated interleaved `float` storage sized from capacity × channels; no allocation after construction — `src/DualPlayer.Core/Outputs/AudioRingBuffer.cs`
- [ ] `Write(ReadOnlySpan<float> frames, int epoch)` on the decode thread; returns frames accepted (never blocks)
- [ ] `Read(Span<float> dest, int currentEpoch)` on the render thread: skips frames tagged with an older epoch, reports how many it discarded, returns frames read
- [ ] `FramesAvailable`, `FreeFrames`, and the timestamp of the last read (passed in by the reader) for stall detection
- [ ] Lock-free with `Volatile` / `Interlocked` only
- [ ] Tests — `tests/DualPlayer.Core.Tests/Outputs/AudioRingBufferTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Write larger than free space | partial write, returns the accepted count |
| Read when empty | returns 0 (the caller counts the underrun) |
| Older-epoch frames at the head after a seek | skipped and counted as discarded, never copied to `dest` |
| Wrap-around in the middle of a write or read | frames come out in order, unchanged |

## Definition of Done

- [ ] `AudioRingBufferTests` pass: wrap-around, partial write, epoch discard, and a 1-writer/1-reader stress test with a checksum over ≥ 10 million frames
- [ ] a test asserts `Read` allocates 0 bytes (`GC.GetAllocatedBytesForCurrentThread`)
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
