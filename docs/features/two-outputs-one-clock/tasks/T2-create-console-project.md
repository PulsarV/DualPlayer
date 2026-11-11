---
id: T2
title: "Create the DualPlayer.Console executable with its own composition root and capped logging"
layer: "wiring"
deps: []
blocks: ["T19"]
acs: ["AC-11"]
files_hint: ["src/DualPlayer.Console/DualPlayer.Console.csproj", "src/DualPlayer.Console/Program.cs", "src/DualPlayer.Console/ConsoleCompositionRoot.cs", "src/DualPlayer.Core/AppPaths.cs", "src/DualPlayer.App/AppPaths.cs", "src/DualPlayer.App/Program.cs", "src/DualPlayer.App/CompositionRoot.cs", "tests/DualPlayer.Console.Tests/", "tests/DualPlayer.App.Tests/CompositionRootSmokeTests.cs", "DualPlayer.slnx", "CLAUDE.md"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "S"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T2 — Create the DualPlayer.Console executable with its own composition root and capped logging

## Place in the sequence

- **Blocked by:** nothing (wave 1 root)
- **Blocks:** T19 — Console: list devices and tracks, and start a run from command-line arguments
- **Wave:** 1 — no dependencies — starts immediately.
- **Lane:** shares `DualPlayer.slnx` with T3 — serialized; shares `DualPlayer.slnx` with T18 — serialized; shares `src/DualPlayer.Console/Program.cs` with T19 — serialized; shares `tests/DualPlayer.Console.Tests/` with T20 — serialized; shares `src/DualPlayer.Console/Program.cs` with T21 — serialized.

## Why (user story)

> **As a** Developer
> **I want** drift, correction and underrun counters recorded separately for every device
> **So that** roadmap steps 4–6 and the crackling investigation work from data instead of guesses
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task creates the process the Developer runs and makes sure the log it writes keeps every record of a ≥ 3 h run.

## Inlined context

> **Chosen:** option 1 (new `src/DualPlayer.Console` executable with its own composition root calling the same `Add<Module>()` extensions). A real console executable gets native Ctrl+C and close-window signals for AC-13, carries no Avalonia dependency, and keeps the console code out of the couch-UI assembly, while reusing every module registration as-is.
> - A second composition root: the CLAUDE.md rule changes to "`DualPlayer.App` and `DualPlayer.Console` are the only composition roots", and the two can drift in what they register.
> - Drift between the two roots is guarded by a second smoke test (`tests/DualPlayer.Console.Tests/`): the container builds and resolves the console's command loop.
>
> — `adr/0001 §Decision outcome + §Consequences, abridged` · full text: [ADR-0001](../adr/0001-host-the-developer-console-as-a-separate-executable.md)

> **Override:** the `AppPaths` record and its `FromRoot(...)` (no OS check) move from `DualPlayer.App` to `DualPlayer.Core` so both composition roots share it; the OS-specific app-data root (`%APPDATA%` vs `~/Library/Application Support`) stays in the composition roots, next to platform selection, as CLAUDE.md requires
>
> — `sad.md §8, App paths, verbatim` · full text: [sad.md](../sad.md)

> `ILogger<T>` everywhere; Serilog rolling file in the app-data `logs/` folder. **Override:** roll on 20 MB file size (`rollOnFileSizeLimit`) and keep at most 10 files (≤ 200 MB total); a ≥ 3 h run with three devices writes about 7 MB of records, so rollover removes only older runs (spec §6.1). Applied to both composition roots.
>
> — `sad.md §8, Logging, verbatim` · full text: [sad.md](../sad.md)

> Unbounded log growth during multi-hour runs: logs roll over with a size cap, so a long run cannot fill the disk. The cap is large enough to hold every record of at least the current run of ≥ 3 h (AC-07, AC-11); rollover removes only older runs (exact size set in design).
>
> — `spec.md §6.1, Abuse cases, verbatim` · full text: [spec.md](../spec.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

> | `cli.usage` | Missing or malformed command-line arguments | 2 |
>
> — `contracts/cli.md §4, Error codes row 1, verbatim` · full text: [cli.md](../contracts/cli.md)

The full command set lands in T19–T21; this task only answers a missing command with `cli.usage`.

## Acceptance criteria

### AC-11 — cross-context

> **Given** a run is in progress on one or more devices
> **When** playback continues
> **Then** once per second the system publishes, for every device, a record of its current drift, current correction and cumulative underruns, tied to that device's operating-system id and the master-clock position; each record is both raised as an engine event that other parts of the player can subscribe to and written to the log, so roadmap step 6 (self-diagnostics) can subscribe to these records without any change to the engine
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] New `src/DualPlayer.Console/DualPlayer.Console.csproj` (Exe, `net10.0`, references Core, Media, Platform.Windows, Platform.MacOS; no Avalonia) and add it to `DualPlayer.slnx`
- [ ] Move `AppPaths` + `FromRoot(...)` to `src/DualPlayer.Core/AppPaths.cs`; the OS-specific `ForCurrentUser()` root stays in each composition root — `src/DualPlayer.App/AppPaths.cs` removed, App callers updated
- [ ] `ConsoleCompositionRoot.Build(paths, loggerFactory)` calling `AddCore`, `AddMedia` and the platform `Add…Platform()` selected with `OperatingSystem.IsWindows()` / `IsMacOS()` — `src/DualPlayer.Console/ConsoleCompositionRoot.cs`
- [ ] `Program.cs`: Serilog rolling file with `rollOnFileSizeLimit` at 20 MB and `retainedFileCountLimit: 10`; top-level exception logged as critical, exit `1`; no command → `error cli.usage: …` on stderr, exit `2`
- [ ] Apply the same Serilog size cap in `src/DualPlayer.App/Program.cs`
- [ ] `tests/DualPlayer.Console.Tests/` project with `ConsoleCompositionRootSmokeTests` (container builds with `ValidateOnBuild`)
- [ ] `CLAUDE.md`: name both `DualPlayer.App` and `DualPlayer.Console` as the only composition roots; add the Console row to the module table

## Edge cases

| Case | Behaviour |
|---|---|
| Run longer than one 20 MB file | rolls to a new file; at most 10 files kept, oldest removed first |
| No arguments | `error cli.usage: …` on stderr, exit `2` |
| Unhandled exception at the top level | logged with its exception, exit `1` |

## Definition of Done

- [ ] `ConsoleCompositionRootSmokeTests` and the existing `CompositionRootSmokeTests` pass
- [ ] `dotnet run --project src/DualPlayer.Console` with no arguments exits `2` with `cli.usage`
- [ ] both roots configure the 20 MB × 10 files cap (asserted by a test on the Serilog configuration helper or by inspection in review)
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
