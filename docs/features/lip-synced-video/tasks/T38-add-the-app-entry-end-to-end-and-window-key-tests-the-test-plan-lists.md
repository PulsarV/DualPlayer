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
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T38 — Add the App-entry, end-to-end and window key tests the test plan lists

## Why

F19 (stage-1, AC-01–AC-07, AC-10, AC-15b): the test-plan's App-entry, end-to-end and headless-window key tests do not exist (`test-plan.md:34-101`); `PlaceOnScreen` is checked only by reflection (`PlaybackWindowTests.cs:29-34`), and the T22 manual run has no evidence.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02, AC-03, AC-04, AC-05, AC-06, AC-07, AC-10, AC-15b.

## Definition of Done

- [ ] Every App-entry / e2e / headless-window row in test-plan.md §34-101 has a test (Avalonia headless where a window is needed, fakes for audio and renderer), or is re-planned in test-plan.md with a reason.
- [ ] `PlaceOnScreen` tested behaviourally (window bounds on the named screen), not by reflection.
- [ ] The T22 manual run is folded into the T23 run guide as a checklist item (evidence lands with T23).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
