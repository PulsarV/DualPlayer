---
id: T169
title: "Pin the output and the track number apart in a missing-track skip"
layer: "test"
deps: []
blocks: ["T23", "T173"]
acs: ["AC-09"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs", "tests/DualPlayer.App.Tests/Startup/TerminalPrinterRawLineTests.cs", "tests/DualPlayer.Media.Tests/Video/NeighbourSkipIntegrationTests.cs"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-24 (AD1)"
---

<!-- Follow-up task from review-2026-11-24. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-24.md) · repo `CLAUDE.md`. Do not guess. -->

# T169 — Pin the output and the track number apart in a missing-track skip

## Why

- **AD1.** No test tells the output number from the track number in a missing-track skip: every fixture uses output 2 with track 2 (`tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs:109-110, 197, 214`, `tests/DualPlayer.App.Tests/Startup/TerminalPrinterTests.cs:266-281`, `tests/DualPlayer.App.Tests/Startup/TerminalPrinterRawLineTests.cs:71-80`, `tests/DualPlayer.Media.Tests/Video/NeighbourSkipIntegrationTests.cs:43`). Mutants that swap the two survive: the note text at `src/DualPlayer.Core/Playback/PlaybackSession.cs:1040`, the `FileSkipped` arguments at :903, the terminal line at `src/DualPlayer.App/Startup/TerminalPrinter.cs:105`. AC-09: "the Developer's log names the file, the output and the missing track"; cli.md §2.2 `skipped … output N  track N`.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09.

## Definition of Done

- [ ] A `PlaybackSessionFileChangeTests` case where output 1 plays track 3 and the neighbour has two tracks: `FileSkipped(…, "track-missing", 1, 3)` and the note `Skipped <name>: no track 3 for output 1`.
- [ ] A `TerminalPrinterRawLineTests` case: `FileSkipped("b.mkv", "track-missing", 1, 3)` prints `skipped  b.mkv  reason track-missing  output 1  track 3`.
- [ ] A `NeighbourSkipIntegrationTests` case on real generated media where output 1 plays track 3 of a three-track film and the neighbour has two tracks: the skip event carries output 1, track 3.
- [ ] Mutants on a scratch copy, each failing at least one new test: note text with `OutputNumber` and `Track` swapped (PlaybackSession.cs:1040); `new FileSkipped(name, "track-missing", track, i + 1)` (:903); `output {t2}  track {o}` (TerminalPrinter.cs:105).
- [ ] No production code changes.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
