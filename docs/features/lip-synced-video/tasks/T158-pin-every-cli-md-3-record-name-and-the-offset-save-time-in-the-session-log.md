---
id: T158
title: "Pin every cli.md \u00a73 record name and the offset save time in the session log"
layer: "test"
deps: []
blocks: ["T23", "T161"]
acs: ["AC-17", "AC-10", "AC-06b"]
files_hint: ["tests/DualPlayer.App.Tests/Startup/SessionEventLogWriterTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23i (AA5)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T158 — Pin every cli.md §3 record name and the offset save time in the session log

## Why

- **AA5.** Renaming `PictureDelayMoved`, `OffsetChanged`, `PictureTiming`, `FrameCounters`, `PictureFailed` or `PictureReload` in the message templates of `SessionEventLogWriter.cs:49, 70, 75, 79, 87, 100, 104`, or logging `SavedAt` as null, each passes App 181/181. The T23 guide finds `PictureDelayMoved`, `PictureTiming` and `FrameCounters` by name (guide:53, 63-64).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-17, AC-10, AC-06b.

## Definition of Done

- [ ] `SessionEventLogWriterTests` asserts the `{OriginalFormat}` prefix (the record name of cli.md §3) of every record the writer logs.
- [ ] An `OffsetChanged` with a save time logs that time; one without logs none.
- [ ] Mutants on a scratch copy, each failing at least one new test: each of the six record names renamed; `SavedAt` logged as null.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
