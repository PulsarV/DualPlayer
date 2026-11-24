---
id: T7
title: "Persist per-device offsets in device-offsets.json and keep them in an offset book with clamp, step and debounced save"
layer: "infra"
deps: ["T5"]
blocks: ["T13"]
acs: ["AC-10", "AC-11", "AC-12"]
files_hint: ["src/DualPlayer.Core/Settings/DeviceOffsetsFile.cs", "src/DualPlayer.Core/Sync/OffsetBook.cs", "tests/DualPlayer.Core.Tests/Fakes/DeviceOffsetsBuilder.cs", "tests/DualPlayer.Core.Tests/Settings/DeviceOffsetsFileTests.cs", "tests/DualPlayer.Core.Tests/Sync/OffsetBookTests.cs"]
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

# T7 — Persist per-device offsets in device-offsets.json and keep them in an offset book with clamp, step and debounced save

## Place in the sequence

- **Blocked by:** T5 — Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict
- **Blocks:** T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events
- **Wave:** 4 — after its deepest dependency finishes (wave 3).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to shift my own output's sound earlier or later in small steps while the film plays, and have the player remember it for my device
> **So that** I fix a visible mismatch once and never have to fix it again for the same device
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task makes a Viewer's correction stick to their device: bounded, stepped, saved within a second, and used again at the next start.

## Inlined context

> **Own file `device-offsets.json`** — `{ "schemaVersion": 1, "offsets": { "<device id>": <milliseconds> } }`, its own `StateFileDefinition` and source-generated JSON context in `Core/Settings/`.
>
> — `adr/0006 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0006](../adr/0006-keep-device-offsets-in-their-own-versioned-state-file.md)

> **Code shape (follows `AppSettingsFile`):** `Core/Settings/DeviceOffsetsFile.cs` exposes a `StateFileDefinition<DeviceOffsets>` with file name `device-offsets.json`, `CurrentVersion = 1`, default = empty map, and an empty upgrader list — the file is new at v1, so no v0 shape ever existed. A source-generated `JsonSerializerContext` (camelCase, indented), as `SettingsJsonContext` does.
> **Access patterns:**
> - Start (sequence flow 1): load the file once, look up each output's device id → in-memory dictionary, no index needed.
> - Offset key (sequence flow 8): update one entry in memory; save the whole file ≤ 1 s after the last press (debounced).
> - Unreadable or invalid file: start with every offset at 0, warn in the terminal and the log, do not overwrite the file until a Viewer presses an offset key (spec §6.1).
>
> — `data-model.md §Entities, access patterns + code shape, abridged` · full text: [data-model.md](../data-model.md)

> A remembered-offsets file edited by hand with values out of range: the player clamps them to ±500 ms and logs it; values off the 10 ms grid are rounded to the nearest 10 ms and logged. If the file cannot be read, the film still starts with every offset at 0, the Developer is warned in the terminal and the log, and the file is not overwritten until a Viewer presses an offset key.
>
> — `spec.md §6.1, abuse case offsets file, verbatim` · full text: [spec.md](../spec.md)

> unit tests for the offset book (step, range, clamp, debounce with a fake clock); an integration test that presses, waits 1 s on the fake clock, abandons the session with no orderly shutdown, and reloads through a fresh `JsonFileStateStore`.
>
> — `sad.md §10, QG-4 how verify, verbatim` · full text: [sad.md](../sad.md)

> `OffsetBook.cs` — per-device offsets, ±500 ms clamp, 10 ms step, save ≤ 1 s after the last press
>
> — `sad.md §5, internal decomposition, Sync/OffsetBook.cs, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** No `DateTime.Now` / `Stopwatch` in Core — time comes from an injected `TimeProvider` or the audio master clock, so picture and latency logic is testable with a fake clock.
>
> — `sad.md §2, Conventions, time, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** Expected runtime failures (file unreadable, no picture, device lost, decode error) travel as `Result` / `Result<T>` (`src/DualPlayer.Core/Result.cs`, dotted lowercase error codes) or a domain event — never thrown across module boundaries; exceptions mean bugs and are logged at the top of each background loop.
>
> — `sad.md §2, Conventions, errors, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

| Field | JSON type | Constraints | Change |
|---|---|---|---|
| `schemaVersion` | integer | required; `1` | added (new file) |
| `offsets` | object: device id → integer | required; may be empty | added (new file) |
| `offsets` key | string | the OS endpoint id, verbatim | added |
| `offsets` value | integer (ms) | −500…+500, multiple of 10; positive = sound heard later | added |

— `data-model.md §Entities, DeviceOffsets + DeviceOffset, abridged` · full text: [data-model.md](../data-model.md)

No database and no migration pair: the file is new at `schemaVersion` 1 with an empty upgrader list. Fixtures: `DeviceOffsetsBuilder.With(deviceId, offsetMs)` and raw JSON for `900`, `15`, `schemaVersion` `2`, malformed JSON, missing `offsets` (`data-model.md §Test fixtures`).

## API contract

| Code | When | Exit | Status in repo |
|---|---|---|---|
| `state.unreadable` / `state.invalid` / `state.too-new` | Remembered offsets could not be read — printed as a warning, the film starts with offsets 0 | — | exists (`state.too-new` is returned by `JsonFileStateStore`) |
| `offsets.clamped` | A remembered offset was out of range or off the 10 ms grid and was corrected — warning | — | new |

— `contracts/cli.md §5, error codes, rows state.* + offsets.clamped, verbatim` · full text: [cli.md](../contracts/cli.md)

This task returns these as load warnings; printing is T21.

## Acceptance criteria

### AC-10 — happy path

> **Given** a film is playing on two outputs (or is paused or at its end — the new offset is then shown and remembered at once and applied when playback continues)
> **When** a Viewer presses their output's offset key to make the sound later (offset +10 ms) or earlier (offset −10 ms)
> **Then** that output's sound moves by 10 ms per press against the picture, gradually at no more than 0.2 % faster or slower than normal speed (about 5 s per 10 ms) with no click or pitch change heard, while every other output stays within 10 ms of where it was against the picture (if the change moves the picture delay, the picture follows as in AC-14); further presses during the move change its target rather than queue behind it; the picture delay is the largest assumed latency (latency minus offset) among the playing outputs and has no lower limit — when every output's assumed latency is below zero the picture is shown ahead of the master clock, so every "later" press takes effect up to +500 ms; the window shows briefly the output and its new offset, and the next start on the same device uses this offset again (it is saved no later than 1 s after the last press, so a crash of the player or the PC does not lose it)
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

### AC-11 — error

> **Given** an output's offset is already at +500 ms (or −500 ms)
> **When** a Viewer presses the key that would make it later (or earlier)
> **Then** the offset stays at the limit and the window shows briefly that the offset cannot go beyond ±500 ms
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

### AC-12 — cross-context

> **Given** a device had an offset remembered in an earlier run
> **When** a film is started with that device on any output
> **Then** the output starts with the remembered offset, and the Developer's log shows the device, the remembered offset and the latency used; only the offset is remembered — the latency is worked out again at every start (reported part plus the current type default, AC-13), so a revised type default (§8) also shifts devices that already have a remembered offset; a type default is revised only together with an acceptance measurement (§7), the Viewers then re-correct each device of that type once, and AC-01 and the §7 offset-corrections KPI count from the last revision
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `DeviceOffsets` record + `DeviceOffsetsFile` state definition (v1, empty upgraders) + source-generated context — `src/DualPlayer.Core/Settings/DeviceOffsetsFile.cs`
- [ ] `OffsetBook.Load()`: clamp/round each value with a warning per corrected device; on `state.*` errors start empty, keep the warning and mark the file «do not overwrite until a press» — `src/DualPlayer.Core/Sync/OffsetBook.cs`
- [ ] `OffsetBook.Step(deviceId, ±10)` → new offset or «at limit»; debounce a save to ≤ 1 s after the last press via `TimeProvider`; a failed save is reported, the in-memory offset kept
- [ ] `DeviceOffsetsBuilder` — `tests/DualPlayer.Core.Tests/Fakes/DeviceOffsetsBuilder.cs`
- [ ] Tests incl. the QG-4 abandon-and-reload integration test — `tests/DualPlayer.Core.Tests/Settings/DeviceOffsetsFileTests.cs`, `tests/DualPlayer.Core.Tests/Sync/OffsetBookTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Offset at +500 and «later» pressed (or −500 and «earlier») | offset unchanged, result says «at limit» (AC-11) |
| Value `900` / `15` in the file | loaded as `500` / rounded to the nearest 10 ms, each logged with the device id (§6.1) |
| File unreadable, invalid or `schemaVersion` 2 | all offsets 0, `state.*` warning, file untouched until the first press (§6.1) |
| Save fails (disk error) | logged, offset kept for this run (`sad.md §6` flow 8) |
| Several presses within 1 s | one save, ≤ 1 s after the last press |

## Definition of Done

- [x] `DeviceOffsetsFileTests` + `OffsetBookTests` pass, incl. the abandon-and-reload test
- [x] writes go through `IStateStore` (atomic temp + rename)
- [x] every Hard Rule inlined above still holds
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
