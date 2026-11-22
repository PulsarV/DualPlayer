---
id: T113
title: "Pin cli.md's exact terminal line shapes and the full start-check order with tests"
layer: "test"
deps: []
blocks: []
acs: ["AC-01", "AC-02", "AC-03b", "AC-06", "AC-07", "AC-09", "AC-13"]
files_hint: ["tests/DualPlayer.App.Tests/Startup/TerminalPrinterTests.cs", "tests/DualPlayer.App.Tests/E2E/PlayEndToEndTests.cs", "tests/DualPlayer.App.Tests/Startup/StartCommandTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22e (W11, W12)"
---

<!-- Follow-up task from review-2026-11-22e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22e.md) · repo `CLAUDE.md`. Do not guess. -->

# T113 — Pin cli.md's exact terminal line shapes and the full start-check order with tests

## Why

- **W11**. Every App terminal-line assertion collapses whitespace runs to one space (`Squash`, `TerminalPrinterTests.cs:323, 359`; `PlayEndToEndTests.cs:54`). Only `next-file-timeout  checked N` checks the raw text. A regression to single spaces, or a lost label padding (`next     <file>`, `picture   delay`, `started   clock`, the two-space `output N  track …`, `skipped  …`, `device-lost  …`), passes every test. Console's `EventPrinterTests.cs:106, 182` show the raw-writer pattern.
- **W12**. The T107 row in test-plan.md (:170) says the start-check order is pinned, but `StartCommandTests.cs:134-157` pins only probe < track < device-not-found < screen. Nothing pins the device open/release check before the screen check, or the screen check before the renderer library (cli.md §1.1 :35); moving the renderer-library check above the screen lookup in `StartCommand.cs:110-127` passes every test.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02, AC-03b, AC-06, AC-07, AC-09, AC-13.

## Definition of Done

- [ ] Raw-writer assertions (exact text, no whitespace squashing) for each line shape of cli.md §1.1, §2.2 and §2.3 that `TerminalPrinter` writes; each fails if a label's padding or a two-space separator changes.
- [ ] Two start-order cases: missing screen + failing renderer library → `start.screen_not_found`; refusing device + missing screen → `outputs.device_access_denied`. Each fails if the two checks are swapped in `StartCommand`.
- [ ] Test-only: no production change, unless a raw assertion exposes a real mismatch with cli.md (then fix the printer, not the test).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
