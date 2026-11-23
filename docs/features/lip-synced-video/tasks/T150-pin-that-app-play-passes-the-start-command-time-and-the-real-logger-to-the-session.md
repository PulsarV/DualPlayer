---
id: T150
title: "Pin that App.Play passes the start-command time and the real logger to the session"
layer: "test"
deps: []
blocks: ["T23", "T152"]
acs: ["AC-01", "AC-17"]
files_hint: ["tests/DualPlayer.App.Tests/Startup/AppEntryTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23h (Y3)"
---

<!-- Follow-up task from review-2026-11-23h. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23h.md) · repo `CLAUDE.md`. Do not guess. -->

# T150 — Pin that App.Play passes the start-command time and the real logger to the session

## Why

- **Y3.** `App.Play` passes `CommandTimestamp` into the session start (`App.axaml.cs:179`) and the App logger into `SessionEventLogWriter` (`:105`). Dropping the timestamp makes `StartTimings`, the `started … first-frame` line and the `start` recovery budget count from the session start, under-reporting the 3 s start budget in the record T23 L1 judges (`evidence/t23-acceptance-run-guide.md:62`). Passing `NullLogger.Instance` removes every cli.md §3 record from the production log. Both mutants pass App 166/166. AC-01 (spec §6 start budget); AC-17; cli.md §3.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-17.

## Definition of Done

- [ ] An `AppEntryTests` case sets `App.CommandTimestamp` to a time before a known delay, runs `App.Play` with a capturing logger, and asserts that the logged `StartTimings` `CommandToFirstFrameMs` includes that delay.
- [ ] The same run (or a sibling case) asserts that session records reach the capturing logger through `App.Play` (at least `LatencyEstimated` and `PictureDelayChanged`, per cli.md §3).
- [ ] Mutants on a scratch copy, each failing at least one new test: `App.axaml.cs:179` without `{ CommandTimestamp = CommandTimestamp }`; `:105` passing `NullLogger.Instance` to `SessionEventLogWriter`.
- [ ] `App.CommandTimestamp` is reset after the test so other tests are unaffected.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
