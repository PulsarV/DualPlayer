---
id: T162
title: "Run the start command through a testable Program.Run and pin the start clock and the refusal exit code"
layer: "wiring"
deps: []
blocks: ["T23", "T168"]
acs: ["AC-01"]
files_hint: ["src/DualPlayer.App/Program.cs", "tests/DualPlayer.App.Tests/Startup/ProgramRunTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23j (AB1)"
---

<!-- Follow-up task from review-2026-11-23j. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23j.md) · repo `CLAUDE.md`. Do not guess. -->

# T162 — Run the start command through a testable Program.Run and pin the start clock and the refusal exit code

## Why

- **AB1.** Nothing calls `Program.Main` (`src/DualPlayer.App/Program.cs:16, 26-31`). Mutants that pass App 193/193: `App.CommandTimestamp = null` (the start budget then counts from the session start, hiding the ≤ 3 s picture probe and window creation, spec §6); `return 0` on a parse failure (cli.md §4 says exit 2 for `cli.usage` and `outputs.too_many`, cli.md §5).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [ ] `Program.Main` delegates to `internal static int Run(string[] args, TimeProvider time, TextWriter stderr, Func<ILoggerFactory> createLogging, Func<StartRequest, ILoggerFactory, int> startApp)`; `Run` sets `App.CommandTimestamp` from `time` before it creates logging or parses; behaviour of the real start is unchanged.
- [ ] New `ProgramRunTests`: `["play"]` → exit 2, stderr `error cli.usage: …`, `startApp` not called; four `--out` → exit 2, stderr `error outputs.too_many: …`; a valid request → `startApp` sees `App.CommandTimestamp` equal to the first timestamp `time` gave, taken before `createLogging` ran; an exception from `startApp` → exit 1. Tests run in the `ConsoleCollection` and restore `App.CommandTimestamp`.
- [ ] Mutants on a scratch copy, each failing at least one new test: `App.CommandTimestamp = null`; `return 0` on a parse failure; the timestamp taken after logging is created.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
