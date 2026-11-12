---
id: T18
title: "Provide the macOS AudioQueue development backend behind the same port"
layer: "infra"
deps: ["T1"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Platform.MacOS/Audio/AudioQueueBackend.cs", "src/DualPlayer.Platform.MacOS/Audio/AudioQueueDevice.cs", "src/DualPlayer.Platform.MacOS/Audio/CoreAudioInterop.cs", "src/DualPlayer.Platform.MacOS/MacOSPlatformServiceCollectionExtensions.cs", "tests/DualPlayer.Platform.MacOS.Tests/", "DualPlayer.slnx"]
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

# T18 — Provide the macOS AudioQueue development backend behind the same port

## Place in the sequence

- **Blocked by:** T1 — Define the audio engine ports, value types and test fakes in Core
- **Blocks:** nothing (final task)
- **Wave:** 2 — after its deepest dependency finishes (wave 1).
- **Lane:** shares `DualPlayer.slnx` with T2 — serialized; shares `DualPlayer.slnx` with T3 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to start a file with each audio output assigned its own track and its own device
> **So that** each Viewer hears their own track from one shared start
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task lets the Developer hear the engine on the dev Mac; it is never shipped and carries no sync targets.

## Inlined context

> Sync targets on the macOS development backend — it exists for hearing the engine during development and is never shipped (ADR-0002).
>
> — `spec.md §3, Non-goals, verbatim` · full text: [spec.md](../spec.md)

> Audio output sits behind the `IAudioBackend` port: Windows adapter on NAudio's WASAPI layer with the engine's own N-device event-driven render loop and MMCSS "Pro Audio"; a dev-only AudioQueue P/Invoke adapter on macOS that is never shipped and not held to sync targets.
>
> — `sad.md §2, Technical constraints, abridged (ADR link dropped)` · full text: [sad.md](../sad.md)

> **Chosen — Pull:** the backend opens the device, owns its render thread and, whenever the device needs data, calls a Core-provided source `Render(buffer, devicePosition)` where `devicePosition` carries the frames the device has played and the monotonic timestamp of that reading; device loss and access denial arrive as backend events.
>
> — `adr/0004 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0004](../adr/0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position.md)

> - Core code runs on the backend's real-time thread: `Render` must not allocate, lock or block (rule in §8).
> - The position timestamp must come from the same monotonic source as the master clock's `TimeProvider`; each adapter converts its OS timestamp (QPC on Windows, host time on macOS) to that base.
> - The backend also reports the device's shared-mode mix format when a stream opens; Core passes it to the decoder as the output's target format (ADR-0005).
>
> — `adr/0004 §Consequences, Negative + Neutral, verbatim` · full text: [ADR-0004](../adr/0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position.md)

> | `src/DualPlayer.Platform.Windows` | WASAPI render loop, device enumeration/hotplug, BT diagnostics; `[SupportedOSPlatform("windows")]` | Core |
> | `src/DualPlayer.Platform.MacOS` | Dev-only AudioQueue backend — never shipped, not held to sync targets; `[SupportedOSPlatform("macos")]` | Core |
>
> — `CLAUDE.md §Module boundaries, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> Tests needing real audio hardware use `[HardwareFact]` (`tests/Shared/HardwareFactAttribute.cs`) — skipped unless `DUALPLAYER_HARDWARE=1`.
>
> — `CLAUDE.md §Tests, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

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

## Checklist

- [ ] `CoreAudioInterop` P/Invoke for device enumeration and AudioQueue output — `src/DualPlayer.Platform.MacOS/Audio/CoreAudioInterop.cs`
- [ ] `AudioQueueBackend : IAudioBackend` (`[SupportedOSPlatform("macos")]`) — list output devices (name, id = device UID), open by id — `AudioQueueBackend.cs`
- [ ] `AudioQueueDevice` — callback calls `IRenderSource.Render` with the queue timeline position converted from host time to the `TimeProvider` base — `AudioQueueDevice.cs`
- [ ] Register in `AddMacOSPlatform` — `MacOSPlatformServiceCollectionExtensions.cs`
- [ ] New `tests/DualPlayer.Platform.MacOS.Tests/` (in `DualPlayer.slnx`) with `[HardwareFact]` list/play tests

## Edge cases

| Case | Behaviour |
|---|---|
| Device removed during playback | device lost raised once |
| No sync measurement on the Mac | by design — not held to spec §6 targets |

## Definition of Done

- [ ] `[HardwareFact]` tests pass on the dev Mac: devices listed, 2 s of a tone rendered on the default device
- [ ] solution still builds and tests on `windows-latest` (CA1416 guards hold)
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
