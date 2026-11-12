---
id: T40
title: "Log console lines independently of the console write and always signal the run end"
layer: "app"
deps: ["T39"]
blocks: []
acs: ["AC-11", "AC-13"]
files_hint: ["src/DualPlayer.Console/Rendering/ConsoleLineQueue.cs", "src/DualPlayer.Console/Rendering/EventPrinter.cs", "src/DualPlayer.Console/Commands/PlayCommand.cs", "tests/DualPlayer.Console.Tests/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r2 (R5)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T40 — Log console lines independently of the console write and always signal the run end

## Why

R5 (review-2026-11-12-r2): `ConsoleLineQueue.Drain` writes, logs, then signals `Written` inside one `try`. A failing write (e.g. `IOException` on a full disk) loses the log line and never fires `RunEnded`, so `UntilRunEnds.ReadLine` can block forever on an open pipe. After a > 5 s stall `EventPrinter.Dispose` disposes the CTS and the late `Cancel` logs a false write failure.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-11, AC-13.

## Definition of Done

- [ ] Each line is logged before the console write; `Written` fires whatever the write outcome; a write failure is logged once.
- [ ] `Cancel` on a disposed run-end token is tolerated (or the token is not disposed while summary lines are queued).
- [ ] Tests: a throwing writer still logs every line and ends the run; a slow writer past the flush bound produces no false failure log.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
