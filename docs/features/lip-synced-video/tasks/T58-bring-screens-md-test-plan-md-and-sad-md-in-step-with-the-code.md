---
id: T58
title: "Bring screens.md, test-plan.md and sad.md in step with the code"
layer: "docs"
deps: ["T49", "T50", "T55"]
blocks: []
acs: []
files_hint: ["docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/sad.md", "tests/DualPlayer.App.Tests/Startup/AppEntryTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21b (Q17)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T58 — Bring screens.md, test-plan.md and sad.md in step with the code

## Why

Q17: SCR-03 (`screens.md:105-112`) lacks the notes "Could not go back: …" and "Could not play <file>: …" (`PlaybackSession.cs:529, 895`); `test-plan.md:112` and the comment at `AppEntryTests.cs:143` still call two App-entry rows "red tests" waiting for a seam, though the seam exists and the tests pass; `screens.md:21` and `sad.md:510` still flag a US-02 wording problem that `ux-flows.md:64` already fixed.

## Acceptance criteria

n/a (documentation).

## Definition of Done

- [x] SCR-03 lists every note the session shows (incl. the two failure notes and any note T49 adds), with trigger and style.
- [x] test-plan.md row 112 names the App-entry tests; the stale test comment is removed; the stale US-02 notes in screens.md and sad.md are removed; test-plan rows reflect T48–T57's new tests.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
