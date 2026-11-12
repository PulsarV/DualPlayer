---
id: T20
title: "Console: pause, resume, seek and stop a running run from standard input"
layer: "ports"
deps: ["T19", "T12"]
blocks: ["T22"]
acs: ["AC-08", "AC-09", "AC-09b", "AC-10"]
files_hint: ["src/DualPlayer.Console/Commands/RunCommandLoop.cs", "src/DualPlayer.Console/Commands/PlayCommand.cs", "tests/DualPlayer.Console.Tests/RunCommandLoopTests.cs"]
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

# T20 — Console: pause, resume, seek and stop a running run from standard input

## Place in the sequence

- **Blocked by:** T19 — Console: list devices and tracks, and start a run from command-line arguments, T12 — Pause, resume and seek every output together with playback epochs
- **Blocks:** T22 — Record the ≥ 3 h evidence run on the target Windows PC and commit its run summary
- **Wave:** 6 — after its deepest dependency finishes (wave 5).
- **Lane:** shares `tests/DualPlayer.Console.Tests/` with T2 — serialized; shares `src/DualPlayer.Console/Commands/PlayCommand.cs` with T19 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to pause, resume and seek all audio outputs at once
> **So that** start and stop alignment is proven before video is added
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task gives the Developer live control over a run so pause/seek alignment can be exercised on the target PC.

## Inlined context

> One decode thread, one render thread per device (MMCSS "Pro Audio" on Windows), one engine-control loop processing commands and backend events in order, one console input loop
>
> — `sad.md §8, Threading, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — Error handling.** Expected failures (file unreadable, track or device invalid, access denied, device lost) are `Result` / `Result<T>` or engine events with a plain-language message for the console; exceptions are bugs, logged at the top of each background loop (decode, control, console); a bug in one render loop stops that output only
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

> | Command | Effect |
> |---|---|
> | `pause` | All outputs fall silent at one master-clock instant (within 10 ms of each other); the clock stops. Prints `paused at <clock>` |
> | `resume` | All outputs continue from the paused (or newly sought) position. Prints `resumed at <clock>` |
> | `seek <±seconds>` | Relative seek, e.g. `seek +60`, `seek -10` |
> | `seek <hh:mm:ss>` | Absolute seek |
> | `stop` | Ends the run: outputs fall silent, the run summary prints, the process exits `0` |
>
> Seek behaviour:
> - While playing: prints `seek confirmed at <clock>` only after every output has discarded its old-position audio, including what its device had queued (AC-09, ADR-0006).
> - While paused: prints `position moved to <clock>, still paused` (AC-09b).
> - Before the start: plays from the beginning and prints `warning: target was outside the file, playing from 00:00:00.000` (AC-10).
> - Past the end: prints `warning: target was outside the file`, moves to the end, outputs fall silent and the run ends with the summary, exit `0` (AC-10).
>
> Unknown or malformed commands print `cli.unknown_command` to standard error; the run continues.
>
> — `contracts/cli.md §1.4, Interactive commands, abridged (Source column dropped)` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

### AC-08 — happy path

> **Given** two audio outputs are playing
> **When** the Developer pauses and later resumes playback
> **Then** on pause (and on stop) all outputs fall silent within 10 ms of each other, judged by each device's reported playback position, and the master clock stops; on resume all outputs continue from the same master-clock position and are back within the drift tolerance within the recovery time (§6)
>
> — `spec.md §5, AC-08, verbatim` · full text: [spec.md](../spec.md)

### AC-09 — happy path

> **Given** two audio outputs are playing
> **When** the Developer seeks 10 or 60 seconds forward or backward
> **Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console; the console prints that confirmation only after every output has discarded the audio it had queued from the old position
>
> — `spec.md §5, AC-09, verbatim` · full text: [spec.md](../spec.md)

### AC-09b — happy path (seek while paused)

> **Given** playback is paused
> **When** the Developer seeks
> **Then** the position moves to the new target and playback stays paused with every output silent; on resume all outputs continue from the new position as in AC-08
>
> — `spec.md §5, AC-09b, verbatim` · full text: [spec.md](../spec.md)

### AC-10 — error

> **Given** two audio outputs are playing
> **When** the Developer seeks to a point before the start or past the end of the file
> **Then** a seek before the start plays from the beginning; a seek past the end moves the position to the end of the file at once, all outputs fall silent and the run ends with the run summary (AC-13); in both cases the console tells the Developer the target was outside the file
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `RunCommandLoop` reads standard input on the console input thread; parses `pause`, `resume`, `seek <±seconds>`, `seek <hh:mm:ss>`, `stop` — `src/DualPlayer.Console/Commands/RunCommandLoop.cs`
- [ ] Forwards to `IAudioEngine` and prints `paused at`, `resumed at`, `seek confirmed at`, `position moved to …, still paused`, and the out-of-range warnings
- [ ] Unknown command → `error cli.unknown_command: …` on stderr, run continues
- [ ] Start the loop from `PlayCommand` after the run starts; return when the run ends — `PlayCommand.cs`
- [ ] Tests with a scripted stdin and the fake engine/devices — `tests/DualPlayer.Console.Tests/RunCommandLoopTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| `seek abc` | `cli.unknown_command`, run continues |
| `resume` while playing | reply with current position, no state change |
| `stop` | summary printed (T21), exit 0 |
| Seek past the end | warning, summary, exit 0 |

## Definition of Done

- [ ] `RunCommandLoopTests` pass for AC-08, AC-09, AC-09b, AC-10: printed confirmations and warnings match `contracts/cli.md` §1.4
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
