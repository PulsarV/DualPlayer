---
id: T19
title: "Parse and validate the play start command in the App executable before any window opens"
layer: "ports"
deps: ["T9"]
blocks: ["T21"]
acs: ["AC-02", "AC-03", "AC-03b"]
files_hint: ["src/DualPlayer.App/Startup/StartCommand.cs", "src/DualPlayer.App/Startup/StartExitCodes.cs", "src/DualPlayer.App/Startup/IScreenCatalog.cs", "src/DualPlayer.App/DualPlayer.App.csproj", "tests/DualPlayer.App.Tests/Startup/StartCommandTests.cs"]
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

# T19 — Parse and validate the play start command in the App executable before any window opens

## Place in the sequence

- **Blocked by:** T9 — Probe a file's picture with FFmpeg in hardware, off the caller's thread, with a time limit
- **Blocks:** T21 — Print start lines, warnings, running lines and run summaries to the terminal and the log
- **Wave:** 5 — after its deepest dependency finishes (wave 4).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task is the Developer's surface: a start command that refuses bad input in plain words, before anything plays or any window appears.

## Inlined context

> **App executable takes the start command** — `DualPlayer.App` parses the start arguments, is built as a console-subsystem executable for this step, prints errors, summaries and exit codes to its terminal, and opens the fullscreen window. Surfaces `[desktop-app, cli]`.
> App now has two jobs: parse and report like a command-line tool, and run a window. Startup validation must finish before the window opens (AC-02, AC-03b: no window stays open on error).
>
> — `adr/0001 §Considered options option 1 (chosen) + §Consequences negative, abridged` · full text: [ADR-0001](../adr/0001-host-the-start-command-in-the-app-executable.md)

> Normal output goes to standard output; errors go to standard error as `error <code>: <plain-language message>`, with the same `code` as the Core `Error(Code, Message)` record (`src/DualPlayer.Core/Result.cs`). Every printed line is also written to the log.
>
> — `contracts/cli.md §Conventions, output, verbatim` · full text: [cli.md](../contracts/cli.md)

> Device and track arguments follow the step-3 console: a device is a number from `DualPlayer.Console devices` or a full OS endpoint id; a track is a number among the file's audio tracks, from 1 (`DualPlayer.Console tracks <file>`). This step adds no listing command.
>
> — `contracts/cli.md §Conventions, devices and tracks, verbatim` · full text: [cli.md](../contracts/cli.md)

> **Hard rule:** Expected runtime failures (file unreadable, no picture, device lost, decode error) travel as `Result` / `Result<T>` (`src/DualPlayer.Core/Result.cs`, dotted lowercase error codes) or a domain event — never thrown across module boundaries; exceptions mean bugs and are logged at the top of each background loop.
>
> — `sad.md §2, Conventions, errors, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

`DualPlayer.App play <file> --out <track>=<device> [--out …] [--screen <n>]`

| Argument / option | Meaning | Constraint | Source |
|---|---|---|---|
| `<file>` | Video file to play | must be readable, have a picture, and its first picture must decode in hardware within the start budget | AC-01, AC-02, AC-03, spec §6.1 |
| `--out <track>=<device>` | One audio output; outputs are numbered 1…3 in the order given (window keys 1/Q, 2/W, 3/E) | repeatable, 1 to 3; track ∈ the file's audio tracks; a device may appear once only; the same track may appear on several outputs | spec §5 key table, AC-01, CONTEXT invariant "one audio output per device" |
| `--screen <n>` | Screen for the fullscreen window, numbered from 1 as listed in a `start.screen_not_found` error | optional; default = the main screen | AC-01, AC-03b |

Order of checks, all before any window opens or any device plays (flow 1, ADR-0007): arguments → file access → file readable → picture present and hardware-decodable → tracks → devices → screen. The first failure prints its error and exits; nothing plays and no window stays open.

| Code | When | Exit |
|---|---|---|
| `cli.usage` | Missing or malformed arguments, including `--screen` not a positive integer | 2 |
| `playback.file_access_denied` | The OS denied the App read access to the file (AC-03) | 4 |
| `playback.file_unreadable` | The file cannot be opened as media | 2 |
| `video.no_picture` | The file has no video stream (AC-02) | 2 |
| `video.not_hardware_decodable` | The picture's format (codec + profile named in the message) cannot be decoded in hardware on this PC, or its first picture did not decode within the start budget (AC-02, spec §6.1) | 2 |
| `playback.track_not_found` | Track number not among the file's audio tracks | 2 |
| `outputs.device_not_found` | Device number or id not a connected playback device | 2 |
| `outputs.device_assigned_twice` | Same device on two outputs | 2 |
| `outputs.too_many` | More than three `--out` options (the window has keys for three outputs) | 2 |
| `outputs.device_access_denied` | The OS refused access to a device | 4 |
| `start.screen_not_found` | `--screen` names a screen that is not connected; the message lists the connected screens with their numbers and resolutions (AC-03b) | 2 |
| `mpv.not_found` / `mpv.load_failed` | The video renderer library is missing or the wrong version | 2 |

```
error start.screen_not_found: screen 3 was not found. Connected screens: 1 (1920x1080, main), 2 (3840x2160)
```

— `contracts/cli.md §1.1 + §4 + §5, abridged (Status column dropped)` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

### AC-02 — error

> **Given** a start command whose file has no picture, or whose picture the PC cannot decode
> **When** the start command is given
> **Then** nothing plays, no window stays open, and the Developer is told in plain words that the file has no picture or that its picture format cannot be played on this PC, naming the format; a picture this PC can decode only in software counts as "cannot decode" in this step (software decoding is roadmap step 6)
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — authorization

> **Given** a start command naming a file the operating system does not let the player read
> **When** the start command is given
> **Then** nothing plays and the Developer is told that the operating system denied the player access to that file, so they can fix the permission and retry
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

### AC-03b — error

> **Given** a start command naming a screen that is not connected
> **When** the start command is given
> **Then** nothing plays, no window opens, and the Developer is told that the named screen was not found, with the list of connected screens
>
> — `spec.md §5, AC-03b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Switch `DualPlayer.App` to a console-subsystem executable (`OutputType` `Exe`) — `src/DualPlayer.App/DualPlayer.App.csproj`
- [ ] `StartCommand.Parse(args)` → `StartRequest` or `cli.usage`; `Validate` in the contract's order using `IPictureProbe`, the step-3 track listing and `OutputAssignmentValidator`, and `IScreenCatalog` — `src/DualPlayer.App/Startup/StartCommand.cs`
- [ ] `IScreenCatalog` (numbered screens with resolution and «main» flag); the Avalonia-backed implementation lands in T22 — `src/DualPlayer.App/Startup/IScreenCatalog.cs`
- [ ] Exit codes 0/1/2/4 and `error <code>: <message>` on stderr — `src/DualPlayer.App/Startup/StartExitCodes.cs`
- [ ] Tests with fakes for probe, devices and screens — `tests/DualPlayer.App.Tests/Startup/StartCommandTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Screen enumeration needs Avalonia | must not show a window; the catalog is queried before any window is created (ADR-0001) |
| Same track on two outputs | allowed (cli §1.1) |
| Four `--out` options | `outputs.too_many`, exit 2 |
| Several problems at once | only the first in the contract's order is reported |

## Definition of Done

- [x] `StartCommandTests` pass for every pre-start error row and the check order
- [x] `DualPlayer.App` is a console-subsystem executable
- [x] every Hard Rule inlined above still holds
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
