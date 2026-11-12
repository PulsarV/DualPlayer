---
id: T17
title: "Render each Windows device on its own MMCSS thread with the device position and the loss signal"
layer: "infra"
deps: ["T16"]
blocks: ["T22"]
acs: ["AC-06", "AC-07"]
files_hint: ["src/DualPlayer.Platform.Windows/Audio/WasapiAudioDevice.cs", "src/DualPlayer.Platform.Windows/Audio/WasapiRenderThread.cs", "src/DualPlayer.Platform.Windows/Audio/QpcTimeBase.cs", "tests/DualPlayer.Platform.Windows.Tests/WasapiRenderTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "M"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T17 — Render each Windows device on its own MMCSS thread with the device position and the loss signal

## Place in the sequence

- **Blocked by:** T16 — List Windows playback devices and open them in shared mode, reporting access denial
- **Blocks:** T22 — Record the ≥ 3 h evidence run on the target Windows PC and commit its run summary
- **Wave:** 3 — after its deepest dependency finishes (wave 2).
- **Lane:** shares `src/DualPlayer.Platform.Windows/Audio/WasapiAudioDevice.cs` with T16 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to keep hearing my track when the other Viewer's device disappears
> **So that** someone else's device problem never interrupts my viewing
>
> — `spec.md §4, US-05, verbatim` · full text: [spec.md](../spec.md)

This task is the Windows half of drift measurement and fault isolation: each device on its own thread, reporting where it really is, and saying when it is gone.

## Inlined context

> **Chosen — Pull:** the backend opens the device, owns its render thread and, whenever the device needs data, calls a Core-provided source `Render(buffer, devicePosition)` where `devicePosition` carries the frames the device has played and the monotonic timestamp of that reading; device loss and access denial arrive as backend events.
>
> — `adr/0004 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0004](../adr/0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position.md)

> - Core code runs on the backend's real-time thread: `Render` must not allocate, lock or block (rule in §8).
> - The position timestamp must come from the same monotonic source as the master clock's `TimeProvider`; each adapter converts its OS timestamp (QPC on Windows, host time on macOS) to that base.
> - The backend also reports the device's shared-mode mix format when a stream opens; Core passes it to the decoder as the output's target format (ADR-0005).
>
> — `adr/0004 §Consequences, Negative + Neutral, verbatim` · full text: [ADR-0004](../adr/0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position.md)

> **Chosen:** One decode thread, one ring buffer per output, one render thread per device — the decode thread demuxes once, decodes each chosen track, converts it to its device's format and writes into each output's single-producer/single-consumer ring buffer holding several seconds of audio; each device's event-driven render thread reads only its own ring.
>
> — `adr/0002 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0002](../adr/0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers.md)

> **Hard rule — Real-time code.** **Override:** code called on a render thread (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws; it only reads its ring and updates atomic counters; faults are reported to the control loop as events
>
> — `sad.md §8, Real-time code, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — Error handling.** Expected failures (file unreadable, track or device invalid, access denied, device lost) are `Result` / `Result<T>` or engine events with a plain-language message for the console; exceptions are bugs, logged at the top of each background loop (decode, control, console); a bug in one render loop stops that output only
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

> Tests needing real audio hardware use `[HardwareFact]` (`tests/Shared/HardwareFactAttribute.cs`) — skipped unless `DUALPLAYER_HARDWARE=1`.
>
> — `CLAUDE.md §Tests, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-06 — domain invariant

> **Given** two audio outputs are playing
> **When** the second output's device disappears (switched off, unplugged, out of range)
> **Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically. With three or more outputs, each further loss behaves the same way
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

### AC-07 — happy path (long run)

> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `WasapiRenderThread`: dedicated thread per device, `AvSetMmThreadCharacteristics("Pro Audio")`, waits on the WASAPI event — `src/DualPlayer.Platform.Windows/Audio/WasapiRenderThread.cs`
- [ ] Each period: padding → frames to write, `GetBuffer`, `IAudioClock.GetPosition(out pos, out qpc)` → `DevicePosition(FramesPlayed, Timestamp)`, call `IRenderSource.Render`, `ReleaseBuffer`
- [ ] `QpcTimeBase` converts the QPC reading (100 ns units) to the `TimeProvider.System` timestamp base — `QpcTimeBase.cs`
- [ ] Device invalidated (or event-wait timeout) → stop the thread, raise device lost once; an exception in the loop is logged at the top and stops that output only
- [ ] `Start()` / `Close()` on `WasapiAudioDevice` — `WasapiAudioDevice.cs`
- [ ] Unit test for `QpcTimeBase`; `[HardwareFact]` render test — `tests/DualPlayer.Platform.Windows.Tests/WasapiRenderTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Headset switched off mid-run | device lost raised once; other device threads unaffected |
| Event wait times out (driver hang) | treated as loss for that device only |
| Position readings | monotonic `FramesPlayed`; timestamps on the same base as the master clock |

## Definition of Done

- [ ] `QpcTimeBase` unit test passes on `windows-latest` CI
- [ ] `[HardwareFact]` render test passes on the target PC: 10 s of silence rendered, positions monotonic, loss raised when the device is disabled
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
