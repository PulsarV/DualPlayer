---
id: T1
title: "Define the audio engine ports, value types and test fakes in Core"
layer: "domain"
deps: []
blocks: ["T4", "T7", "T9", "T10", "T14", "T16", "T18"]
acs: ["AC-02", "AC-04", "AC-06"]
files_hint: ["src/DualPlayer.Core/Outputs/IAudioBackend.cs", "src/DualPlayer.Core/Outputs/IAudioDevice.cs", "src/DualPlayer.Core/Outputs/IRenderSource.cs", "src/DualPlayer.Core/Outputs/AudioDeviceInfo.cs", "src/DualPlayer.Core/Outputs/DevicePosition.cs", "src/DualPlayer.Core/Outputs/MixFormat.cs", "src/DualPlayer.Core/Playback/IAudioDecoder.cs", "src/DualPlayer.Core/Playback/AudioTrackInfo.cs", "src/DualPlayer.Core/Playback/EngineOptions.cs", "tests/DualPlayer.Core.Tests/Fakes/", "Directory.Packages.props"]
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

# T1 — Define the audio engine ports, value types and test fakes in Core

## Place in the sequence

- **Blocked by:** nothing (wave 1 root)
- **Blocks:** T4 — Validate output assignments and resolve devices by list number or OS id, T7 — Implement the master clock, drift measurement, PI drift controller and clock-mismatch estimate, T9 — Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting, T10 — Fan one decoder out to the output rings with epochs, full-ring dropping and stall detection, T14 — List a file's audio tracks and decode the chosen ones once with FFmpeg, T16 — List Windows playback devices and open them in shared mode, reporting access denial, T18 — Provide the macOS AudioQueue development backend behind the same port
- **Wave:** 1 — no dependencies — starts immediately.
- **Lane:** shares `Directory.Packages.props` with T3 — serialized; shares `Directory.Packages.props` with T16 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to start a file with each audio output assigned its own track and its own device
> **So that** each Viewer hears their own track from one shared start
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task fixes the contracts every other task builds against: the backend, device, render-source and decoder ports, plus fakes so the engine can be driven without hardware.

## Inlined context

> - `IAudioBackend` lists the playback devices the OS offers right now (`ListDevices()` — name, type when known, endpoint id; AC-02, AC-03) and opens one by id (`Open(deviceId)`), returning an `IAudioDevice`: the open shared-mode stream with its mix format, start/close and loss/denial events. Hotplug (`IDeviceWatcher`) stays with roadmap step 5; this feature does not create it.
> - The device stream is pull-based: the backend opens a device in shared mode, reports its mix format, owns that device's render thread and calls a Core-provided source with a buffer to fill and the device's playback position (frames played + monotonic timestamp); device loss and access denial arrive as backend events.
> - `IAudioDecoder` lists a file's audio tracks and decodes the chosen ones once, converting each output's track to that output's device mix format with FFmpeg swresample — fold-down drops LFE, fewer-channel tracks go to the front channels, no upmix.
>
> — `sad.md §5, Port contracts, abridged (ADR links dropped)` · full text: [sad.md](../sad.md)

> **Chosen — Pull:** the backend opens the device, owns its render thread and, whenever the device needs data, calls a Core-provided source `Render(buffer, devicePosition)` where `devicePosition` carries the frames the device has played and the monotonic timestamp of that reading; device loss and access denial arrive as backend events.
>
> — `adr/0004 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0004](../adr/0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position.md)

> - Core code runs on the backend's real-time thread: `Render` must not allocate, lock or block (rule in §8).
> - The position timestamp must come from the same monotonic source as the master clock's `TimeProvider`; each adapter converts its OS timestamp (QPC on Windows, host time on macOS) to that base.
> - The backend also reports the device's shared-mode mix format when a stream opens; Core passes it to the decoder as the output's target format (ADR-0005).
>
> — `adr/0004 §Consequences, Negative + Neutral, verbatim` · full text: [ADR-0004](../adr/0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position.md)

> Buffer defaults, held in an `EngineOptions` record and overridable by console flags without rebuilding: ring buffer 2 s per output; start level 0.5 s in every ring before the master clock starts; requested shared-mode device buffer 40 ms (event-driven, about 10 ms device period).
>
> — `sad.md §7, Scaling thresholds, verbatim` · full text: [sad.md](../sad.md)

> sad §8 lists "tolerance" inside `EngineOptions`; the contract does not expose it as a flag, because the 10 ms tolerance is a spec §6 target, not a tuning knob. Either drop it from `EngineOptions` or keep it internal — settle in `tasks`.
>
> — `contracts/api-sync-report.md §B, follow-up notes, verbatim` · full text: [api-sync-report.md](../contracts/api-sync-report.md)

> Ports and adapters: `DualPlayer.Core` references only the BCL and `Microsoft.Extensions.*.Abstractions` — no OS, UI or native library; OS code lives in `DualPlayer.Platform.Windows` / `.MacOS`, codec code in `DualPlayer.Media`.
>
> — `sad.md §2, Technical constraints, abridged` · full text: [sad.md](../sad.md)

> **Hard rule — Real-time code.** **Override:** code called on a render thread (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws; it only reads its ring and updates atomic counters; faults are reported to the control loop as events
>
> — `sad.md §8, Real-time code, verbatim` · full text: [sad.md](../sad.md)

**Breakdown decision (settles the api-sync-report follow-up):** keep `DriftTolerance` (10 ms) inside `EngineOptions` as an internal value with no console flag — it is a spec §6 target, not a tuning knob.

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-02 — happy path

> **Given** devices are connected and the Developer has a media file
> **When** the Developer asks for the device list and the track list
> **Then** the console shows every playback device the operating system currently offers (name, device type when known, device id) and every audio track of the file (number, language, channel layout), and nothing starts playing. Tracks are numbered from 1 among the file's audio tracks only; the Developer names a device either by its number in this list or by its full operating-system id, and when playback starts the console echoes the id each output resolved to
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-04 — authorization

> **Given** one of the chosen devices is held exclusively by another application, or the operating system otherwise refuses the player access to it
> **When** the Developer starts playback
> **Then** no audio output starts and the console names that device and says the operating system denied the player access to it, so the Developer can free the device and retry
>
> — `spec.md §5, AC-04, verbatim` · full text: [spec.md](../spec.md)

### AC-06 — domain invariant

> **Given** two audio outputs are playing
> **When** the second output's device disappears (switched off, unplugged, out of range)
> **Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically. With three or more outputs, each further loss behaves the same way
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `IAudioBackend` — `ListDevices()` returning `AudioDeviceInfo(Name, Type?, Id)`, and `Open(deviceId, requestedBuffer, IRenderSource)` returning `Result<IAudioDevice>` (denial is a failure `Result`, not an exception) — `src/DualPlayer.Core/Outputs/IAudioBackend.cs`, `AudioDeviceInfo.cs`
- [ ] `IAudioDevice` — `MixFormat`, `Start()`, `Close()`, `GrantedBuffer`, and a device-lost notification raised once — `src/DualPlayer.Core/Outputs/IAudioDevice.cs`
- [ ] `IRenderSource.Render(Span<float> buffer, DevicePosition position)` and `DevicePosition(FramesPlayed, Timestamp)` on the `TimeProvider` timestamp base — `IRenderSource.cs`, `DevicePosition.cs`
- [ ] `MixFormat(SampleRate, Channels, ChannelLayout)` (float samples) — `MixFormat.cs`
- [ ] `IAudioDecoder` — list tracks of a file as `Result<IReadOnlyList<AudioTrackInfo>>` (number from 1, language or `und`, layout, rate) and open a decode session for chosen tracks with each output's target `MixFormat`, supporting seek and epoch tagging — `src/DualPlayer.Core/Playback/IAudioDecoder.cs`, `AudioTrackInfo.cs`
- [ ] `EngineOptions` record — `RingBuffer` 2 s, `StartLevel` 0.5 s, `DeviceBuffer` 40 ms, internal `DriftTolerance` 10 ms — `src/DualPlayer.Core/Playback/EngineOptions.cs`
- [ ] Fakes: `FakeAudioBackend`, `FakeAudioDevice` (drives `Render` on demand with a clock running ±N ppm against a `FakeTimeProvider`, can raise loss or deny open), `FakeAudioDecoder` (deterministic ramp/sine frames) — `tests/DualPlayer.Core.Tests/Fakes/`
- [ ] Add `Microsoft.Extensions.TimeProvider.Testing` (for `FakeTimeProvider`) to `Directory.Packages.props` and the Core test project only

## Edge cases

| Case | Behaviour |
|---|---|
| Device id unknown to the backend at `Open` | failure `Result` with code `outputs.device_not_found`; no exception |
| OS refuses access at `Open` | failure `Result` with code `outputs.device_access_denied` naming the device |
| Device lost after `Start` | one loss notification, delivered off the render thread; `Render` is never called again for it |
| Fake device clock at +N ppm | `FramesPlayed` advances by `rate × (1 + N·10⁻⁶)` per fake-clock second |

## Definition of Done

- [ ] Core still references only the BCL + `Microsoft.Extensions.*.Abstractions` (no new package in `DualPlayer.Core.csproj`)
- [ ] `tests/DualPlayer.Core.Tests/Fakes/FakeAudioDeviceTests.cs` passes: the fake device reports positions advancing at the configured ppm and raises loss once
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
