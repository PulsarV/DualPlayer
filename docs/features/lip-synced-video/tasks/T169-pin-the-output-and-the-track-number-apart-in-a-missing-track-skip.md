---
id: T169
title: "Pin the output and the track number apart in a missing-track skip"
layer: "test"
deps: []
blocks: ["T23", "T173"]
acs: ["AC-09"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs", "tests/DualPlayer.App.Tests/Startup/TerminalPrinterRawLineTests.cs", "tests/DualPlayer.Media.Tests/Video/NeighbourSkipIntegrationTests.cs"]
status: "done"
step: "COMMIT"
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

- [x] A `PlaybackSessionFileChangeTests` case where output 1 plays track 3 and the neighbour has two tracks: `FileSkipped(…, "track-missing", 1, 3)` and the note `Skipped <name>: no track 3 for output 1`. *(Core test A_missing_track_skip_names_the_output_and_the_track_apart_when_output_1_plays_track_3_AC_09 pins FileSkipped(..., track-missing, 1, 3) and the note 'Skipped Episode-02.mkv: no track 3 for output 1'; green)*
- [x] A `TerminalPrinterRawLineTests` case: `FileSkipped("b.mkv", "track-missing", 1, 3)` prints `skipped  b.mkv  reason track-missing  output 1  track 3`. *(App TerminalPrinterRawLineTests case pins 'skipped  b.mkv  reason track-missing  output 1  track 3'; green)*
- [x] A `NeighbourSkipIntegrationTests` case on real generated media where output 1 plays track 3 of a three-track film and the neighbour has two tracks: the skip event carries output 1, track 3. *(Media NeighbourSkipIntegrationTests case on real generated media (output 1, track 3, two-track neighbour) asserts the skip names output 1 and track 3; green)*
- [x] Mutants on a scratch copy, each failing at least one new test: note text with `OutputNumber` and `Track` swapped (PlaybackSession.cs:1040); `new FileSkipped(name, "track-missing", track, i + 1)` (:903); `output {t2}  track {o}` (TerminalPrinter.cs:105). *(3 mutants re-applied to the final code (PlaybackSession.cs:903 args swap, :1040 note swap, TerminalPrinter.cs:105 line swap): each failed its new test; all reverted with git checkout -- src)*
- [x] No production code changes. *(git diff -- src is empty; only 4 test files modified (the fourth, EngineRig.cs, is the test scaffold))*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(dotnet build 0 warnings/0 errors; dotnet test all projects green (0 failed); dotnet format --verify-no-changes clean)*
