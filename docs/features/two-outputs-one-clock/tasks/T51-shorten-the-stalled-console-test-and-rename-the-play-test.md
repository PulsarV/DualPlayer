---
id: T51
title: "Shorten the stalled-console test and rename the play test"
layer: "test"
deps: ["T47"]
blocks: []
acs: ["AC-13"]
files_hint: ["src/DualPlayer.Console/Rendering/ConsoleLineQueue.cs", "tests/DualPlayer.Console.Tests/ConsoleWriteFailureTests.cs", "tests/DualPlayer.Console.Tests/ListAndPlayCommandTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r3 (S9, S11)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T51 — Shorten the stalled-console test and rename the play test

## Why

S9, S11 (review-2026-11-12-r3): The stalled-dispose test waits out three fixed 5 s limits (15 s). The `ListAndPlayCommandTests` play test name still mentions the started clock, which PlayCommand no longer prints.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-13.

## Definition of Done

- [ ] `ConsoleLineQueue` drain limit is an internal constructor parameter (default 5 s); the test uses ~100 ms and runs well under 1 s.
- [ ] Play test renamed to what it asserts.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
