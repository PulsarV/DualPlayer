---
id: T6
title: "Model device types, type defaults, assumed latency, picture delay and lags behind a latency-probe port"
layer: "domain"
deps: ["T5"]
blocks: ["T10", "T13"]
acs: ["AC-12", "AC-13"]
files_hint: ["src/DualPlayer.Core/Sync/LatencyModel.cs", "src/DualPlayer.Core/Sync/DeviceType.cs", "src/DualPlayer.Core/Sync/IDeviceLatencyProbe.cs", "tests/DualPlayer.Core.Tests/Fakes/FakeDeviceLatencyProbe.cs", "tests/DualPlayer.Core.Tests/Sync/LatencyModelTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
status: "blocked"
step: "GATE — test assertion contradicts spec (escalated)"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T6 — Model device types, type defaults, assumed latency, picture delay and lags behind a latency-probe port

## Place in the sequence

- **Blocked by:** T5 — Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict
- **Blocks:** T10 — Report each device's connection kind, form factor and hidden latency on Windows and the dev Mac, T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events
- **Wave:** 4 — after its deepest dependency finishes (wave 3).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** the player to estimate each device's latency from what it knows about the device
> **So that** the first film with a new device is already close to lip-synced before I touch anything
>
> — `spec.md §4, US-05, verbatim` · full text: [spec.md](../spec.md)

This task turns what the OS says about each device, plus its offset, into the latency the player assumes and the picture delay every output and the picture follow.

## Inlined context

> **Hidden latency only.** Assumed latency = the latency the OS reports *beyond* the device position + the type default − the offset. On WASAPI no such report exists today, so the reported part is 0 ms and is logged as such, with its source. A latency-probe port still returns the reported part, so a later OS facility or platform can fill it.
>
> — `adr/0004 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0004](../adr/0004-estimate-output-latency-as-the-hidden-latency-beyond-the-device-position.md)

> Offsets are integer milliseconds, step 10, range −500…+500; a positive offset makes sound heard later. Assumed latency `Aᵢ = reported part + type default − offset`; picture delay `PD = max Aᵢ` may be negative; lag `Dᵢ = PD − Aᵢ ≥ 0`. Type defaults (spec §8): Bluetooth headphones 200 ms, Bluetooth speaker 200 ms, wired 0 ms, HDMI 0 ms, other 0 ms, kept in one table in `Core/Sync/LatencyModel`
>
> — `sad.md §8, Units and sign, verbatim` · full text: [sad.md](../sad.md)

> `LatencyModel.cs` — device type → default, assumed latency Aᵢ, picture delay PD = max Aᵢ, lags Dᵢ
> `DeviceType.cs` — Bluetooth headphones / Bluetooth speaker / wired / HDMI / other, from connection kind + form factor
> `IDeviceLatencyProbe.cs` — port: reported hidden latency, connection kind, form factor per device id
>
> — `sad.md §5, internal decomposition, Sync/, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** Ports and adapters: `DualPlayer.Core` references only the BCL and `Microsoft.Extensions.*.Abstractions` — no OS, UI or native library; OS code lives in `DualPlayer.Platform.Windows` / `.MacOS`, codec and video code in `DualPlayer.Media`.
>
> — `sad.md §2, Technical, ports and adapters, abridged` · full text: [sad.md](../sad.md)

> **Hard rule:** No `DateTime.Now` / `Stopwatch` in Core — time comes from an injected `TimeProvider` or the audio master clock, so picture and latency logic is testable with a fake clock.
>
> — `sad.md §2, Conventions, time, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-12 — cross-context

> **Given** a device had an offset remembered in an earlier run
> **When** a film is started with that device on any output
> **Then** the output starts with the remembered offset, and the Developer's log shows the device, the remembered offset and the latency used; only the offset is remembered — the latency is worked out again at every start (reported part plus the current type default, AC-13), so a revised type default (§8) also shifts devices that already have a remembered offset; a type default is revised only together with an acceptance measurement (§7), the Viewers then re-correct each device of that type once, and AC-01 and the §7 offset-corrections KPI count from the last revision
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

### AC-13 — happy path

> **Given** a device that has never been used by the player
> **When** a film is started on it
> **Then** its output's assumed latency is the reported part — the latency the operating system reports for the device beyond the device's own reported playback position, which the master clock already accounts for (0 ms when the operating system reports nothing beyond it) — plus the default for its device type (Bluetooth headphones, Bluetooth speaker, wired, HDMI, other — taken from the connection kind and form factor the operating system reports, never from the device name; any Bluetooth connection counts as Bluetooth headphones unless reported as a speaker, USB counts as wired, audio to a display over HDMI or DisplayPort counts as HDMI, anything not recognised as other), its offset is 0, and the Developer's log shows the reported part, the type default, the type it was taken as, and the resulting picture delay
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `DeviceType` enum + a pure classifier from (connection kind, form factor) following the AC-13 rules; never reads the device name — `src/DualPlayer.Core/Sync/DeviceType.cs`
- [ ] `IDeviceLatencyProbe`: per device id → `Result<DeviceLatencyReport>` (reported hidden latency + its source, connection kind, form factor) — `src/DualPlayer.Core/Sync/IDeviceLatencyProbe.cs`
- [ ] `LatencyModel`: the one type-default table (spec §8 values), per-output estimate (device, type, reported, default, offset, assumed, lag) and `PD`; recompute when an offset changes or an output drops out — `src/DualPlayer.Core/Sync/LatencyModel.cs`
- [ ] `FakeDeviceLatencyProbe` — `tests/DualPlayer.Core.Tests/Fakes/FakeDeviceLatencyProbe.cs`
- [ ] Tests per AC-13 mapping rule and for the arithmetic with 1, 2, 3 outputs and all-negative assumed latencies — `tests/DualPlayer.Core.Tests/Sync/LatencyModelTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| OS reports nothing beyond the device position | reported part 0 ms, still logged as its own term (AC-13) |
| Bluetooth connection reported as a speaker | Bluetooth speaker; any other Bluetooth → Bluetooth headphones (AC-13) |
| USB / HDMI or DisplayPort display audio / unrecognised | wired / HDMI / other (AC-13) |
| Every assumed latency below zero | `PD` negative, lags still ≥ 0 (AC-10: no lower limit) |
| Latency for a device with a remembered offset | worked out again at every start; only the offset comes from the file (AC-12) |

## Definition of Done

- [ ] `LatencyModelTests` pass for every AC-13 mapping rule and the `Aᵢ` / `PD` / `Dᵢ` arithmetic
- [ ] the type-default table exists in exactly one place
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
