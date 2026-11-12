---
id: T47
title: "Print the started line after the output lines and list the new events in the SAD"
layer: "app"
deps: ["T46"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.Console/Rendering/EventPrinter.cs", "src/DualPlayer.Console/Commands/PlayCommand.cs", "tests/DualPlayer.Console.Tests/", "docs/features/two-outputs-one-clock/sad.md"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r3 (S4)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T47 — Print the started line after the output lines and list the new events in the SAD

## Why

S4 (review-2026-11-12-r3): On WASAPI the pre-fill sets first-audio before `StartRun` returns, so `AudioStarted` can be printed before PlayCommand enqueues the `output N` lines (cli.md §1.3 order). `sad.md:473` event list lacks `AudioStarted` and `SeekTargetOutsideFile`.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [ ] The printer holds the `started` line until PlayCommand has enqueued the output lines.
- [ ] Test: `AudioStarted` published inside `StartRun` (before it returns) still prints after the output lines.
- [ ] sad.md event list updated.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
