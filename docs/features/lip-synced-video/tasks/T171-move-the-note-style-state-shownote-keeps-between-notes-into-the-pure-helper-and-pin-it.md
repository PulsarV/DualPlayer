---
id: T171
title: "Move the note-style state ShowNote keeps between notes into the pure helper and pin it"
layer: "adapter"
deps: []
blocks: ["T23", "T173"]
acs: ["AC-06b", "AC-08", "AC-09", "AC-09b", "AC-10", "AC-11", "AC-15", "AC-15b"]
files_hint: ["src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "tests/DualPlayer.Media.Tests/Video/MpvVideoRendererTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-24 (AD3)"
---

<!-- Follow-up task from review-2026-11-24. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-24.md) · repo `CLAUDE.md`. Do not guess. -->

# T171 — Move the note-style state ShowNote keeps between notes into the pure helper and pin it

## Why

- **AD3.** AB2 residual: the state `ShowNote` hands to `NoteCommands` and then records (`src/DualPlayer.Media/Video/MpvVideoRenderer.cs:473-479`) is untested. Mutants that pass Media 183/183: passing `style` as `appliedStyle` at :473 (no `osd-color` sent, warnings never yellow); `_appliedNoteStyle = NoteStyle.Info` at :479 (an info note after a warning stays yellow); `.TakeLast(1)` on the command loop. screens.md:100-102 (SCR-03 info / warning styles).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-08, AC-09, AC-09b, AC-10, AC-11, AC-15, AC-15b.

## Definition of Done

- [x] The state transition lives in a pure helper: given the current `(layoutApplied, appliedStyle)`, the style options, the text and the note style, it returns the commands **and** the next state; `ShowNote` only runs the returned commands and stores the returned state. *(Added public readonly record struct NoteState and static NextNote(style, text, note, state) returning (Commands, State); ShowNote runs the commands and stores the state, replacing the two fields with one _noteState)*
- [x] New tests over a sequence Warning → Info → Info: the warning sends `set osd-color #FFFFD400`, the following info note sends `set osd-color <foreground>`, the third sends only `show-text`; layout `set`s only on the first note. *(3 new tests (Warning -> Info -> Info sequence, state after a note, info after a warning not yellow) pass; they assert the osd-color #FFFFD400 and #FFEEEEEE sends, layout only on the first note, show-text only on the third)*
- [x] Mutants on a scratch copy, each failing at least one new test: the next applied style always `Info`; the next applied style never updated; layout never marked applied. *(3 mutants (next style always Info, never updated, layout never marked applied) each fail 2-3 new tests; reverted from backup, diff clean)*
- [x] `ShowNote` keeps the `_handle == 0` guard and the lock; no other behaviour change. *(ShowNote keeps the _handle == 0 guard and the lock; NoteCommands unchanged; no other behaviour change)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(build 0 warnings 0 errors; dotnet test DualPlayer.slnx all pass; format --verify-no-changes exit 0)*
