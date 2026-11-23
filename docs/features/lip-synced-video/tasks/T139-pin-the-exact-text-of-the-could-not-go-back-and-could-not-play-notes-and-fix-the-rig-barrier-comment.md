---
id: T139
title: "Pin the exact text of the Could-not-go-back and Could-not-play notes, and fix the rig barrier comment"
layer: "test"
deps: []
blocks: ["T141", "T23"]
acs: ["AC-05", "AC-06b", "AC-07", "AC-15b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionEndedRecoveryTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23e (U1, U4)"
---

<!-- Follow-up task from review-2026-11-23e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23e.md) · repo `CLAUDE.md`. Do not guess. -->

# T139 — Pin the exact text of the Could-not-go-back and Could-not-play notes, and fix the rig barrier comment

## Why

- **U1.** The SCR-03 notes "Could not go back: …" (`PlaybackSession.cs:1407`) and "Could not play <file>: …" (`PlaybackSession.cs:682`) have no test that pins their text. `PlaybackSessionEndedRecoveryTests.cs:116, 185, 242` only check the note count went up; `PlaybackSessionFileChangeTests.cs:264` only checks the note contains the file name and has the warning style. Mutants `"Output 9 lost"` and `$"Skipped {file}"` pass all 621 Core and 165 App tests. Manifest: screens.md:114-115.
- **U4.** The `Barrier()` doc comment (`EngineRig.cs:313`) still says "(a start while running is refused on the loop)"; T137 removed that probe start.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-05, AC-06b, AC-07, AC-15b.

## Definition of Done

- [x] `PlaybackSessionEndedRecoveryTests` at :116, :185 and :242 assert the new note starts with `"Could not go back: "` and has the warning style; with the text at `PlaybackSession.cs:1407` changed to `"Output 9 lost"` (scratch copy) they fail. *(Mutant "Output 9 lost": all three fail.)*
- [x] `PlaybackSessionFileChangeTests` at :264 asserts the exact note `"Could not play Episode 2.mkv: mpv could not open the file."`; with the text at `PlaybackSession.cs:682` changed to `$"Skipped {file}"` (scratch copy) it fails. *(Mutant $"Skipped {file}": it fails.)*
- [x] The `Barrier()` doc comment at `EngineRig.cs:313` describes the strict check as a round trip through the engine's loop that requires the run to still be active, with no mention of a probe start.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(Build 0 warnings; tests Core 621, Media 174, Console 151, App 166, Windows 41, MacOS 12 passed; format clean.)*
