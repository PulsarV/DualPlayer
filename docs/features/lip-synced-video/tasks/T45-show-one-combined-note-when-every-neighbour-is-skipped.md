---
id: T45
title: "Show one combined note when every neighbour is skipped"
layer: "app"
deps: ["T44"]
blocks: []
acs: ["AC-08", "AC-09"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/ux-flows.md", "docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/contracts/cli.md", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21 (R14)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T45 — Show one combined note when every neighbour is skipped

## Why

R14 (stage-1, AC-08/AC-09): when every neighbour is skipped, the skip note is published and at once replaced by "No next file" (`PlaybackSession.cs:442-455`), so it is never visible. `test-plan.md:127` expects "one note with the last skipped name and the count"; `ux-flows.md:98` says "it ends in 'no next file'". Owner decision (review-2026-11-21): one combined note — "No next file (skipped N; last: X – reason)" (and the previous-file equivalent).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09.

## Definition of Done

- [x] When every following (preceding) file is skipped, exactly one note is shown: `No next file (skipped N; last: <name> – <reason>)` / `No previous file (…)`.
- [x] test-plan.md, ux-flows.md (and screens.md SCR-03 / cli.md if they list the note text) are aligned to the combined note.
- [x] Test: `When_every_following_file_is_skipped…` asserts the single visible note text (last note shown), for next and previous.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
