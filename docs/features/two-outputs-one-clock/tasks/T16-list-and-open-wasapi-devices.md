---
id: T16
title: "List Windows playback devices and open them in shared mode, reporting access denial"
layer: "infra"
deps: ["T1"]
blocks: ["T17"]
acs: ["AC-02", "AC-04"]
files_hint: ["src/DualPlayer.Platform.Windows/Audio/WasapiAudioBackend.cs", "src/DualPlayer.Platform.Windows/Audio/WasapiAudioDevice.cs", "src/DualPlayer.Platform.Windows/WindowsPlatformServiceCollectionExtensions.cs", "Directory.Packages.props", "src/DualPlayer.Platform.Windows/DualPlayer.Platform.Windows.csproj", "tests/DualPlayer.Platform.Windows.Tests/WasapiAudioBackendTests.cs"]
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

# T16 — List Windows playback devices and open them in shared mode, reporting access denial

## Place in the sequence

- **Blocked by:** T1 — Define the audio engine ports, value types and test fakes in Core
- **Blocks:** T17 — Render each Windows device on its own MMCSS thread with the device position and the loss signal
- **Wave:** 2 — after its deepest dependency finishes (wave 1).
- **Lane:** shares `Directory.Packages.props` with T1 — serialized; shares `Directory.Packages.props` with T3 — serialized; shares `src/DualPlayer.Platform.Windows/Audio/WasapiAudioDevice.cs` with T17 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to list the connected devices and the tracks inside a file
> **So that** I can choose the inputs for a run without guessing device ids or track numbers
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

> **As a** Developer
> **I want** to start a file with each audio output assigned its own track and its own device
> **So that** each Viewer hears their own track from one shared start
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task lets the Developer see the target PC's devices and get a clear refusal when Windows will not hand one over.

## Inlined context

> Audio output sits behind the `IAudioBackend` port: Windows adapter on NAudio's WASAPI layer with the engine's own N-device event-driven render loop and MMCSS "Pro Audio"; a dev-only AudioQueue P/Invoke adapter on macOS that is never shipped and not held to sync targets.
>
> — `sad.md §2, Technical constraints, abridged (ADR link dropped)` · full text: [sad.md](../sad.md)

> Devices are opened in shared mode (the OS mixes the player with other applications); every spec §6 target is measured under that condition (spec §1).
>
> — `sad.md §2, Technical constraints, verbatim` · full text: [sad.md](../sad.md)

> - `IAudioBackend` lists the playback devices the OS offers right now (`ListDevices()` — name, type when known, endpoint id; AC-02, AC-03) and opens one by id (`Open(deviceId)`), returning an `IAudioDevice`: the open shared-mode stream with its mix format, start/close and loss/denial events. Hotplug (`IDeviceWatcher`) stays with roadmap step 5; this feature does not create it.
> - The device stream is pull-based: the backend opens a device in shared mode, reports its mix format, owns that device's render thread and calls a Core-provided source with a buffer to fill and the device's playback position (frames played + monotonic timestamp); device loss and access denial arrive as backend events.
> - `IAudioDecoder` lists a file's audio tracks and decodes the chosen ones once, converting each output's track to that output's device mix format with FFmpeg swresample — fold-down drops LFE, fewer-channel tracks go to the front channels, no upmix.
>
> — `sad.md §5, Port contracts, abridged (ADR links dropped)` · full text: [sad.md](../sad.md)

> - Start-to-audio timing: timestamps of the start command and of the first rendered frame per device in the log (spec §6, ≤ 2 s).
> - Actual shared-mode device buffer and mix format per device logged when each stream opens (the OS may grant a larger buffer than requested).
>
> — `sad.md §7, Monitoring, abridged` · full text: [sad.md](../sad.md)

> The OS grants a larger shared-mode buffer than the requested 40 ms, slowing seek confirmation and pause alignment — Mitigation: Actual buffer logged per device at stream open; `EngineOptions` lets the Developer tune it on the target PC
>
> — `sad.md §11, Risks row 7, abridged` · full text: [sad.md](../sad.md)

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

> ```
> #  name                    type        id
> 1  Test Headset A          bluetooth   {0.0.0.00000000}.{00000000-0000-0000-0000-000000000001}
> ```
> Devices are sorted by OS endpoint id, so the same set of connected devices always gets the same numbers in `devices` and in a later `play`; if a device is connected or removed in between, numbers may shift — the full id is always accepted, and `play` echoes the id each number resolved to (AC-02).
>
> — `contracts/cli.md §1.1 `devices`, abridged` · full text: [cli.md](../contracts/cli.md)

> | `outputs.device_access_denied` | OS refused access to a device | 4 |
>
> — `contracts/cli.md §4, Error codes, verbatim row` · full text: [cli.md](../contracts/cli.md)

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

## Checklist

- [ ] Add NAudio's WASAPI package to `Directory.Packages.props` and the Windows project
- [ ] `WasapiAudioBackend : IAudioBackend` (`[SupportedOSPlatform("windows")]`) — `ListDevices()` over active render endpoints: friendly name, type from the endpoint form factor when known, endpoint id — `src/DualPlayer.Platform.Windows/Audio/WasapiAudioBackend.cs`
- [ ] `Open(id, requestedBuffer, source)` → shared-mode, event-driven `IAudioClient` init; read the mix format; log granted buffer + mix format — `WasapiAudioDevice.cs` (render thread lands in T17)
- [ ] Map HRESULTs: access denied / device in use → `outputs.device_access_denied` naming the device; endpoint not found → `outputs.device_not_found`
- [ ] Register in `AddWindowsPlatform` — `WindowsPlatformServiceCollectionExtensions.cs`
- [ ] Unit tests for HRESULT mapping; `[HardwareFact]` tests for listing and opening the default device — `tests/DualPlayer.Platform.Windows.Tests/WasapiAudioBackendTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Device held in exclusive mode by another app | `outputs.device_access_denied` with the device name |
| Device unplugged between `devices` and `play` | `outputs.device_not_found` |
| OS grants a larger buffer than requested | accepted; actual buffer logged |
| Endpoint form factor unknown | type column empty |

## Definition of Done

- [ ] HRESULT-mapping unit tests pass on `windows-latest` CI
- [ ] `[HardwareFact]` listing/open tests pass on the target PC with `DUALPLAYER_HARDWARE=1`
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
