---
id: T4
title: "Validate output assignments and resolve devices by list number or OS id"
layer: "domain"
deps: ["T1"]
blocks: ["T11"]
acs: ["AC-02", "AC-03", "AC-05"]
files_hint: ["src/DualPlayer.Core/Outputs/OutputAssignment.cs", "src/DualPlayer.Core/Outputs/OutputAssignmentValidator.cs", "src/DualPlayer.Core/Outputs/DeviceCatalog.cs", "tests/DualPlayer.Core.Tests/Outputs/OutputAssignmentValidatorTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "M"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T4 — Validate output assignments and resolve devices by list number or OS id

## Place in the sequence

- **Blocked by:** T1 — Define the audio engine ports, value types and test fakes in Core
- **Blocks:** T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss
- **Wave:** 2 — after its deepest dependency finishes (wave 1).
- **Lane:** own lane.

## Why (user story)

> **As a** Developer
> **I want** to start a file with each audio output assigned its own track and its own device
> **So that** each Viewer hears their own track from one shared start
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

> **As a** Developer
> **I want** to list the connected devices and the tracks inside a file
> **So that** I can choose the inputs for a run without guessing device ids or track numbers
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

This task turns the Developer's `track=device` choices into resolved outputs, or into one plain-language refusal before anything opens.

## Inlined context

> - One audio output per device: a device can never be assigned to two audio outputs at once.
> - The master clock never depends on any single device: losing one device can never stop the other audio outputs.
>
> — `CONTEXT.md §Invariants, verbatim` · full text: [CONTEXT.md](../../../../CONTEXT.md)

> src/DualPlayer.Core/
> ├── Playback/      engine facade, transport state machine (stopped/playing/paused/ended), run lifecycle, commands
> ├── Sync/          master clock, drift controller (PI), adaptive resampler, one-step re-alignment
> ├── Outputs/       audio output model, output assignment validation ("one audio output per device"), ring buffer
> ├── Diagnostics/   per-device sync record, excursion tracker, run summary, counters
> └── ports          IAudioDecoder (Playback/), IAudioBackend + IAudioDevice (Outputs/)
>
> — `sad.md §5, Internal decomposition, abridged (Core only)` · full text: [sad.md](../sad.md)

> **Hard rule — Error handling.** Expected failures (file unreadable, track or device invalid, access denied, device lost) are `Result` / `Result<T>` or engine events with a plain-language message for the console; exceptions are bugs, logged at the top of each background loop (decode, control, console); a bug in one render loop stops that output only
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

> | Code | When | Exit |
> |---|---|---|
> | `playback.file_unreadable` | File cannot be opened as media | 2 |
> | `playback.track_not_found` | Track number not among the file's audio tracks | 2 |
> | `outputs.device_not_found` | Device number or id not a connected playback device | 2 |
> | `outputs.device_assigned_twice` | Same device on two outputs — "one audio output per device" | 2 |
> | `outputs.device_access_denied` | OS refused access to a device | 4 |
>
> All errors print to standard error as `error <code>: <plain-language message>`; the same `code` travels in the Core `Error(Code, Message)` record (`src/DualPlayer.Core/Result.cs`).
>
> — `contracts/cli.md §4, Error codes, abridged` · full text: [cli.md](../contracts/cli.md)

> ```
> #  name                    type        id
> 1  Test Headset A          bluetooth   {0.0.0.00000000}.{00000000-0000-0000-0000-000000000001}
> ```
> Devices are sorted by OS endpoint id, so the same set of connected devices always gets the same numbers in `devices` and in a later `play`; if a device is connected or removed in between, numbers may shift — the full id is always accepted, and `play` echoes the id each number resolved to (AC-02).
>
> — `contracts/cli.md §1.1 `devices`, abridged` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

### AC-02 — happy path

> **Given** devices are connected and the Developer has a media file
> **When** the Developer asks for the device list and the track list
> **Then** the console shows every playback device the operating system currently offers (name, device type when known, device id) and every audio track of the file (number, language, channel layout), and nothing starts playing. Tracks are numbered from 1 among the file's audio tracks only; the Developer names a device either by its number in this list or by its full operating-system id, and when playback starts the console echoes the id each output resolved to
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — error

> **Given** the Developer names a file that cannot be opened as media, a track number the file does not have, or a device that is not connected
> **When** the Developer starts playback
> **Then** no audio output starts and the console names the invalid input and the reason in plain language (for example: track 3 does not exist, the file has tracks 1 and 2)
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

### AC-05 — domain invariant

> **Given** the Developer assigns the same device to two audio outputs
> **When** the Developer starts playback
> **Then** the system refuses to start and names the rule "one audio output per device"
>
> — `spec.md §5, AC-05, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `DeviceCatalog` — the backend's devices sorted by OS endpoint id and numbered from 1 — `src/DualPlayer.Core/Outputs/DeviceCatalog.cs`
- [ ] `OutputAssignment(TrackNumber, DeviceRef)` where `DeviceRef` is a list number or a full id — `OutputAssignment.cs`
- [ ] `OutputAssignmentValidator.Validate(tracks, catalog, assignments)` → `Result<IReadOnlyList<ResolvedOutput>>` with the resolved device id per output; first invalid input wins — `OutputAssignmentValidator.cs`
- [ ] Messages in plain language, e.g. `track 3 does not exist, the file has tracks 1 and 2`; duplicate device names the rule `one audio output per device`
- [ ] Unit tests for every row of Edge cases — `tests/DualPlayer.Core.Tests/Outputs/OutputAssignmentValidatorTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Track number 0 or above the track count | `playback.track_not_found` listing the existing track numbers |
| Device number out of range or unknown id | `outputs.device_not_found` |
| Same device given once by number and once by id | resolves to the same id → `outputs.device_assigned_twice` |
| Same track on two different devices | valid |

## Definition of Done

- [ ] `OutputAssignmentValidatorTests` pass for every Edge-case row and for AC-05
- [ ] resolved outputs carry the full OS id (echoed later by the console, AC-02)
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
