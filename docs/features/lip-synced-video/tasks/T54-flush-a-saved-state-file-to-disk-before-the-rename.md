---
id: T54
title: "Flush a saved state file to disk before the rename"
layer: "infra"
deps: []
blocks: []
acs: ["AC-10"]
files_hint: ["src/DualPlayer.Core/Settings/JsonFileStateStore.cs", "tests/DualPlayer.Core.Tests/Settings/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21b (Q9)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T54 — Flush a saved state file to disk before the rename

## Why

Q9: AC-10 (`spec.md:177`) says an offset is saved so that a crash of the player or the PC does not lose it. `JsonFileStateStore.cs:86-87` writes the temp file with `File.WriteAllText` and renames it without flushing to disk; after a power loss or BSOD `device-offsets.json` can come back empty or corrupt and every device starts at offset 0. The only test (`DeviceOffsetsFileTests.cs:62`) covers a dropped process.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-10.

## Definition of Done

- [ ] The temp file is written through a `FileStream` and `Flush(flushToDisk: true)` before `File.Move(..., overwrite: true)`; expected IO errors still return `Result` failures.
- [ ] A test pins the write path (e.g. the temp file is complete and flushed before the move, through a seam or by asserting the written content after a simulated rename failure); existing store tests stay green.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
