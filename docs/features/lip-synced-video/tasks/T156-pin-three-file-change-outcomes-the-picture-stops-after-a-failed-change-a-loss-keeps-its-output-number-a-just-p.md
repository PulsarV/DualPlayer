---
id: T156
title: "Pin three file-change outcomes: the picture stops after a failed change, a loss keeps its output number, a just-pressed offset survives"
layer: "test"
deps: []
blocks: ["T23", "T161"]
acs: ["AC-07", "AC-15", "AC-15b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23i (AA3)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T156 — Pin three file-change outcomes: the picture stops after a failed change, a loss keeps its output number, a just-pressed offset survives

## Why

- **AA3.** Each mutant passes Core 626/626: (a) without `_renderer.Pause()` at `PlaybackSession.cs:689`, a change that fails before the new picture loads (`outputs.device_not_found`) leaves the old film's picture running under the "Could not play …" note (ux-flows.md:104-108, screens.md:76); the only failed-change test (`PlaybackSessionFileChangeTests.cs:287-297`) fails at `Load`; (b) `number = lost.OutputIndex + 1` at :2062 numbers a loss by the run's engine index, so after output 1 is lost and the file changes, losing output 3 shows "Output 2 lost" (cli.md:90); (c) re-reading the offsets file at every start (`LoadOffsetsOnce`, :2008-2024) drops an offset pressed < 1 s before Page Down (spec.md:153 "same … offsets").

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-15, AC-15b.

## Definition of Done

- [x] (a) After a file change that fails with `outputs.device_not_found`, the renderer is not playing. *(Picture-stops-after-failed-change test is green. Mutant A (renderer Pause removed) fails it, from the RED run.)
- [x] (b) Three outputs: lose output 1, press Page Down, lose output 3 → `OutputLost.OutputNumber == 3` and the note "Output 3 lost". *(Loss-keeps-output-number test is green. Mutant B (lost.OutputIndex + 1) fails it, from the RED run.)
- [x] (c) An offset press followed at once by Page Down → the new file's `LatencyEstimated` carries the pressed offset. *(Just-pressed-offset test is green. Mutant C (offsets re-read at every start) fails it, from the RED run.)
- [x] Mutants on a scratch copy, each failing at least one new test: `_renderer.Pause()` at :689 removed; `number = lost.OutputIndex + 1`; offsets re-read at every start. *(Mutants A, B and C each made exactly one new test fail in the RED step; none remains)
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(Full gate: build has 0 warnings, tests pass, format is clean. A Media seek test flaked once under full load and passed on rerun.)
