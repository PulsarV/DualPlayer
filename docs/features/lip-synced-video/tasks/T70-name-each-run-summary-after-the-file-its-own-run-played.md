---
id: T70
title: "Name each run summary after the file its own run played"
layer: "app"
deps: []
blocks: []
acs: ["AC-06", "AC-07"]
files_hint: ["src/DualPlayer.Core/Diagnostics/EngineEvent.cs", "src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.App/Startup/TerminalPrinter.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "tests/DualPlayer.App.Tests/Startup/", "tests/DualPlayer.Core.Tests/Playback/"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21d (R1)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T70 — Name each run summary after the file its own run played

## Why

R1 (T60 regression, reproduced by all three reviewers): when a file change starts from an ended film (natural end, AC-15b, picture failure, failed change), `AudioEngine.Stop(FileChange)` has no run and publishes no `RunSummary` (`AudioEngine.cs:110-116, 1178`). `FileChanged` still sets `_stoppedFile = _file` in the printer (`TerminalPrinter.cs:137-146`), and nothing clears it. In production the engine publishes the summary synchronously inside `Stop`, before the session publishes `FileChanged` (`AudioEngine.cs:1182`; `PlaybackSession.cs:543, 567`). So the next file-change summary takes the stale name, and every later one is one file behind for the rest of the session (terminal and log). Repro: a.mkv ends → Page Down → b plays → Page Down prints `run-summary file a.mkv reason file-change` for b's run. The T60 printer test (`TerminalPrinterTests.cs:155-169`) feeds `FileChanged` before the summary, the order that never happens.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06, AC-07.

## Definition of Done

- [ ] `RunSummary` carries the file its own run played (the engine already holds `run.File`, `AudioEngine.cs:1180`); the printer and the log writer name the summary from that field.
- [ ] The `_stoppedFile` / `_changeSummaryPrinted` pairing in `TerminalPrinter` is removed.
- [ ] The T60 printer test is rewritten in production order (summary before `FileChanged`).
- [ ] New test in production order: a.mkv ends (summary reason end) → `FileChanged b` → b's summary (reason file-change) names b → `FileChanged c` → c's summary names c.
- [ ] The exit summary after a failed change still names the old film (the T60 case stays covered).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
