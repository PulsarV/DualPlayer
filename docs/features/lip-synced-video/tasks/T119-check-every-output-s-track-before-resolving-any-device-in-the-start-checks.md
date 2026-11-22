---
id: T119
title: "Check every output's track before resolving any device in the start checks"
layer: "app"
deps: []
blocks: []
acs: ["AC-01", "AC-03b"]
files_hint: ["src/DualPlayer.Core/Outputs/OutputAssignmentValidator.cs", "tests/DualPlayer.Core.Tests/Outputs/OutputAssignmentValidatorTests.cs", "tests/DualPlayer.App.Tests/Startup/StartCommandTests.cs"]
status: "todo"
step: ""
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22f (X4)"
---

<!-- Follow-up task from review-2026-11-22f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22f.md) · repo `CLAUDE.md`. Do not guess. -->

# T119 — Check every output's track before resolving any device in the start checks

## Why

- **X4**. cli.md §1.1 (:35) orders the checks "tracks → devices", but `OutputAssignmentValidator.cs:16-38` checks track and device output by output. `play f --out 1=99 --out 5=1` (output 1's device missing, output 2's track missing) prints `outputs.device_not_found`. The order tests (`StartCommandTests.cs:161-185`) use one output only.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03b.

## Definition of Done

- [ ] The validator checks every `--out` track before resolving any device (then presence, then used-once), as cli.md §1.1 says.
- [ ] Tests: two outputs, output 1's device missing and output 2's track missing → the track code (Core validator test and an App `StartCommandTests` case). Fail if reverted.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
