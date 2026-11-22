---
id: T120
title: "Drive the real session through the view model to the renderer's notes in an App end-to-end test"
layer: "ui"
deps: []
blocks: []
acs: ["AC-08", "AC-11"]
files_hint: ["tests/DualPlayer.App.Tests/E2E/PlayEndToEndTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22f (X8)"
---

<!-- Follow-up task from review-2026-11-22f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22f.md) · repo `CLAUDE.md`. Do not guess. -->

# T120 — Drive the real session through the view model to the renderer's notes in an App end-to-end test

## Why

- **X8**. The window notes (AC-11 limit, AC-08 no next file, skipped, lost, picture failed) reach App tests only as hand-written `NoteShown` events (`tests/DualPlayer.App.Tests/Views/PlaybackWindowHeadlessTests.cs:78-95`, `tests/DualPlayer.App.Tests/ViewModels/PlaybackViewModelTests.cs:109`). No App or e2e test drives the real `PlaybackSession` through `PlaybackViewModel` to `IVideoRenderer.ShowNote`; `PlayEndToEndTests` checks terminal lines only.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-11.

## Definition of Done

- [x] One or more `PlayEndToEndTests` cases build the real session and the real `PlaybackViewModel` with a substitute renderer and fake audio, press the keys, and assert that the AC-11 limit note and the AC-08 "No next file" note reach `IVideoRenderer.ShowNote` with the spec's text. They fail if the view model stops forwarding notes.
- [x] Production code changes only if the test finds a real gap (then name it in the commit).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
