---
id: T53
title: "Write log record values with the contract's names"
layer: "ports"
deps: []
blocks: []
acs: ["AC-13", "AC-17"]
files_hint: ["src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "tests/DualPlayer.App.Tests/Startup/SessionEventLogWriterTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21b (Q13)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T53 — Write log record values with the contract's names

## Why

Q13: `SessionEventLogWriter.cs:40, 108` logs `Cause` and `Type` with enum `ToString()` (`Offset`, `DeviceLost`, `FileChange`, `BluetoothHeadphones`), while `contracts/cli.md:47, 99` defines `offset`, `device-lost`, `seek`, `pause`, `resume`, `file-change` and `bluetooth-headphones`, `bluetooth-speaker`, `wired`, `hdmi`, `other`. `SessionEventLogWriterTests.cs:39` asserts `"Offset"`, locking the mismatch in; test-plan row 105 asks for names equal to cli.md §3.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-13, AC-17.

## Definition of Done

- [x] Every enum value written to the log uses the cli.md string (reuse the `TerminalPrinter`/`RunSummaryLines` mappers; no second mapping table).
- [x] Tests assert the contract strings for every `PictureDelayCause` and device type.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
