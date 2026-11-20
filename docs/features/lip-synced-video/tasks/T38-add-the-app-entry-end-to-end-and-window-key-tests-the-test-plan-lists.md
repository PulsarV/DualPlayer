---
id: T38
title: "Add the App-entry, end-to-end and window key tests the test plan lists"
layer: "test"
deps: ["T27", "T28", "T31", "T33", "T35", "T37"]
blocks: []
acs: ["AC-01", "AC-02", "AC-03", "AC-04", "AC-05", "AC-06", "AC-07", "AC-10", "AC-15b"]
files_hint: ["tests/DualPlayer.App.Tests/", "docs/features/lip-synced-video/test-plan.md"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "L"
origin: "review-2026-11-20 (F19)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T38 — Add the App-entry, end-to-end and window key tests the test plan lists

## Why

F19 (stage-1, AC-01–AC-07, AC-10, AC-15b): the test-plan's App-entry, end-to-end and headless-window key tests do not exist (`test-plan.md:34-101`); `PlaceOnScreen` is checked only by reflection (`PlaybackWindowTests.cs:29-34`), and the T22 manual run has no evidence.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02, AC-03, AC-04, AC-05, AC-06, AC-07, AC-10, AC-15b.

## Definition of Done

- [x] Every App-entry / e2e / headless-window row in test-plan.md §34-101 has a test (Avalonia headless where a window is needed, fakes for audio and renderer), or is re-planned in test-plan.md with a reason.
- [x] `PlaceOnScreen` tested behaviourally (window bounds on the named screen), not by reflection.
- [x] The T22 manual run is folded into the T23 run guide as a checklist item (evidence lands with T23).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

Added `PlaybackWindowHeadlessTests` (Avalonia.Headless 11.3.23 added to the App tests) and `PlayEndToEndTests` (the App's host pieces over the real session and engine; the Core fakes are linked as the Console tests do). The new tests pinned existing behaviour and passed at once; each was proven by mutation (key routing off, `PlaceOnScreen` position or fullscreen removed). Writing the AC-15b end-to-end test found a gap T27 had opened: once every output was lost, next/previous kept them all lost and refused; a file change then tries every device again (red → green, mutation-checked). Process-level refusal runs moved to the T23 checklist (test-plan.md, "Where the e2e and e2e-through-UI rows run").
