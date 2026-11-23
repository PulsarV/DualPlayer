---
id: T163
title: "Build the mpv note commands in a pure helper and pin the 2 s hold and the warning colour"
layer: "adapter"
deps: []
blocks: ["T23", "T168"]
acs: ["AC-06b", "AC-08", "AC-09", "AC-09b", "AC-10", "AC-11", "AC-15", "AC-15b"]
files_hint: ["src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "tests/DualPlayer.Media.Tests/Video/MpvVideoRendererTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23j (AB2)"
---

<!-- Follow-up task from review-2026-11-23j. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23j.md) · repo `CLAUDE.md`. Do not guess. -->

# T163 — Build the mpv note commands in a pure helper and pin the 2 s hold and the warning colour

## Why

- **AB2.** Only `NoteHold`, `NoteColor` and `NoteOptions` are tested; the commands `ShowNote` sends to mpv are not (`src/DualPlayer.Media/Video/MpvVideoRenderer.cs:485, 490`). Mutants that pass Media 179/179 and App 193/193: `show-text` duration → `"1000"`; the `set osd-color` call dropped (warnings no longer yellow). spec.md:99 "Shows briefly"; screens.md:100-102 (SCR-03 info / warning).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-08, AC-09, AC-09b, AC-10, AC-11, AC-15, AC-15b.

## Definition of Done

- [x] A pure `MpvVideoRenderer.NoteCommands(OsdNoteStyle style, string text, NoteStyle note, bool layoutApplied, NoteStyle? appliedStyle)` returns the mpv command argument lists in order; `ShowNote` runs exactly those and then records layout and style as applied. *(MpvVideoRenderer.NoteCommands returns the ordered mpv argument lists; ShowNote runs exactly those, then records layout and style as applied)*
- [x] New tests: the first note sends every `NoteOptions` `set`, then `set osd-color <NoteColor>`, then `show-text <text> 2000`; a warning after an info note sends `set osd-color #FFFFD400`; a second note in the same style sends only `show-text`; `$` in the text is doubled. *(4 new MpvVideoRendererTests: first note (layout, colour, show-text 2000), warning after info (#FFFFD400), same style (show-text only), dollar doubled)*
- [x] Mutants on a scratch copy, each failing at least one new test: duration `"1000"`; the `osd-color` command dropped; layout `set`s repeated on every note. *(duration 1000, osd-color dropped, layout on every note: each fails at least one new test; reverted)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(build 0 warnings, Media 183 and all suites pass, format clean; one full run showed 1 unnamed Core failure, not reproduced in 3 Core-only and 3 full re-runs — T163 does not touch Core)*
