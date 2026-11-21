---
id: T10
title: "Report each device's connection kind, form factor and hidden latency on Windows and the dev Mac"
layer: "infra"
deps: ["T6"]
blocks: ["T22"]
acs: ["AC-13"]
files_hint: ["src/DualPlayer.Platform.Windows/Audio/WasapiLatencyProbe.cs", "src/DualPlayer.Platform.Windows/WindowsPlatformServiceCollectionExtensions.cs", "src/DualPlayer.Platform.MacOS/Audio/CoreAudioLatencyProbe.cs", "src/DualPlayer.Platform.MacOS/MacOSPlatformServiceCollectionExtensions.cs", "tests/DualPlayer.Platform.Windows.Tests/WasapiLatencyProbeTests.cs", "tests/DualPlayer.Platform.MacOS.Tests/CoreAudioLatencyProbeTests.cs"]
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

# T10 — Report each device's connection kind, form factor and hidden latency on Windows and the dev Mac

## Place in the sequence

- **Blocked by:** T6 — Model device types, type defaults, assumed latency, picture delay and lags behind a latency-probe port
- **Blocks:** T22 — Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code
- **Wave:** 5 — after its deepest dependency finishes (wave 4).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** the player to estimate each device's latency from what it knows about the device
> **So that** the first film with a new device is already close to lip-synced before I touch anything
>
> — `spec.md §4, US-05, verbatim` · full text: [spec.md](../spec.md)

This task feeds the latency model with what each OS actually knows about a device — never its name.

## Inlined context

> On WASAPI no such report exists today, so the reported part is 0 ms and is logged as such, with its source. A latency-probe port still returns the reported part, so a later OS facility or platform can fill it.
>
> — `adr/0004 §Considered options, option 1, abridged` · full text: [ADR-0004](../adr/0004-estimate-output-latency-as-the-hidden-latency-beyond-the-device-position.md)

> `src/DualPlayer.Platform.Windows/Audio/WasapiLatencyProbe.cs` — IDeviceLatencyProbe: enumerator / bus + form factor; hidden latency 0 ms
> `src/DualPlayer.Platform.MacOS/Audio/CoreAudioLatencyProbe.cs` — dev-only: Core Audio transport type + device latency
>
> — `sad.md §5, internal decomposition, platform probes, verbatim` · full text: [sad.md](../sad.md)

> Platform.Windows holds the WASAPI backend (enumeration reports name, form factor, id — no latency, no bus)
>
> — `sad.md §3, brownfield note, abridged` · full text: [sad.md](../sad.md)

> **Hard rule:** **Parallel wave 4 ∥ 5 ∥ 6** (`docs/roadmap.md` execution path, wave 3): this step works only in `Core/Playback/`, `Core/Sync/`, `Core/Settings/` (new files), `src/DualPlayer.Media/` and `src/DualPlayer.App/`, plus new files elsewhere. It does not edit `Core/Outputs/` or the device enumeration in `Platform.Windows/` (owned by step 5).
>
> — `sad.md §2, Technical, parallel wave, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-13 — happy path

> **Given** a device that has never been used by the player
> **When** a film is started on it
> **Then** its output's assumed latency is the reported part — the latency the operating system reports for the device beyond the device's own reported playback position, which the master clock already accounts for (0 ms when the operating system reports nothing beyond it) — plus the default for its device type (Bluetooth headphones, Bluetooth speaker, wired, HDMI, other — taken from the connection kind and form factor the operating system reports, never from the device name; any Bluetooth connection counts as Bluetooth headphones unless reported as a speaker, USB counts as wired, audio to a display over HDMI or DisplayPort counts as HDMI, anything not recognised as other), its offset is 0, and the Developer's log shows the reported part, the type default, the type it was taken as, and the resulting picture delay
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `WasapiLatencyProbe` in a **new file**: read the endpoint's enumerator/bus and form factor from its property store by endpoint id; map to connection kind; hidden latency 0 ms, source «none»; `[SupportedOSPlatform("windows")]` — `src/DualPlayer.Platform.Windows/Audio/WasapiLatencyProbe.cs`
- [ ] Register in `AddWindowsPlatform` — `src/DualPlayer.Platform.Windows/WindowsPlatformServiceCollectionExtensions.cs`
- [ ] `CoreAudioLatencyProbe` (dev-only): transport type + device latency — `src/DualPlayer.Platform.MacOS/Audio/CoreAudioLatencyProbe.cs`; register in `AddMacOSPlatform`
- [ ] Keep the mapping a pure function and test it; add `[HardwareFact]` listing tests — `tests/DualPlayer.Platform.Windows.Tests/WasapiLatencyProbeTests.cs`, `tests/DualPlayer.Platform.MacOS.Tests/CoreAudioLatencyProbeTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Device id no longer present | `Result` error; the model treats it like any lost device |
| Property missing in the store | connection kind unknown → device type «other» (AC-13) |
| Existing enumeration code in `Platform.Windows` | not edited (step 5 owns it) — new file only |

## Definition of Done

- [ ] mapping tests pass on both OS test projects; hardware listing tests pass where hardware exists
- [x] no edit to `WasapiAudioBackend.cs` / existing enumeration
- [x] every Hard Rule inlined above still holds
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
