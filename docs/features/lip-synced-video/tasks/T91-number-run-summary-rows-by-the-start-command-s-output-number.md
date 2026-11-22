---
id: T91
title: "Number run-summary rows by the start command's output number"
layer: "app"
deps: []
blocks: []
acs: ["AC-06", "AC-07"]
files_hint: ["src/DualPlayer.App/Startup/RunSummaryLines.cs", "src/DualPlayer.App/Startup/TerminalPrinter.cs", "src/DualPlayer.Core/Diagnostics/EngineEvent.cs", "src/DualPlayer.Core/Diagnostics/RunSummaryBuilder.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.App.Tests/Startup/", "tests/DualPlayer.Core.Tests/"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22b (S4)"
---

<!-- Follow-up task from review-2026-11-22b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22b.md) · repo `CLAUDE.md`. Do not guess. -->

# T91 — Number run-summary rows by the start command's output number

## Why

**S4** (checked in code). `RunSummaryLines` numbers rows by list position (`index++`, `src/DualPlayer.App/Startup/RunSummaryLines.cs:18-25`), but after a loss the new file's engine run gets only the playing outputs (`PlaybackSession.cs:297-299`). With output 1 lost, a Page Down prints `output 1  lost` / `output 2 … dev-2` in the start lines, but that file's summary row reads `1  dev-2`. cli.md §2.1 says the `output` column is the output's number from the start command. T73 fixed this numbering for log records only; no test covers the summary.

Owner decision: fix now — carry the start-command output number into `OutputSummary` (or map DeviceId → number from the latest `LatencyEstimated` in the printer).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06, AC-07.

## Definition of Done

- [ ] Run-summary rows show each output's start-command number, also after an output was lost and the file changed.
- [ ] Test: lose output 1, Page Down, Esc → the new file's summary row reads `2  dev-2`; existing summary tests stay green.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
