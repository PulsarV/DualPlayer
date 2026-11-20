---
id: T32
title: "Print the terminal lines exactly as cli.md specifies"
layer: "ports"
deps: ["T29"]
blocks: []
acs: ["AC-08", "AC-15"]
files_hint: ["src/DualPlayer.App/Startup/TerminalPrinter.cs", "src/DualPlayer.Core/Sync/OffsetBook.cs", "src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.App.Tests/Startup/TerminalPrinterTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-20 (F15)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T32 — Print the terminal lines exactly as cli.md specifies

## Why

F15 (cli.md §1.1, §2.2, §2.3): terminal lines diverge from the contract: no `no-next-file`/`no-previous-file`; no `picture-delay a -> b` on `device-lost`; no codec/profile on `skipped`; a previous move prints `next`; file changes print full paths; the offset clamp line has the wrong format (`TerminalPrinter.cs:96-128`; `OffsetBook.cs:66-67`). The tests feed in contract text, so they cannot catch it.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-15.

## Definition of Done

- [ ] Every line in cli.md §1.1/§2.2/§2.3 is printed in its contract form for the events the session really emits (direction for previous, file names not paths, codec/profile on `skipped`, `picture-delay a -> b` on `device-lost`, the clamp line format).
- [ ] Tests drive the printer with the events a real `PlaybackSession` emits (or the session itself with fakes) and compare with literal lines copied from cli.md.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
