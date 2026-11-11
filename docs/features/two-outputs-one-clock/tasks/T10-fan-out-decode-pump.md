---
id: T10
title: "Fan one decoder out to the output rings with epochs, full-ring dropping and stall detection"
layer: "app"
deps: ["T1", "T5"]
blocks: ["T11"]
acs: ["AC-01", "AC-06"]
files_hint: ["src/DualPlayer.Core/Playback/DecodePump.cs", "tests/DualPlayer.Core.Tests/Playback/DecodePumpTests.cs"]
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

# T10 — Fan one decoder out to the output rings with epochs, full-ring dropping and stall detection

## Place in the sequence

- **Blocked by:** T1 — Define the audio engine ports, value types and test fakes in Core, T5 — Build the epoch-tagged single-producer/single-consumer ring buffer
- **Blocks:** T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss
- **Wave:** 2 — after its deepest dependency finishes (wave 1).
- **Lane:** own lane.

## Why (user story)

> **As a** Developer
> **I want** to start a file with each audio output assigned its own track and its own device
> **So that** each Viewer hears their own track from one shared start
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

> **As a** Viewer
> **I want** to keep hearing my track when the other Viewer's device disappears
> **So that** someone else's device problem never interrupts my viewing
>
> — `spec.md §4, US-05, verbatim` · full text: [spec.md](../spec.md)

This task feeds every output from one decode, so a slow or lost device can never starve the others.

## Inlined context

> **Chosen:** One decode thread, one ring buffer per output, one render thread per device — the decode thread demuxes once, decodes each chosen track, converts it to its device's format and writes into each output's single-producer/single-consumer ring buffer holding several seconds of audio; each device's event-driven render thread reads only its own ring.
>
> — `adr/0002 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0002](../adr/0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers.md)

> The decoder never waits on a single full ring: while any other ring has room it keeps decoding and drops (and logs) frames for the output whose ring is full. An output that has not read its ring for more than 1 s is marked stalled and no longer paces the decoder; a lost output's ring is detached. If a stalled output resumes, the drift controller re-aligns it in one step. This keeps a hanging device — one not yet reported lost — from starving the others (QG-2 counts the 5 s before a loss too).
>
> — `adr/0002 §Consequences, Neutral, verbatim` · full text: [ADR-0002](../adr/0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers.md)

> One decode thread, one render thread per device (MMCSS "Pro Audio" on Windows), one engine-control loop processing commands and backend events in order, one console input loop
>
> — `sad.md §8, Threading, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — Error handling.** Expected failures (file unreadable, track or device invalid, access denied, device lost) are `Result` / `Result<T>` or engine events with a plain-language message for the console; exceptions are bugs, logged at the top of each background loop (decode, control, console); a bug in one render loop stops that output only
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-01 — happy path

> **Given** the Developer has a media file with at least two tracks and two connected devices
> **When** the Developer starts playback with one track assigned to the first device and another track assigned to the second device
> **Then** both audio outputs start together from the beginning of the file, each Viewer hears the assigned track, and the console shows for every output its track, its device and the current master-clock position. The same track may be assigned to more than one audio output; only a device is exclusive (AC-05)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-06 — domain invariant

> **Given** two audio outputs are playing
> **When** the second output's device disappears (switched off, unplugged, out of range)
> **Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically. With three or more outputs, each further loss behaves the same way
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `DecodePump` on its own dedicated thread: pulls decoded frames per output from the `IAudioDecoder` session and writes them into each attached ring with the current epoch — `src/DualPlayer.Core/Playback/DecodePump.cs`
- [ ] Pacing: wait (not spin) only while every non-stalled ring is full; waits are timed through `TimeProvider`
- [ ] Full ring while another has room → drop that output's frames and log (rate-limited)
- [ ] Stall: an output whose ring was not read for > 1 s is marked stalled and stops pacing; it resumes pacing when reads restart (and is flagged for one-step re-alignment)
- [ ] `Detach(output)` on device loss; `Seek(target, epoch)` re-seeks the decoder and tags new frames; end of file reported to the control loop
- [ ] Exceptions caught and logged at the top of the loop, reported to the control loop as a decode fault
- [ ] Tests with `FakeAudioDecoder` and rings — `tests/DualPlayer.Core.Tests/Playback/DecodePumpTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| One output stops reading | after 1 s it is stalled; others keep filling |
| All rings full | pump waits, no frames dropped |
| Detached output | never written again |
| Decoder error | logged once, control loop notified, thread ends cleanly |

## Definition of Done

- [ ] `DecodePumpTests` pass: one decode feeds N rings, stalled output stops pacing within 1 s, full ring drops only its own frames, detach and seek-epoch tagging
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
