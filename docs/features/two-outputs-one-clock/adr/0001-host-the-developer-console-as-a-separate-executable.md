---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-11"
feature_size: "L"
ticket: ""
---

# 0001 — Host the Developer console as a separate DualPlayer.Console executable with its own composition root

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** Volodymyr Kravchuk (project owner), during the design Socratic walk

## Context

Roadmap step 3 ships the engine through a Developer console — a text program that takes commands while playback runs (spec §1), with Ctrl+C and window close producing the run summary (AC-13). The feature's only target surface is `cli`. The repository rule (CLAUDE.md, ADR-0003 of the repo) names `src/DualPlayer.App/CompositionRoot.cs` the only composition root, but `DualPlayer.App` is an Avalonia `WinExe` that has no console window on Windows.

## Decision drivers

- Spec AC-13: Ctrl+C and closing the console window must still print and log the run summary.
- Spec §1: the console is separate from the couch UI.
- Repo constraint (§2): modules self-register through `Add<Module>()`; platform is selected at runtime with `OperatingSystem.IsWindows()` / `IsMacOS()`.
- Roadmap step 4 must reuse the same engine modules unchanged inside `DualPlayer.App`.

## Considered options

1. **New `src/DualPlayer.Console` executable** with its own composition root calling the same `Add<Module>()` extensions.
2. **Same, placed under `tools/engine-console/`** to mark it as a never-shipped developer tool.
3. **A `--console` mode inside `DualPlayer.App`** that skips Avalonia and allocates a console via P/Invoke.

## Decision outcome

**Chosen:** option 1. A real console executable gets native Ctrl+C and close-window signals for AC-13, carries no Avalonia dependency, and keeps the console code out of the couch-UI assembly, while reusing every module registration as-is.

## Consequences

**Positive**
- Console lifecycle (Ctrl+C, window close) is handled by the runtime, not by P/Invoke tricks.
- The engine is exercised through exactly the registrations step 4 will use in `DualPlayer.App`.

**Negative**
- A second composition root: the CLAUDE.md rule changes to "`DualPlayer.App` and `DualPlayer.Console` are the only composition roots", and the two can drift in what they register.
- One more project in `DualPlayer.slnx` and CI.

**Neutral**
- Drift between the two roots is guarded by a second smoke test (`tests/DualPlayer.Console.Tests/`): the container builds and resolves the console's command loop.
- Whether the console is excluded from the installer is decided at roadmap step 13 (packaging).

## Links

- Spec: [[../spec.md]] §1, AC-13
- SAD: [[../sad.md]] §4
- Repo ADR: `docs/adr/0003-organize-code-as-core-with-platform-adapters.md`
