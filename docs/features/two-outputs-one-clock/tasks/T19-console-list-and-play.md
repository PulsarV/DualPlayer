---
id: T19
title: "Console: list devices and tracks, and start a run from command-line arguments"
layer: "ports"
deps: ["T2", "T11"]
blocks: ["T20", "T21"]
acs: ["AC-01", "AC-02", "AC-03", "AC-04", "AC-05"]
files_hint: ["src/DualPlayer.Console/Program.cs", "src/DualPlayer.Console/ConsoleCompositionRoot.cs", "src/DualPlayer.Console/Commands/CommandLine.cs", "src/DualPlayer.Console/Commands/DevicesCommand.cs", "src/DualPlayer.Console/Commands/TracksCommand.cs", "src/DualPlayer.Console/Commands/PlayCommand.cs", "src/DualPlayer.Console/Rendering/ConsoleFormat.cs", "tests/DualPlayer.Console.Tests/ListAndPlayCommandTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "M"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T19 — Console: list devices and tracks, and start a run from command-line arguments

## Place in the sequence

- **Blocked by:** T2 — Create the DualPlayer.Console executable with its own composition root and capped logging, T11 — Run the engine-control loop: start a run, stop it, and end it on file end or device loss
- **Blocks:** T20 — Console: pause, resume, seek and stop a running run from standard input, T21 — Console: print sync records, excursions, device loss and the run summary, also on Ctrl+C and window close
- **Wave:** 5 — after its deepest dependency finishes (wave 4).
- **Lane:** shares `src/DualPlayer.Console/Program.cs` with T2 — serialized; shares `src/DualPlayer.Console/Commands/PlayCommand.cs` with T20 — serialized; shares `src/DualPlayer.Console/Program.cs` with T21 — serialized.

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

This task is the Developer's entry point: choose inputs from real lists and start a run, or get one plain-language refusal.

## Inlined context

> **Hard rule — Error handling.** Expected failures (file unreadable, track or device invalid, access denied, device lost) are `Result` / `Result<T>` or engine events with a plain-language message for the console; exceptions are bugs, logged at the top of each background loop (decode, control, console); a bug in one render loop stops that output only
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

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

> Prints every audio track of the file, numbered from 1 among audio tracks only, with language (or `und`) and channel layout. Nothing plays. Exit code `0`.
> ```
> #  language  layout   rate
> 1  ukr       5.1      48000
> 2  eng       stereo   44100
> ```
> Errors: `playback.file_unreadable` (exit `2`).
>
> — `contracts/cli.md §1.2 `tracks <file>`, abridged` · full text: [cli.md](../contracts/cli.md)

> | Argument / option | Meaning | Constraint |
> |---|---|---|
> | `<file>` | Media file to play | must open as media |
> | `--out <track>=<device>` | One audio output: a track number and a device | repeatable, ≥ 1; track ∈ the file's tracks (from 1); device = number from `devices` or full OS id; a device may appear once only; the same track may appear on several outputs |
> | `--ring-ms <n>` | Ring buffer per output | integer, default `2000` |
> | `--start-level-ms <n>` | Audio every ring must hold before the master clock starts | integer, default `500`, ≤ `--ring-ms` |
> | `--device-buffer-ms <n>` | Requested shared-mode device buffer | integer, default `40` |
>
> All inputs are validated before any device opens; on the first invalid input nothing plays (AC-03, AC-05). Devices are then opened in shared mode; if the OS denies one, every device already opened is closed and nothing plays (AC-04).
> ```
> output 1  track 1 (ukr 5.1 -> stereo, LFE dropped)  device Test Headset A  id {…0001}
> output 2  track 2 (eng stereo 44100 -> 48000)        device Test Headset B  id {…0002}
> started   clock 00:00:00.000  start-to-audio 0.8 s
> ```
> The process then stays in the run and reads interactive commands (§1.4) from standard input until the run ends.
> Errors (exit `2` unless stated, nothing plays): `playback.file_unreadable`, `playback.track_not_found`, `outputs.device_not_found`, `outputs.device_assigned_twice`, `outputs.device_access_denied` (exit `4`), `cli.usage` — missing or malformed arguments.
>
> — `contracts/cli.md §1.3 `play`, abridged (Source column dropped)` · full text: [cli.md](../contracts/cli.md)

> | Code | Meaning |
> |---|---|
> | `0` | Listing finished, or a run ended with a summary (file end, `stop`, Ctrl+C, window closed, seek past the end) |
> | `1` | Unexpected failure (a bug); logged with its exception |
> | `2` | Refused before start: invalid file, track, device, duplicate device, or usage |
> | `3` | Run ended because every output's device was lost; summary printed |
> | `4` | Refused before start: the OS denied access to a device |
>
> — `contracts/cli.md §3, Exit codes, abridged (Source column dropped)` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

### AC-01 — happy path

> **Given** the Developer has a media file with at least two tracks and two connected devices
> **When** the Developer starts playback with one track assigned to the first device and another track assigned to the second device
> **Then** both audio outputs start together from the beginning of the file, each Viewer hears the assigned track, and the console shows for every output its track, its device and the current master-clock position. The same track may be assigned to more than one audio output; only a device is exclusive (AC-05)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

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

### AC-04 — authorization

> **Given** one of the chosen devices is held exclusively by another application, or the operating system otherwise refuses the player access to it
> **When** the Developer starts playback
> **Then** no audio output starts and the console names that device and says the operating system denied the player access to it, so the Developer can free the device and retry
>
> — `spec.md §5, AC-04, verbatim` · full text: [spec.md](../spec.md)

### AC-05 — domain invariant

> **Given** the Developer assigns the same device to two audio outputs
> **When** the Developer starts playback
> **Then** the system refuses to start and names the rule "one audio output per device"
>
> — `spec.md §5, AC-05, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `CommandLine` parser for `devices`, `tracks <file>`, `play <file> --out <t>=<d> … [--ring-ms] [--start-level-ms] [--device-buffer-ms]` → `EngineOptions`; malformed → `cli.usage` — `src/DualPlayer.Console/Commands/CommandLine.cs`
- [ ] `DevicesCommand` / `TracksCommand` print the tables, exit `0` — `DevicesCommand.cs`, `TracksCommand.cs`
- [ ] `PlayCommand` calls `IAudioEngine.StartRun`; prints the per-output start lines (track, conversion, device name, resolved id) and the `started` line; maps `Error.Code` to stderr `error <code>: <message>` and the exit code — `PlayCommand.cs`
- [ ] `ConsoleFormat` — `hh:mm:ss.fff` clock, signed ms, ppm — `src/DualPlayer.Console/Rendering/ConsoleFormat.cs`
- [ ] Register commands in the console root; dispatch from `Program.cs`
- [ ] Tests with the Core fakes substituted in the container — `tests/DualPlayer.Console.Tests/ListAndPlayCommandTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| `--start-level-ms` greater than `--ring-ms` | `cli.usage`, exit 2 |
| No `--out` | `cli.usage`, exit 2 |
| Device given by number | start line echoes the full resolved id |
| Access denied | stderr names the device, exit 4 |

## Definition of Done

- [ ] `ListAndPlayCommandTests` pass for AC-01, AC-02, AC-03, AC-04, AC-05: printed lines, stderr codes and exit codes match `contracts/cli.md`
- [ ] console smoke test resolves the command handlers
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
