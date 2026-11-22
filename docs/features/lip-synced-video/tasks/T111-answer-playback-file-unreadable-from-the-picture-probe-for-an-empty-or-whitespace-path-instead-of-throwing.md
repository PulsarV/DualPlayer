---
id: T111
title: "Answer playback.file_unreadable from the picture probe for an empty or whitespace path instead of throwing"
layer: "integration"
deps: ["T109"]
blocks: []
acs: ["AC-02"]
files_hint: ["src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "tests/DualPlayer.Media.Tests/Video/FfmpegPictureProbeTests.cs", "tests/DualPlayer.App.Tests/Startup/StartCommandTests.cs"]
status: "blocked"
step: "DEPS — dep T109 blocked"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22e (W6)"
---

<!-- Follow-up task from review-2026-11-22e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22e.md) · repo `CLAUDE.md`. Do not guess. -->

# T111 — Answer playback.file_unreadable from the picture probe for an empty or whitespace path instead of throwing

## Why

- **W6**. T102 added `Path.GetFullPath(path)` at the top of the probe (`FfmpegPictureProbe.cs:40`). `play "" --out 1=1` passes `StartCommand.Parse` (`StartCommand.cs:20`); `Validate` calls `Probe("")`, `GetFullPath` throws `ArgumentException`, and the start exits 1 ("terminated unexpectedly", `App.axaml.cs:76`) instead of 2. A whitespace-only path does the same on Windows. Before T102, `avformat_open_input` failed cleanly with `playback.file_unreadable`. It also throws across a module boundary (CLAUDE.md errors convention).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02.

## Definition of Done

- [ ] `FfmpegPictureProbe.Probe` returns `Result.Failure("playback.file_unreadable", …)` for a null, empty or whitespace path before it builds the key, and never lets an `ArgumentException` / `PathTooLongException` / `NotSupportedException` from `GetFullPath` escape.
- [ ] Tests: a Media test for `""` and `"   "` → `playback.file_unreadable`; a start-path test (`StartCommand` with the real probe or a matching fake) → exit 2 with `playback.file_unreadable`. Each fails if the fix is reverted.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
